# Requirements — H265-ODM H.265/HEVC Support for ONVIF Device Manager

## Base Branch
`development` — branch to fork from and merge back to

## Goal
Enable H.265 (HEVC) video stream playback and display within ONVIF Device Manager (ODM), which currently only handles H.264 and MJPEG streams. Users with H.265-capable cameras cannot view live video from those cameras in ODM today.

## Scope
- Identify all code paths involved in video stream negotiation, decoding, and display
- Map the gaps: what currently blocks H.265 from working
- Produce a concrete PLAN.md with implementation tasks

## Out of Scope
- Re-encoding or transcoding H.265 → H.264 (decode natively)
- Adding cameras or third-party device profiles

## Constraints
- Windows target environment (primary)
- Must not break existing H.264 / MJPEG playback
- Must remain compatible with existing ONVIF profile negotiation

## Root Cause Analysis

### Architecture Overview
ODM is a C#/.NET WPF application (not Python) that uses a multi-process video pipeline:
1. **ONVIF negotiation** (C#/F#): `ContentController.cs` calls `GetStreamUri()` to get the RTSP URL from the camera profile
2. **RTSP/RTP transport** (C++, Live555): `Live555.cpp` connects to the RTSP URL, parses SDP, iterates media subsessions
3. **Codec detection** (C++): `Live555.cpp:InitSubsession()` maps RTP codec names to FFmpeg codec IDs
4. **NAL framing** (C++): `H264VirtualSink.hpp` adds start codes to H.264 RTP payloads
5. **Decoding** (C++, FFmpeg): `VideoDecoder.hpp` decodes frames using FFmpeg's avcodec
6. **Rendering** (C++→C#): `VideoRenderer.hpp` converts color space and writes to shared memory `VideoBuffer`, which WPF's `VideoPlayer.xaml.cs` renders via `WriteableBitmap`

### Four Blocking Gaps

**Gap 1 — FFmpeg too old (CRITICAL)**
- **Location:** `libs/ffmpeg-git-a5c1a0c/` — FFmpeg avcodec-54, built 2012-06-14
- **Problem:** `AV_CODEC_ID_HEVC` was introduced in avcodec-55 (late 2013). The bundled FFmpeg literally cannot decode H.265. There is no HEVC decoder in this build.
- **Evidence:** `grep -r "HEVC\|H265\|h265" libs/ffmpeg-git-a5c1a0c/include/` returns zero matches.

**Gap 2 — Live555 too old (CRITICAL)**
- **Location:** `libs/live555-2013.02.11/`
- **Problem:** `H265VideoRTPSource` (the H.265 RTP depayloader) was added to Live555 circa 2014. The bundled version (2013.02.11) cannot process H.265 RTP packets.
- **Evidence:** `grep -r "H265\|HEVC" libs/live555-2013.02.11/` returns zero matches.

**Gap 3 — No H.265 codec branch in pipeline (BLOCKING)**
- **Location:** `odm/odm.player/odm.player.lib/Live555.cpp:137-155`
- **Problem:** `InitSubsession()` handles JPEG, H264, MPEG4, MP4V-ES, MPV — but has no branch for `"H265"`. When an SDP contains an H.265 media subsession, the function returns `nullptr` (line 154), silently skipping it.
- **Also:** `SetupSubsession()` (line 176) only creates `H264VirtualSink` for H.264; no equivalent for H.265.

**Gap 4 — ONVIF types missing H.265 (BLOCKING)**
- **Location:** `onvif/onvif.services/onvif.types.cs:3618-3628`
- **Problem:** `VideoEncoding` enum has only `jpeg`, `mpeg4`, `h264`. No `h265` value.
- **Also missing:** `H265Configuration` class, `H265Profile` enum, `H265Options` class in `VideoEncoderConfigurationOptions`.
- **UI impact:** `VideoSettingsView.xaml.cs:159-188` and `VideoSettingsActivity.fs:93-266` only handle 3 codecs. H.265 cameras would show no encoder options.

### ONVIF Library Analysis
- **Not Python-based** — ODM uses WCF-generated C# service proxies from ONVIF WSDL schemas, not a Python ONVIF library
- **WSDL/XSD schemas** located in `onvif/onvif.services/schemas/` and `Service References/services/`
- **Types file** `onvif.types.cs` is a hand-maintained C# file (not auto-generated) defining ONVIF data contracts
- **H.265 types must be added manually** to `onvif.types.cs` — no library upgrade needed, just extending the existing type definitions
- The WCF service proxies (`Reference.cs`, `Reference1.cs`) are auto-generated but the core types are in the hand-maintained file

### Dependency Versions

| Component | Current | Needed | Gap |
|---|---|---|---|
| FFmpeg avcodec | 54.25 (2012-06) | >= 55.x (2013+), ideally 58+ | No HEVC decoder |
| Live555 | 2013.02.11 | >= 2015.01.01 | No H265VideoRTPSource |
| ONVIF types | Manual C# (pre-H.265) | Add H.265 types | Missing enum/classes |
| C++ pipeline | H.264/MJPEG/MPEG4 only | Add H.265 branches | Silent skip |

## Acceptance Criteria (for investigation phase)
- [x] PLAN.md produced with specific file locations, function names, and task breakdown
- [x] requirements.md updated with root cause analysis and implementation gaps
- [x] ONVIF library version identified and H.265 enum gap confirmed

## Acceptance Criteria (for implementation phase)
- [ ] FFmpeg upgraded to version with HEVC decoder support (avcodec >= 55)
- [ ] Live555 upgraded to version with H265VideoRTPSource
- [ ] `Live555.cpp:InitSubsession()` handles `"H265"` codec name -> `AV_CODEC_ID_HEVC`
- [ ] `H265VirtualSink` created for NAL unit framing
- [ ] `VideoDecoder.hpp` compiles with modern FFmpeg API and handles HEVC codec ID
- [ ] `VideoEncoding.h265` enum value added to `onvif.types.cs`
- [ ] `H265Configuration`, `H265Profile`, `H265Options` types added
- [ ] `VideoSettingsActivity.fs` handles H.265 in all codec filtering/application blocks
- [ ] `VideoSettingsView.xaml.cs` lists H.265 encoder options in dropdown
- [ ] H.265 RTSP stream plays in ODM video player
- [ ] Existing H.264 and MJPEG playback unaffected (regression test)
- [ ] Full solution builds cleanly on x64
