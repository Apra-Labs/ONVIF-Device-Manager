# H.265/HEVC Support

Delivered in Sprint 4 (squash-merged as commit `b7ee223`).

---

## What Was Built

End-to-end H.265/HEVC video stream playback in ODM, covering all five layers of the video pipeline:

1. ONVIF schema and C# type bindings
2. Live555 H.265 RTP depayloader
3. FFmpeg HEVC decoder
4. C++ player pipeline (codec dispatch + NAL framing)
5. F# UI activity (encoder settings)

---

## Five Blocking Gaps Fixed

| Gap | Layer | Problem | Fix |
|-----|-------|---------|-----|
| 1 | FFmpeg | avcodec-54 (2012) has no HEVC decoder — `AV_CODEC_ID_HEVC` did not exist | Upgraded to FFmpeg n7.1-lgpl-shared (avcodec-61) |
| 2 | live555 | 2013 build has no `H265VideoRTPSource` RTP depayloader | Upgraded live555; added `H265VirtualSink` |
| 3 | C++ pipeline | `InitSubsession()` had no branch for `"H265"` codec name — stream silently dropped | Added H265 branch → `AV_CODEC_ID_HEVC` |
| 4 | ONVIF types | `VideoEncoding` enum had no `h265` value; `H265Configuration`, `H265Profile`, `H265Options` types missing | Added all missing types to `onvif.types.cs` and both XSD copies |
| 5 | F# UI | `VideoSettingsActivity.fs` match expression threw `ArgumentException` on unknown encoding | Added `h265` case to all encoder-dispatch match expressions |

---

## FFmpeg Upgrade

**From:** ffmpeg-git-a5c1a0c (libavcodec-54, 2012)  
**To:** FFmpeg n7.1-lgpl-shared

New DLL versions checked into `build/` and staged in CI:

| DLL | Version |
|-----|---------|
| `avcodec-61.dll` | 61 |
| `avformat-61.dll` | 61 |
| `avutil-59.dll` | 59 |
| `swscale-8.dll` | 8 |
| `swresample-5.dll` | 5 |

**API migrations required:**
- `av_register_all()` / `avcodec_register_all()` — removed in FFmpeg 4.0; calls removed
- `CODEC_ID_*` constants renamed to `AV_CODEC_ID_*` throughout `Live555.cpp` and `VideoDecoder.hpp`
- `avcodec_alloc_context()` → `avcodec_alloc_context3()`
- `avcodec_open()` → `avcodec_open2()`
- `avcodec_alloc_frame()` → `av_frame_alloc()`

---

## live555 H265VideoRTPSource

The upgraded live555 includes `H265VideoRTPSource` (added to live555 circa 2014). live555 automatically creates the correct `RTPSource` subclass based on the SDP codec name — no application code changes are required for RTP depayloading once the library is updated.

---

## H265VirtualSink (NAL framing)

**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/H265VirtualSink.hpp`

H.265 RTP payloads arrive as raw NAL units without Annex-B start codes. FFmpeg's HEVC decoder expects Annex-B format (`0x00 0x00 0x00 0x01` prefix). `H265VirtualSink` prepends the 4-byte start code before each NAL unit — identical logic to `H264VirtualSink`.

`SetupSubsession()` in `Live555.cpp` selects:
- `H264VirtualSink` for H.264 streams
- `H265VirtualSink` for H.265 streams
- `VirtualSink` (raw passthrough) for all other codecs

---

## CODEC_FLAG2_CHUNKS for HEVC

`VideoDecoder.hpp` sets `CODEC_FLAG2_CHUNKS` for both `AV_CODEC_ID_H264` and `AV_CODEC_ID_HEVC`. This flag tells FFmpeg to handle incomplete frames gracefully — necessary because RTP delivery may split a NAL unit across multiple packets.

---

## ONVIF Type Additions

All additions are in `onvif/onvif.services/onvif.types.cs` and mirrored to both XSD copies (`schemas/onvif.xsd` and `Service References/services/onvif.xsd`):

| Type | What it is |
|------|-----------|
| `VideoEncoding.h265` | New enum member (`XmlEnum("H265")`) added after `h264` |
| `H265Profile` | Enum: `main`, `main10`, `mainStillPicture` (ONVIF 17.06+) |
| `H265Configuration` | Class with `govLength` and `h265Profile` properties |
| `H265Options` | Class with resolution, govLength, frameRate, encodingInterval, and H265 profile arrays |
| `H265Options2` | Extended options including bitrateRange |
| `VideoEncoderConfiguration.h265` | New property of type `H265Configuration` |
| `VideoEncoderConfigurationOptions.h265` | New property of type `H265Options` |
| `VideoEncoderOptionsExtension.h265` | New property of type `H265Options2` |

These types are hand-maintained (not auto-generated from WSDL). They follow the exact same patterns as the adjacent H.264 types.

---

## Media2 Detection Hack (Issue #21)

Some cameras advertise H.265 profiles only via the ONVIF Media2 service (`GetVideoEncoderConfigurations` on `onvif10_media2`) rather than the Media1 service. A workaround was added in `NvtSession.fs` — `GetVideoEncoderConfigurationsMedia2` — that calls the Media2 endpoint when present.

Issue #21 tracks proper Media2 support as a follow-up sprint.

---

## F# UI Activity Changes (`VideoSettingsActivity.fs`)

All codec match expressions that previously covered only `jpeg`, `mpeg4`, `h264` were extended with an `h265` case:

- Frame rate range extraction from `options.h265`
- Encoding interval range from `options.h265`
- GOV length range from `options.h265`
- GOV length read from `vec.h265.govLength`
- Bitrate range from `H265Options2.bitrateRange`
- Encoder configuration creation: builds `H265Configuration` with `govLength`

Without these additions, selecting an H.265 profile in the video settings UI would throw `ArgumentException` on the default `|_` branch.
