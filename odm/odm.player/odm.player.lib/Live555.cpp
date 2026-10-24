#include "odm.player.lib/all.h"
#include "odm.player.lib/PlaybackPolicy.hpp"
#include "GroupsockHelper.hh"
#include "io.h"
#include <stdlib.h>
#include <stdio.h>
#include <fcntl.h>
#include <iostream>

namespace onvifmp{
	using namespace std;

	Live555::Live555(){
		//authenticator = nullptr;
		scheduler = nullptr;
		usageEnvironment = nullptr;
		rtspClient = nullptr;
		mediaSession = nullptr;
		
		videoSubsessionInitialized = false;
		metadataSubsessionInitialized = false;

		packetReorderingThresholdTime = -1;
		lastFailure = 0;
	}

	Live555::~Live555(){
	}

	void Live555::SetVideoOutput(shared_ptr<VideoBuffer> videoBuffer){
		this->videoBuffer = videoBuffer;
	}

	void Live555::SetMetadataOutput(MetadataCollector::Listener metadataListener){
		this->metadataListener = metadataListener;
	}

	static void OpenConsole()
	{
		int outHandle, errHandle, inHandle;
		FILE *outFile, *errFile, *inFile;
		AllocConsole();
		CONSOLE_SCREEN_BUFFER_INFO coninfo;
		GetConsoleScreenBufferInfo(GetStdHandle(STD_OUTPUT_HANDLE), &coninfo);
		coninfo.dwSize.Y = 9999;
		SetConsoleScreenBufferSize(GetStdHandle(STD_OUTPUT_HANDLE), coninfo.dwSize);

		outHandle = _open_osfhandle((long)GetStdHandle(STD_OUTPUT_HANDLE), _O_TEXT);
		errHandle = _open_osfhandle((long)GetStdHandle(STD_ERROR_HANDLE), _O_TEXT);
		inHandle = _open_osfhandle((long)GetStdHandle(STD_INPUT_HANDLE), _O_TEXT);

		outFile = _fdopen(outHandle, "w");
		errFile = _fdopen(errHandle, "w");
		inFile = _fdopen(inHandle, "r");

		*stdout = *outFile;
		*stderr = *errFile;
		*stdin = *inFile;

		setvbuf(stdout, NULL, _IONBF, 0);
		setvbuf(stderr, NULL, _IONBF, 0);
		setvbuf(stdin, NULL, _IONBF, 0);

		std::ios::sync_with_stdio();

	}

