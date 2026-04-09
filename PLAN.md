# H.265 ODM Support — Implementation Plan

## Overview

ODM currently cannot play H.265/HEVC streams because of **four layered gaps**: (1) the bundled FFmpeg (avcodec-54, June 2012) predates HEVC decoder support entirely, (2) the bundled Live555 (2013.02.11) predates H.265 RTP depayloading, (3) the C++ media pipeline (`Live555.cpp:137-155`) has no codec-name branch for `"H265"`, and (4) the ONVIF types layer (`onvif.types.cs:3618-3628`) defines `VideoEncoding` with only `jpeg/mpeg4/h264` — no `h265` enum value. The fix requires upgrading both native libraries, threading H.265 through the RTSP→decode→render pipeline, and extending the ONVIF type model + UI.

## Dependencies

| Dependency | Current Version | Required Version | Why |
|---|---|---|---|
| FFmpeg | avcodec-54 (2012-06-14 git-a5c1a0c) | ≥ avcodec-58 (FFmpeg 4.x+) | HEVC decoder (`AV_CODEC_ID_HEVC`) added in avcodec-55; modern builds include hardware-accelerated HEVC |
| Live555 | 2013.02.11 | ≥ 2015.01.01 | `H265VideoRTPSource` added circa 2014; required for H.265 RTP depayloading |

### FFmpeg API Migration Notes
The current code uses deprecated FFmpeg API calls that were removed in newer versions:
- `CodecID` → `AVCodecID` (enum renamed)
- `CODEC_ID_*` → `AV_CODEC_ID_*` (constant prefix changed)
- `avcodec_alloc_context()` → `avcodec_alloc_context3()`
- `avcodec_open()` → `avcodec_open2()`
- `avcodec_alloc_frame()` → `av_frame_alloc()`
- `avcodec_decode_video2()` → `avcodec_send_packet()` / `avcodec_receive_frame()`
- `av_register_all()` / `avcodec_register_all()` → no-ops in FFmpeg 4.x (auto-registered)
- `CODEC_FLAG2_CHUNKS` → `AV_CODEC_FLAG2_CHUNKS`
- `parseSPropParameterSets()` — still available via live555
- `avcodec_close()` + `av_free()` → `avcodec_free_context()`

---

## Phase 1 — Upgrade Native Libraries

### Task 1.1 — Upgrade FFmpeg binaries and headers
**File:** `libs/ffmpeg-git-a5c1a0c/` (entire directory)
**What:** Replace with FFmpeg 7.x (or latest LTS) shared builds for both win32 and x64. Download from https://github.com/BtbN/FFmpeg-Builds or build from source. Update:
- `libs/ffmpeg-git-a5c1a0c/include/` — new headers (libavcodec, libavformat, libavutil, libswscale)
- `libs/ffmpeg-git-a5c1a0c/win32/bin/` — new DLLs
- `libs/ffmpeg-git-a5c1a0c/x64/bin/` — new DLLs
- Rename directory to reflect new version (e.g., `libs/ffmpeg-7.x/`)
**Why:** Current avcodec-54 (2012) has no HEVC decoder. `AV_CODEC_ID_HEVC` was introduced in avcodec-55 (2013-2014).
**Done when:** `ffprobe -decoders | grep hevc` on the new FFmpeg binary shows HEVC decoder available.
**Type:** task

### Task 1.2 — Update FFmpeg linker references in vcxproj
**File:** `odm/odm.player/odm.player.net/odm.player.net.vcxproj:92-96`
**What:** Update `<AdditionalDependencies>` to match new FFmpeg library names (e.g., `avcodec.lib` instead of `avcodec-54.lib` if naming changed). Update `<AdditionalIncludeDirectories>` and `<AdditionalLibraryDirectories>` if the FFmpeg directory was renamed.
**Why:** Build will fail if lib references don't match new FFmpeg.
**Done when:** Project compiles against new FFmpeg headers and links against new libraries.
**Type:** task

### Task 1.3 — Update FFmpeg DLL copy rules in odm.ui.app.csproj
**File:** `odm/odm.ui.app/odm.ui.app.csproj` (the `<Content>` entries for FFmpeg DLLs)
**What:** Update all `<Content Include="..\..\libs\ffmpeg-git-a5c1a0c\win32\bin\avcodec-54.dll">` entries to reference new DLL names and paths. There are 8 DLLs: avcodec, avdevice, avfilter, avformat, avutil, postproc, swresample, swscale.
**Why:** Runtime will fail if the correct DLLs aren't copied to output directory.
**Done when:** All new FFmpeg DLLs are listed as Content and copy to `3rd/` output folder.
**Type:** task

