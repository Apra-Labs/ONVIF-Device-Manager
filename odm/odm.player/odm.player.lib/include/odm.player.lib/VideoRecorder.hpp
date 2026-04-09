#pragma once
#include "odm.player.lib/all.h"

class TSWriter {
public:
  TSWriter(const std::string& aFilePath, int aBitRate, int aWidth, int aHeight,
    int aFrameRate, AVPixelFormat aPixFmt);
  ~TSWriter();

  bool write_picture(AVFrame *aPicture);

  bool hasError() { return mHasError; }
  std::string getError() { return mErrorMsg; }

  double getPTS() {
    // AVStream::pts was removed in FFmpeg 4.0+; stream PTS must be tracked from packets
    return -1.00;
  }
  int getTicksPerFrame() {
    // AVStream::codec was removed in FFmpeg 4.0+, replaced by AVStream::codecpar
    // (AVCodecParameters*) which does not expose ticks_per_frame; return safe default of 1
    return 1;
  }
private:
  AVStream* setup_video_stream();
  bool open_video();
  AVFrame *alloc_picture(AVPixelFormat pix_fmt, int width, int height);
  void close_video();

  //errors
  bool mHasError;
  std::string mErrorMsg;
  //inner data
  std::string mFilePath;
  int mBitRate, mWidth, mHeight, mFrameRate;
  AVPixelFormat mPixFmt;
  AVOutputFormat *mOutFormat;
  AVFormatContext *mFormatCtx;
  AVStream *mVideoStream;

  static const AVPixelFormat s_CodecPixFormat;

  struct PictureData {
    uint8_t *mOutBuf;
    int mOutBufSize;
    AVFrame *mTmpPicture;
  }mData;
};