	void Live555::Run(MediaStreamInfo* mediaStreamInfo, IPlaybackController* playbackController){
#ifdef DEBUG
		OpenConsole();
#endif // DEBUG

		if(!Init(mediaStreamInfo)){
			return;
		}
		//this.playbackController = controller;
			
		struct PlaybackSession: IPlaybackSession{
			PlaybackSession(){
				disposed = false;
				stop = nullptr;
			}
			volatile bool disposed;
			volatile char* stop;			//(the event loop's watch variable: set, the loop ends)
			virtual void Dispose(){
				disposed = true;
				if(stop != nullptr){
					*stop = 1;
				}
			}
		}playbackSession;

		if(!playbackController->Initialized(&playbackSession)){
			Cleanup();
			return;
		}
		
		// Keeping the session alive and watching it - live555 does neither by itself - and opening
		// it again when it stalls (the decisions are in PlaybackPolicy.hpp):
		// - cameras end an RTSP session after its timeout (usually 60 s) unless the client says
		//   something in between, so a keep-alive goes out every half timeout;
		// - when video stops arriving for 10 s (the camera went away, or dropped the session
		//   anyway), the session is closed and opened again, after a growing delay - but not once
		//   the camera has refused the credentials. ODM doesn't restart a player that has reported
		//   its end, so it isn't told: the picture just carries on once the stream is back.
		for(;;){
			rtspClient->playMediaSession(*mediaSession);

			struct Watch{
				Live555* player;
				MediaStreamInfo* streamInfo;
				char stop;
				bool hasVideo;
				unsigned videoPackets;
				int secondsWithoutVideo;
				int keepAliveEvery, sinceKeepAlive;
				TaskToken tick;
			}watch;
			watch.player = this;
			watch.streamInfo = mediaStreamInfo;
			watch.stop = 0;
			watch.hasVideo = false;
			{
				MediaSubsessionIterator itor(*mediaSession);
				for(auto subsession = itor.next(); subsession != nullptr; subsession = itor.next()){
					if(subsession->rtpSource() != nullptr && strcmp(subsession->mediumName(), "video") == 0){
						watch.hasVideo = true;
					}
				}
			}
			watch.videoPackets = 0;
			watch.secondsWithoutVideo = 0;
			watch.keepAliveEvery = policy::KeepAliveInterval(rtspClient->sessionTimeoutParameter());
			watch.sinceKeepAlive = 0;
			playbackSession.stop = &watch.stop;
			if(playbackSession.disposed){
				watch.stop = 1;
			}

			// (whether this camera has refused GET_PARAMETER; one player per player host process)
			static volatile bool getParameterRefused = false;
			static TaskFunc* const tick = [](void* data){
				auto w = static_cast<Watch*>(data);
				auto self = w->player;
				w->tick = nullptr;
				unsigned packets = 0;
				MediaSubsessionIterator itor(*self->mediaSession);
				for(auto subsession = itor.next(); subsession != nullptr; subsession = itor.next()){
					if(subsession->rtpSource() != nullptr && strcmp(subsession->mediumName(), "video") == 0){
						packets += subsession->rtpSource()->receptionStatsDB().totNumPacketsReceived();
					}
				}
				if(packets != w->videoPackets){
					w->videoPackets = packets;
					w->secondsWithoutVideo = 0;
				}else{
					++w->secondsWithoutVideo;
				}
				if(policy::Stalled(w->hasVideo, w->videoPackets, w->secondsWithoutVideo)){
					w->stop = 1;
					return;
				}
				if(++w->sinceKeepAlive >= w->keepAliveEvery){
					w->sinceKeepAlive = 0;
					if(policy::KeepAliveMethod(getParameterRefused) == policy::KeepAlive::GetParameter){
						auto done = [](RTSPClient*, int resultCode, char* resultString){
							if(policy::RefusesGetParameter(resultCode)){
								getParameterRefused = true;
							}
							delete[] resultString;
						};
						self->rtspClient->sendGetParameterCommand(*self->mediaSession, done, nullptr, w->streamInfo->authenticator);
					}else{
						auto done = [](RTSPClient*, int, char* resultString){ delete[] resultString; };
						self->rtspClient->sendOptionsCommand(done, w->streamInfo->authenticator);
					}
				}
				w->tick = self->scheduler->scheduleDelayedTask(1000000, tick, w);
			};
			watch.tick = scheduler->scheduleDelayedTask(1000000, tick, &watch);

			scheduler->doEventLoop(&watch.stop);
			playbackSession.stop = nullptr;
			if(watch.tick != nullptr){
				scheduler->unscheduleDelayedTask(watch.tick);
			}
			rtspClient->teardownMediaSession(*mediaSession);
			Cleanup();
			if(playbackSession.disposed){
				break;
			}

			// the stream stalled while the view is still open: open it again
			bool reopened = false;
			for(int attempt = 1; !playbackSession.disposed && !reopened; ++attempt){
				for(int i = 0; i < policy::ReconnectDelay(attempt) * 10 && !playbackSession.disposed; ++i){
					Sleep(100);
				}
				if(playbackSession.disposed){
					break;
				}
				reopened = Init(mediaStreamInfo);
				if(!reopened && !policy::RetryAfter(lastFailure)){
					break;					//(the credentials were refused: don't keep trying them)
				}
			}
			if(!reopened){
				if(!playbackSession.disposed){
					playbackController->Shutdown();
				}
				break;
			}
		}
	}


	IFrameProcessorFactory Live555::InitVideoSubsession(AVCodecID codecId, const char* sprops){
		//dbg::Info(sys::String::Format("processing subsession for {0}",gcnew sys::String(codecName)));
		if(videoBuffer == nullptr || videoSubsessionInitialized){
			return nullptr;
		}
		videoSubsessionInitialized = true;
		auto videoDecoderFactory = VideoDecoder::Create(codecId, sprops);
		auto videoRendererFactory = VideoRenderer::Create(videoBuffer);
		return ([=](VirtualSink* sink)->shared_ptr<IFrameProcessor>{
			auto videoDecoder = videoDecoderFactory(sink);
			videoDecoder->AddVideoRenderer(videoRendererFactory);
			return videoDecoder;
		});
	}

	IFrameProcessorFactory Live555::InitMetadataSubsession(const char* sprops){
		//auto sink = MetaSink::Create(*env, buf_size, pLive->mEvent, pLive->mCallback);
		if(metadataListener == nullptr || metadataSubsessionInitialized){
			return nullptr;
		}
		metadataSubsessionInitialized = true;
		auto metadataCollectorFactory = MetadataCollector::Create();
		return ([=](VirtualSink* sink)->shared_ptr<IFrameProcessor>{
			auto metadataCollector = metadataCollectorFactory(sink);
			metadataCollector->AddListener(this->metadataListener);
			return metadataCollector;
		});
	}
		