### Task 1.4 — Upgrade Live555 library
**File:** `libs/live555-2013.02.11/` (entire directory)
**What:** Replace with Live555 ≥ 2015.01.01. Build as static library for Win32/x64. Key new header needed: `H265VideoRTPSource.hh`.
**Why:** Current Live555 (2013.02.11) cannot depayload H.265 RTP streams. `H265VideoRTPSource` was added later.
**Done when:** Live555 static lib builds cleanly and includes `H265VideoRTPSource.hh`.
**Type:** task

### Task 1.5 — VERIFY: Native libraries build and link
**What:** Full build of `odm.player.lib` and `odm.player.net` projects. Verify they compile and link against upgraded FFmpeg + Live555 without errors.
**Done when:** Clean build of both native projects succeeds on x64.
**Type:** verify

---

## Phase 2 — Migrate C++ Media Pipeline to New FFmpeg API

### Task 2.1 — Migrate VideoDecoder.hpp to modern FFmpeg API
**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/VideoDecoder.hpp`
**What:** Update all deprecated FFmpeg calls:
- Line 9: `Create(CodecID codecId, ...)` → `Create(AVCodecID codecId, ...)`
- Line 12: Remove `av_register_all()` / `avcodec_register_all()` (no-ops in FFmpeg 4.x+)
- Line 15: `avcodec_find_decoder(codecId)` — same function name, but param type changes to `AVCodecID`
- Line 43: `avcodec_alloc_context()` → `avcodec_alloc_context3(avCodec)`
- Line 72: `avcodec_open(avCodecContext, avCodec)` → `avcodec_open2(avCodecContext, avCodec, NULL)`
- Line 77: `CODEC_ID_H264` → `AV_CODEC_ID_H264`
- Line 78: `CODEC_FLAG2_CHUNKS` → `AV_CODEC_FLAG2_CHUNKS`
- Line 81: `avcodec_alloc_frame()` → `av_frame_alloc()`
- Lines 93-99: `avcodec_close()` + `av_free()` → `avcodec_free_context(&avCodecContext)`
- Lines 139-158: `avcodec_decode_video2()` → `avcodec_send_packet()` + `avcodec_receive_frame()` loop
- Line 117: `AVCodec*` → `const AVCodec*` (const-correctness in modern FFmpeg)
**Why:** Old API functions were removed in newer FFmpeg. Code won't compile without migration.
**Done when:** VideoDecoder.hpp compiles cleanly with new FFmpeg headers.
**Type:** task

### Task 2.2 — Update core.h type references
**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/core.h`
**What:**
- Line 17: Add `#include "H265VideoRTPSource.hh"` (if needed for H.265 RTP source)
- Ensure `PixelFormat` type references resolve correctly (FFmpeg renamed `PixelFormat` → `AVPixelFormat`)
- Line 116: `AVCodecContext*` / `AVFrame*` types should still work but verify
**Why:** Header compatibility with upgraded FFmpeg.
**Done when:** core.h compiles without errors.
**Type:** task

