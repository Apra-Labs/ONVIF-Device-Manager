#pragma once
#include "odm.player.lib/all.h"

namespace onvifmp{

	class VideoRenderer : public IVideoRenderer{
	public:
		typedef function<shared_ptr<IVideoRenderer> (VideoDecoder* sink)> Factory;
		static Factory Create(shared_ptr<VideoBuffer> videoBuffer){
			return ([=](VideoDecoder* decoder)->shared_ptr<IVideoRenderer>{
				if(videoBuffer == nullptr){
					return nullptr;
				}
				return make_shared<VideoRenderer>(videoBuffer);
			});
		}

		VideoRenderer(shared_ptr<VideoBuffer> videoBuffer){
			this->videoBuffer = videoBuffer;
			shownWidth = shownHeight = 0;
		}

		~VideoRenderer(){
		}

	protected:
		shared_ptr<VideoBuffer> videoBuffer;
		AVPixelFormat videoBufferPixelFormat;
		int shownWidth, shownHeight;		//(the size last described in the video buffer's info)

		///<summary>tells the UI what is playing: the codec and the picture's own size</summary>
		void Describe(AVCodecContext* avCodecContext, AVFrame* avFrame){
			auto vb = videoBuffer.get();
			shownWidth = avFrame->width;
			shownHeight = avFrame->height;
			if(vb->info == nullptr || vb->infoSize <= 0){
				return;
			}
			const char* codec;
			switch(avCodecContext->codec_id){
				case AV_CODEC_ID_HEVC: codec = "H.265"; break;
				case AV_CODEC_ID_H264: codec = "H.264"; break;
				case AV_CODEC_ID_MJPEG: codec = "MJPEG"; break;
				case AV_CODEC_ID_MPEG4: codec = "MPEG-4"; break;
				default: codec = avcodec_get_name(avCodecContext->codec_id); break;
			}
			char text[64];
			_snprintf_s(text, sizeof(text), _TRUNCATE, "%s  %dx%d", codec, avFrame->width, avFrame->height);
			auto length = (int)strlen(text);
			if(length > vb->infoSize - 1){
				length = vb->infoSize - 1;
			}
			vb->info[length] = 0;					//(cut short first: the UI may be reading it)
			memcpy(vb->info, text, length);
		}

		virtual void RenderFrame(AVCodecContext* avCodecContext, AVFrame* avFrame){
			if(videoBuffer == nullptr){
				return;
			}
			auto vb = videoBuffer.get();
			if(avFrame->width != shownWidth || avFrame->height != shownHeight){
				Describe(avCodecContext, avFrame);
			}
			auto width = avCodecContext->width;
			auto height = avCodecContext->height;
			auto pixelFormat = avCodecContext->pix_fmt;
			auto stride = avFrame->linesize;
			auto scan0 = avFrame->data;
			auto scaleContext = sws_getContext(width, height, pixelFormat, vb->width, vb->height, vb->pixelFormat, SWS_FAST_BILINEAR,NULL,NULL,NULL);
			if (scaleContext == NULL){
				//TODO: log error
				return;
			}
			__try{
				*vb->signal = 1;
				sws_scale(scaleContext, scan0, stride, 0, height, vb->scan0, vb->stride);
			}__finally{
				sws_freeContext(scaleContext);
			}
		}
		virtual void Dispose(){
			//
		}
	};
}