	IFrameProcessorFactory Live555::InitSubsession(const char* codecName, const char* sprops){
		if(_stricmp(codecName, "META")==0){
			return InitMetadataSubsession(sprops);
		}else if (_stricmp(codecName, "vnd.onvif.metadata")==0){
			return InitMetadataSubsession(sprops);
		}else if (_stricmp(codecName, "JPEG")==0){
			return InitVideoSubsession(AV_CODEC_ID_MJPEG, sprops);
		}else if (_stricmp(codecName, "H264")==0){
			return InitVideoSubsession(AV_CODEC_ID_H264, sprops);
		}else if (_stricmp(codecName, "H265")==0){
			return InitVideoSubsession(AV_CODEC_ID_HEVC, sprops);
		}else if (_stricmp(codecName, "MPEG4")==0){
			return InitVideoSubsession(AV_CODEC_ID_MPEG4, sprops);
		}else if (_stricmp(codecName, "MP4V-ES")==0){
			return InitVideoSubsession(AV_CODEC_ID_MPEG4, sprops);
		}else if (_stricmp(codecName, "MPV") == 0){
			return InitVideoSubsession(AV_CODEC_ID_MPEG2VIDEO, sprops);
		}

		return nullptr;
	}
		
	void Live555::SetupSubsession(MediaStreamInfo* mediaStreamInfo, MediaSubsession* subsession){
		auto rtpSource = subsession->rtpSource();
		if(rtpSource==NULL){
			//dbg::Info("rtpSource is NULL");
			return;
		}
		if(packetReorderingThresholdTime != -1){
			rtpSource->setPacketReorderingThresholdTime(
				packetReorderingThresholdTime
			);
		}
		auto streamUsingTcp = mediaStreamInfo->transport != StreamTransport::Udp;
		//rtspClient->setupMediaSubsession(*subsession, false, false, false);
		auto codecName = subsession->codecName();
		// H264: fmtp_spropparametersets() carries SPS/PPS as base64 pairs.
		// H265: RFC 7798 uses sprop-vps, sprop-sps, sprop-pps instead, which
		//       live555-2013 does not expose via MediaSubsession accessors.
		//       TODO(#20): add fmtp_spropvps/sps/pps to live555 MediaSubsession and
		//       combine them into the sprops string here.  Until then, cameras that
		//       do not inline VPS/SPS/PPS in-band before the first IDR will produce
		//       a black frame until the next keyframe.
		auto sprops = subsession->fmtp_spropparametersets();
		auto frameProcessorFactory = InitSubsession(codecName, sprops);
		if(frameProcessorFactory!=nullptr){
			rtspClient->setupMediaSubsession(*subsession, false, streamUsingTcp, false);
			VirtualSink* sink;
			if(_stricmp(codecName, "H264")==0){
				//create special sink to fix H264 payload format
				sink = H264VirtualSink::CreateNew(*usageEnvironment);
				//sink = VirtualSink::CreateNew(*usageEnvironment);
			}else if(_stricmp(codecName, "H265")==0){
				sink = H265VirtualSink::CreateNew(*usageEnvironment);
			}else{
				sink = VirtualSink::CreateNew(*usageEnvironment);
			}
			
			//TODO: increase receiver buffer size for HD videos
			increaseReceiveBufferTo(*usageEnvironment, rtpSource->RTPgs()->socketNum(), 5000*1024);

			sink->AddFrameProcessor(frameProcessorFactory);
			subsession->sink = sink;
			auto source = subsession->readSource();
			//dbg::Info(sys::String::Format("setup subsession for {0}",gcnew sys::String(codecName)));
			sink->startPlaying(*source, 0, 0);
		}
	}