### Task 2.3 — Update VideoRenderer.hpp for AVPixelFormat
**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/VideoRenderer.hpp`
**What:** Replace any `PixelFormat` references with `AVPixelFormat` (FFmpeg renamed this enum). Verify `sws_getContext()` calls use updated enum values.
**Why:** `PixelFormat` was deprecated and removed in favor of `AVPixelFormat`.
**Done when:** VideoRenderer.hpp compiles cleanly.
**Type:** task

### Task 2.4 — VERIFY: C++ pipeline compiles with new API
**What:** Full build of odm.player.lib and odm.player.net. All deprecated API usage eliminated.
**Done when:** Clean build, zero warnings from deprecated FFmpeg usage.
**Type:** verify

---

## Phase 3 — Add H.265 Codec Path in Native Pipeline

### Task 3.1 — Add H.265 codec branch in Live555.cpp InitSubsession
**File:** `odm/odm.player/odm.player.lib/Live555.cpp:137-155`
**What:** Add H.265 codec name handling in `InitSubsession()`. Insert after the H264 branch (line 145):
```cpp
}else if (_stricmp(codecName, "H265")==0){
    return InitVideoSubsession(AV_CODEC_ID_HEVC, sprops);
```
The RTP codec name for H.265 is `"H265"` per RFC 7798.
**Why:** Without this branch, H.265 RTP subsessions from SDP are silently ignored (function returns `nullptr` at line 154).
**Done when:** When Live555 parses an SDP with `H265` codec, it routes to `InitVideoSubsession` with `AV_CODEC_ID_HEVC`.
**Type:** task

### Task 3.2 — Create H265VirtualSink for HEVC NAL unit framing
**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/H265VirtualSink.hpp` (new file)
**What:** Create `H265VirtualSink` class analogous to `H264VirtualSink.hpp`. H.265 uses the same 4-byte start code prefix (`0x00 0x00 0x00 0x01`) for NAL units as H.264. The class should:
- Inherit from `VirtualSink`
- Prepend start code to frames that lack one
- Check for both 3-byte and 4-byte start codes (same logic as H264VirtualSink)
- The NAL unit structure is identical in start-code-prefixed Annex B format
**Why:** H.265 RTP payloads may arrive without start codes (same issue as H.264). The sink corrects this.
**Done when:** H265VirtualSink.hpp exists and compiles.
**Type:** task

### Task 3.3 — Add H.265 sink selection in Live555.cpp SetupSubsession
**File:** `odm/odm.player/odm.player.lib/Live555.cpp:176-182`
**What:** Extend the sink selection block to handle H.265:
```cpp
if(_stricmp(codecName, "H264")==0){
    sink = H264VirtualSink::CreateNew(*usageEnvironment);
}else if(_stricmp(codecName, "H265")==0){
    sink = H265VirtualSink::CreateNew(*usageEnvironment);
}else{
    sink = VirtualSink::CreateNew(*usageEnvironment);
}
```
**Why:** H.265 needs NAL start code correction just like H.264.
**Done when:** H.265 subsessions use H265VirtualSink.
**Type:** task

### Task 3.4 — Add H.265 flags in VideoDecoder.hpp
**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/VideoDecoder.hpp:77-80`
**What:** Extend the codec-specific flag block to also handle HEVC:
```cpp
if (avCodecContext->codec_id == AV_CODEC_ID_H264 || avCodecContext->codec_id == AV_CODEC_ID_HEVC){
    avCodecContext->flags2 |= AV_CODEC_FLAG2_CHUNKS;
}
```
**Why:** HEVC benefits from the same chunked decoding flag as H.264.
**Done when:** HEVC decoder context has CHUNKS flag set.
**Type:** task

### Task 3.5 — Include H265VirtualSink.hpp in all.h
**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/all.h:132`
**What:** Add `#include "odm.player.lib/H265VirtualSink.hpp"` after the H264VirtualSink include.
**Why:** New header must be included in the compilation.
**Done when:** all.h includes the new header.
**Type:** task

### Task 3.6 — VERIFY: H.265 codec path compiles and links
**What:** Full build of odm.player.lib, odm.player.net, odm.player.host. Verify H.265 code path is reachable.
**Done when:** Clean build. Manual test with H.265 RTSP stream if available, or unit-level verification.
**Type:** verify

---

## Phase 4 — Extend ONVIF Type Model for H.265

### Task 4.1 — Add H265 to VideoEncoding enum
**File:** `onvif/onvif.services/onvif.types.cs:3618-3628`
**What:** Add `h265` value to the `VideoEncoding` enum:
```csharp
[System.Xml.Serialization.XmlEnumAttribute(Name = "H265")]
h265,
```
**Why:** ONVIF Profile S supports `VideoEncoding.H265`. Without this enum value, the XML deserializer will throw when a camera reports H.265 encoding.
**Done when:** `VideoEncoding.h265` is a valid enum member.
**Type:** task

### Task 4.2 — Add H265Configuration class
**File:** `onvif/onvif.services/onvif.types.cs` (after H264Configuration, ~line 3783)
**What:** Add `H265Configuration` class with `govLength` (int) and `h265Profile` (`H265Profile`) properties. Add `H265Profile` enum with values: `Main`, `Main10`, `MainStillPicture`. Follow the exact serialization pattern of `H264Configuration`:
```csharp
[System.SerializableAttribute()]
[System.Xml.Serialization.XmlTypeAttribute(TypeName = "H265Configuration", Namespace = "http://www.onvif.org/ver10/schema")]
public partial class H265Configuration {
    private int _govLength;
    private H265Profile _h265Profile;
    // ... properties with XmlElementAttribute ...
}

[System.SerializableAttribute()]
[System.Xml.Serialization.XmlTypeAttribute(TypeName = "H265Profile", Namespace = "http://www.onvif.org/ver10/schema")]
public enum H265Profile {
    [System.Xml.Serialization.XmlEnumAttribute(Name = "Main")]
    main,
    [System.Xml.Serialization.XmlEnumAttribute(Name = "Main10")]
    main10,
    [System.Xml.Serialization.XmlEnumAttribute(Name = "MainStillPicture")]
    mainStillPicture,
}
```
**Why:** ONVIF H.265 cameras return `H265Configuration` in their profile. Without this type, deserialization fails.
**Done when:** `H265Configuration` and `H265Profile` classes exist and serialize correctly.
**Type:** task

### Task 4.3 — Add h265 field to VideoEncoderConfiguration
**File:** `onvif/onvif.services/onvif.types.cs:3411-3437`
**What:** Add `private H265Configuration _h265;` field and corresponding property with `[XmlElementAttribute("H265", ...)]` attribute, following the pattern of `h264` field (lines 3429-3431).
**Why:** Camera profiles with H.265 encoding include an `H265` configuration block.
**Done when:** `VideoEncoderConfiguration.h265` property exists.
**Type:** task

### Task 4.4 — Add H265Options class and wire into VideoEncoderConfigurationOptions
**File:** `onvif/onvif.services/onvif.types.cs`
**What:**
1. Create `H265Options` class (after `H264Options`, ~line 6565) with same structure: `resolutionsAvailable`, `govLengthRange`, `frameRateRange`, `encodingIntervalRange`, `h265ProfilesSupported`. Follow the exact pattern of `H264Options` (lines 6483-6565).
2. Add `private H265Options _h265Options;` field and property to `VideoEncoderConfigurationOptions` (after `_h264`, ~line 6262).
3. Create `H265Options2` class (after `H264Options2`, ~line 6868) with `bitrateRange` property.
4. Add `H265Options2` field to `VideoEncoderOptionsExtension` (~line 6569).
**Why:** Without these, the UI cannot discover H.265-capable resolutions and settings from the camera.
**Done when:** All H265Options types exist and are wired into the options hierarchy.
**Type:** task

### Task 4.5 — VERIFY: ONVIF types compile
**What:** Build `onvif.services` project. Verify all new types are serializable.
**Done when:** Clean build. No XML serialization errors.
**Type:** verify

---

## Phase 5 — Update UI and Settings Activities for H.265

### Task 5.1 — Add H.265 to VideoSettingsActivity codec filtering
**File:** `odm/odm.ui.activities/VideoSettingsActivity.fs:93-136`
**What:** Add H.265 options to all the settings aggregation blocks:
- Lines 93-100: Add `if options.h265 |> NotNull then yield options.h265.frameRateRange`
- Lines 102-109: Add `if options.h265 |> NotNull then yield options.h265.encodingIntervalRange`
- Lines 111-116: Add `if options.h265 |> NotNull then yield options.h265.govLengthRange`
- Lines 118-123: Add `elif vec.encoding = VideoEncoding.h265 && NotNull(vec.h265) then vec.h265.govLength`
- Lines 126-135: Add `elif x.Name = @"H265" then yield x.Deserialize<H265Options2>().bitrateRange`
**Why:** Without these, H.265 encoder options won't populate the settings sliders.
**Done when:** Video settings UI shows H.265-specific ranges when camera reports H.265 support.
**Type:** task

### Task 5.2 — Add H.265 to apply_changes codec match
**File:** `odm/odm.ui.activities/VideoSettingsActivity.fs:248-266`
**What:** Extend the encoder-specific config block:
- After line 253, add:
```fsharp
elif model.encoder = VideoEncoding.h265 then
    if vec.h265 |> IsNull then vec.h265 <- new H265Configuration()
    vec.h265.govLength <- model.govLength |> CoerceGovLength(options.h265)
```
- In the match block (lines 261-266), add: `|VideoEncoding.h265 -> validateConfig(options.h265)`
**Why:** Without this, saving H.265 encoder settings would throw.
**Done when:** Applying H.265 video settings saves the config to the camera.
**Type:** task

### Task 5.3 — Add H.265 encoder/resolution display in VideoSettingsView
**File:** `odm/odm.ui.views/views/SectionNVT/VideoSettingsView.xaml.cs:171-188`
**What:** Add H.265 resolution enumeration in `GetEncoderResolutions()`:
```csharp
if (opts.h265 != null && opts.h265.resolutionsAvailable != null) {
    foreach (var res in opts.h265.resolutionsAvailable) {
        yield return Tuple.Create(VideoEncoding.h265, res);
    }
}
```
Also add H.265 color in `EncoderResolutionPair.Foreground` (after line 138):
```csharp
case VideoEncoding.h265:
    frgnd = new SolidColorBrush(Color.FromArgb(255, 80, 0, 80)); // purple
    break;
```
**Why:** Without this, H.265 resolutions won't appear in the encoder dropdown.
**Done when:** H.265 encoder options appear in the video settings dropdown with a distinct color.
**Type:** task

### Task 5.4 — VERIFY: Full solution builds and UI shows H.265
**What:** Full solution build (`odm.sln`). Manual verification: when connected to an H.265-capable camera (or mock ONVIF response), the video settings dropdown should list H.265 options.
**Done when:** Clean build. H.265 options visible in video settings UI when reported by camera.
**Type:** verify

---

## Phase 6 — Integration Testing

### Task 6.1 — Test H.265 stream playback end-to-end
**What:** Test with either:
- A real H.265 ONVIF camera
- An RTSP test server streaming H.265 (e.g., `ffmpeg -re -i input.mp4 -c:v libx265 -f rtsp rtsp://localhost:8554/test`)
- Verify: stream negotiation, SDP parsing, RTP depayloading, decoding, rendering all work
**Done when:** H.265 video plays in the ODM video player window.
**Type:** task

### Task 6.2 — Regression test H.264 and MJPEG playback
**What:** Verify existing H.264 and MJPEG streams still play correctly after all changes.
**Done when:** H.264 and MJPEG streams play without regression.
**Type:** task

### Task 6.3 — Run existing unit tests
**What:** Run all tests in `odm/odm.tests/` to ensure no regressions.
**Done when:** All existing tests pass.
**Type:** task

### Task 6.4 — VERIFY: Full integration verification
**What:** Final checkpoint — all codecs work, no regressions, solution builds clean.
**Done when:** H.265, H.264, and MJPEG all play. Tests pass. Clean build.
**Type:** verify

---

## Risks

1. **FFmpeg API churn** — The jump from avcodec-54 (2012) to modern FFmpeg (7.x, 2024) spans 12 years of API changes. The migration in Task 2.1 is substantial. If too many APIs changed, consider an intermediate version (FFmpeg 4.4 LTS) as a stepping stone.

2. **Live555 build complexity** — Live555 doesn't use CMake; it uses a custom makefile system. Building on Windows may require MSYS2 or manual vcxproj creation. The existing `libs/live555-2013.02.11/` may have a custom VS project already.

3. **Binary compatibility** — The FFmpeg DLLs are loaded at runtime. If the new DLL names differ (e.g., `avcodec-61.dll` vs `avcodec-54.dll`), all references in csproj, vcxproj, and any P/Invoke declarations must be updated.

4. **ONVIF schema version** — The ONVIF WSDL/XSD schemas in `onvif/onvif.services/schemas/` are from an older ONVIF spec version. H.265 types may not exist in those schemas. We're adding them manually to `onvif.types.cs` which is a hand-maintained file (not auto-generated from WSDL), so this is safe. However, if the WCF service proxies (`Reference.cs`, `Reference1.cs`) also need H.265 types, those may require re-generation from updated WSDL.

5. **VPS (Video Parameter Sets)** — H.265 introduces VPS in addition to SPS/PPS. The `parseSPropParameterSets()` function used in `VideoDecoder.hpp:52` may need adjustment for H.265's `sprop-vps`, `sprop-sps`, `sprop-pps` SDP parameters (RFC 7798 defines these separately, vs H.264's single `sprop-parameter-sets`).

6. **Hardware acceleration** — Modern FFmpeg supports hardware-accelerated HEVC decoding (DXVA2/D3D11VA on Windows). The current software-only decode path will work but may struggle with 4K HEVC streams. Hardware acceleration is out of scope for this sprint but should be considered for follow-up.
