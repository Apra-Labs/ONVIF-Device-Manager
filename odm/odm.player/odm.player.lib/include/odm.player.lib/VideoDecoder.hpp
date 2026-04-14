#pragma once
#include <ctime>
#include "odm.player.lib/all.h"

namespace onvifmp{

	class VideoDecoder : public IFrameProcessor{
	public:
		typedef function<shared_ptr<VideoDecoder> (VirtualSink* sink)> Factory;
		static Factory Create(AVCodecID codecId, const char* sporps){
			return ([=](VirtualSink* sink)->shared_ptr<VideoDecoder>{
				// av_register_all() and avcodec_register_all() were removed in FFmpeg 4.0.
				// Codecs and formats are now registered automatically.

				auto avCodec = avcodec_find_decoder(codecId);
				if (avCodec == NULL) {
					return nullptr;
				}
				auto videoDecoder = make_shared<VideoDecoder>();
				if(!videoDecoder->Init(sink, avCodec, sporps)){
					return nullptr;
				}
				return videoDecoder;
			});
		}

		VideoDecoder(){
			avCodec = NULL;
			avCodecContext = NULL;
			avFrame = NULL;
			frameBuffer = NULL;
			extraDataSize = 0;
		}

		~VideoDecoder(){
			Cleanup();
		}

		bool Init(VirtualSink* sink, const AVCodec* avCodec, const char* sprops){
			//if it has been initialized before, we should do cleanup first
			Cleanup();

			// avcodec_alloc_context3 replaces deprecated avcodec_alloc_context (removed in FFmpeg 4.0)
			avCodecContext = avcodec_alloc_context3(avCodec);
			if (!avCodecContext) {
				//failed to allocate codec context
				Cleanup();
				return false;
			}
			uint8_t startCode[] = {0x00, 0x00, 0x01};
			if(sprops != NULL){
				unsigned spropCount;
				SPropRecord* spropRecords = parseSPropParameterSets(sprops, spropCount);
				try{
					for (unsigned i = 0; i < spropCount; ++i) {
						AddExtraData(startCode, sizeof(startCode));
						AddExtraData(spropRecords[i].sPropBytes, spropRecords[i].sPropLength);
					}
				}catch(...){
					//extradata exceeds size limit
					delete[] spropRecords;
					Cleanup();
					return false;
				}
				delete[] spropRecords;

				avCodecContext->extradata = extraDataBuffer;
				avCodecContext->extradata_size = extraDataSize;
			}
			AddExtraData(startCode, sizeof(startCode));
			avCodecContext->flags = 0;
			// AV_CODEC_FLAG2_CHUNKS must be set before avcodec_open2 so the codec's
			// init callback can enable its internal NAL-chunk parser (affects HEVC).
			if (avCodec->id == AV_CODEC_ID_H264 || avCodec->id == AV_CODEC_ID_HEVC){
				avCodecContext->flags2 |= AV_CODEC_FLAG2_CHUNKS;
			}

			// avcodec_open2 replaces deprecated avcodec_open (removed in FFmpeg 4.0)
			if (avcodec_open2(avCodecContext, avCodec, NULL) < 0) {
				//failed to open codec
				Cleanup();
				return false;
			}
			// av_frame_alloc replaces deprecated avcodec_alloc_frame (removed in FFmpeg 4.0)
			avFrame = av_frame_alloc();
			if (!avFrame){
				//failed to allocate frame
				Cleanup();
				return false;
			}
			return true;
		}

		void Cleanup(){
			extraDataSize = 0;
			if (avFrame != NULL){
				// av_frame_free replaces av_free for AVFrame (frees the frame and its data)
				av_frame_free(&avFrame);
				avFrame = NULL;
			}
			if(avCodecContext != NULL){
				// avcodec_free_context replaces avcodec_close + av_free for AVCodecContext
				avcodec_free_context(&avCodecContext);
				avCodecContext = NULL;
			}
		}

		///<summary></summary>
		///<param name="factory"></param>
		///<returns></returns>
		void AddVideoRenderer(IVideoRendererFactory factory){
			if(videoRenderer!=nullptr){
				videoRenderer->Dispose();
				videoRenderer = NULL;
			}
			if(factory!=nullptr){
				videoRenderer = factory(this);
			}
		}

	protected:
		const AVCodec* avCodec;
		AVCodecContext* avCodecContext;
		AVFrame* avFrame;
		char* frameBuffer;
		int extraDataSize;
		static const int MaxExtraDataSize = 1024;
		uint8_t extraDataBuffer[MaxExtraDataSize];
		shared_ptr<IVideoRenderer> videoRenderer;

		void AddExtraData(uint8_t* data, int size){
			auto newSize = extraDataSize+size;
			if(newSize > MaxExtraDataSize){
				throw "extradata exceeds size limit";
			}
			memcpy(extraDataBuffer + extraDataSize, data, size);
			extraDataSize = newSize;
		}

		virtual void ProcessFrame(unsigned char* framePtr, int frameSize, struct timeval presentationTime, unsigned duration){
			// New send/receive API replaces deprecated avcodec_decode_video2 (removed in FFmpeg 3.1+)
			AVPacket* avpkt = av_packet_alloc();
			if (!avpkt) {
				return;
			}
			avpkt->data = framePtr;
			avpkt->size = frameSize;
			// Make a ref-counted copy so the HEVC frame-threaded decoder can safely
			// hold a reference past this call without aliasing live555's receive buffer.
			av_packet_make_refcounted(avpkt);

			int ret = avcodec_send_packet(avCodecContext, avpkt);
			av_packet_free(&avpkt);

			if (ret < 0) {
				//TODO: log error
				return;
			}

			while (ret >= 0) {
				ret = avcodec_receive_frame(avCodecContext, avFrame);
				if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
					break;
				}
				if (ret < 0) {
					//TODO: log error
					return;
				}
				if(videoRenderer!=nullptr){
					videoRenderer->RenderFrame(avCodecContext, avFrame);
				}
			}
			//dbg::Info(sys::String::Format("processed in {0}ms",clock()-started));
		}
		virtual void Dispose(){
			//
		}
	};
}