	bool Live555::Init(MediaStreamInfo* mediaStreamInfo){
		//if it has been initialized before, we should do cleanup first
		Cleanup();
		lastFailure = 0;

		scheduler = BasicTaskScheduler::createNew();
		if(scheduler == NULL){
			return false;
		}
		usageEnvironment = BasicUsageEnvironment::createNew(*scheduler);
		if(usageEnvironment == NULL){
			Cleanup();
			return false;
		}
		auto httpProt = 0U;
		
		if(mediaStreamInfo->transport == StreamTransport::Http){
			char* username = nullptr;
			char* password = nullptr;
			NetAddress address;
			portNumBits port;
			if(!RTSPClient::parseRTSPURL(*usageEnvironment, mediaStreamInfo->url, username, password, address, port)){
				//TODO: log error
				fprintf(stderr, "failed to parse url with error message: %s\n", usageEnvironment->getResultMsg());
				if(username != nullptr){
					delete[] username;
				}
				if(password != nullptr){
					delete[] password;
				}
				Cleanup();
				return false;
			}
			httpProt = port;
			delete[] username;
			delete[] password;
		}
		rtspClient = RTSPClient::createNew(*usageEnvironment, nullptr, 1/*verbosityLevel*/, nullptr/*applicationName*/, httpProt);
		if(rtspClient == NULL){
			//TODO: log error
			fprintf(stderr, "error %d: %s\n", usageEnvironment->getErrno(), usageEnvironment->getResultMsg());
			Cleanup();
			return false;
		}

		//rtspClient->fCurrentAuthenticator = mediaStreamInfo->authenticator;
		auto options = rtspClient->sendOptionsCmd(mediaStreamInfo->url, nullptr, nullptr, mediaStreamInfo->authenticator, 5/*timeout in seconds*/);
		rtspOptions.getParamSupported = (options!= nullptr && strstr(options, "GET_PARAMETER")!=nullptr);

		//should we take care of release sdp string???
		auto sdp = rtspClient->describeURL(mediaStreamInfo->url, mediaStreamInfo->authenticator,0U, 5/*timeout in seconds*/);
		if(sdp == NULL){
			//TODO: log error
			fprintf(stderr, "error %d: %s", usageEnvironment->getErrno(), usageEnvironment->getResultMsg());
			//(the camera's answer, e.g. "... 401 Unauthorized": the reconnect stops on refused credentials)
			lastFailure = strstr(usageEnvironment->getResultMsg(), "401") != nullptr ? 401 : 0;
			Cleanup();
			return false;
		}

		mediaSession = MediaSession::createNew(*usageEnvironment, sdp);
		if(mediaSession == NULL){
			//TODO: log error
			fprintf(stderr, "error %d: %s", usageEnvironment->getErrno(), usageEnvironment->getResultMsg());
			delete[] sdp;
			Cleanup();
			return false;
		}
		delete[] sdp;
		sdp = nullptr;

		MediaSubsessionIterator itor(*mediaSession);
		auto subsession = itor.next();
		while(subsession != NULL){
			//subsession->setClientPortNum(-1);
			if(subsession->initiate(0)){
				SetupSubsession(mediaStreamInfo, subsession);
			}
			subsession = itor.next();
		}
		return true;
	}

	void Live555::Cleanup(){
		if(mediaSession != nullptr){
			MediaSubsessionIterator itor(*mediaSession);
			auto subsession = itor.next();
			while(NULL != subsession) {
				//auto rtpSource = subsession->rtpSource();
				//if(rtpSource != nullptr){
				//	rtpSource->stopGettingFrames();
				//}
				if(subsession->sink != NULL){
					subsession->sink->stopPlaying();
					MediaSink::close(subsession->sink);
					subsession->sink = NULL;
				}
				subsession->deInitiate();
				subsession = itor.next();
			}
			MediaSession::close(mediaSession);
			mediaSession = nullptr;
		}
		if(rtspClient != nullptr){
			RTSPClient::close(rtspClient);
			rtspClient = nullptr;
		}
		if(usageEnvironment != nullptr){
			usageEnvironment->reclaim();
			usageEnvironment = nullptr;
		}
		if(scheduler != nullptr){
			delete scheduler;
			scheduler = nullptr;
		}
		videoSubsessionInitialized = false;
		metadataSubsessionInitialized = false;
	}

	void Live555::WriteLog(){
		//char buf[255];
		//const char *source = NULL;

		////work around // we have %td from av_codec
		//string msg(aMsg);
		//size_t pos = std::string::npos;
		//while (std::string::npos != (pos = msg.find("%td")))
		//msg.replace(pos, 3, "%ld");
		////work around // we have %td from av_codec

		//vsprintf_s<255>(buf, msg.c_str(), aArgs);
		//if (aClass)
		//{
		//AVClass* avc= aClass ? *(AVClass**)aClass : NULL;
		//if (avc)
		//source = avc->item_name(aClass);
		//}
		//pLive->mInstance.Log(buf, source, (AV_LOG_FATAL == aLevel || AV_LOG_ERROR == aLevel) ?
		//LOG_ERROR : (AV_LOG_WARNING == aLevel ? LOG_WARNING : LOG_INFORMATION));
	}
}