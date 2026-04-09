# H.265 ODM Support — Implementation Plan

## Overview

ONVIF Device Manager cannot play H.265/HEVC streams because **all five layers** of its video pipeline predate H.265 support: the ONVIF XSD schema (no `H265` enum), the generated C# type bindings, the bundled FFmpeg (libavcodec 54, ~2012), the bundled live555 (2013.02.11), and the C++ player codec dispatch. Each layer must be updated: schema/types to negotiate H.265, libraries to decode it, and the C++/F# code to route it through the pipeline.

## Dependencies

| Component | Current Version | Required For H.265 | Notes |
|-----------|----------------|--------------------|-|
| FFmpeg libavcodec | 54.25.100 (~2012) | 56+ (FFmpeg 2.4+), ideally 4.x+ | HEVC decoder added in libavcodec 55; stable from 56+ |
| live555 | 2013.02.11 | 2014.07.04+ | `H265VideoRTPSource` added mid-2014 |
| ONVIF XSD | Pre-17.06 (no H265) | 17.06+ spec | `VideoEncoding.H265` introduced in ONVIF Profile S 2.x |

## Phase 1 — ONVIF Schema & Type Bindings

### Task 1.1 — Add H265 to VideoEncoding enum in XSD
**File:** `onvif/onvif.services/schemas/onvif.xsd:307-313`
**What:** Add `<xs:enumeration value="H265"/>` to the `VideoEncoding` simpleType restriction, after the `H264` entry.
**Why:** The XSD defines the canonical list of video encodings. Without H265 here, the generated C# enum cannot represent it, and ONVIF SOAP responses containing `H265` will fail deserialization.
**Done when:** The `VideoEncoding` simpleType in `onvif.xsd` includes JPEG, MPEG4, H264, and H265.
**Type:** task

### Task 1.2 — Add H265 to VideoEncoding enum in generated C# types
**File:** `onvif/onvif.services/onvif.types.cs:3616-3628`
**What:** Add a new enum member to `VideoEncoding`:
```csharp
[System.Xml.Serialization.XmlEnumAttribute(Name = "H265")]
h265,
```
**Why:** The C# enum is the runtime representation of the XSD type. Without `h265`, any ONVIF profile returning `VideoEncoding=H265` will throw a deserialization exception.
**Done when:** `VideoEncoding` enum has four members: `jpeg`, `mpeg4`, `h264`, `h265`.
**Type:** task

### Task 1.3 — Add H265Profile enum
**File:** `onvif/onvif.services/onvif.types.cs` (insert after `H264Profile` at line ~3783)
**What:** Add a new enum:
```csharp
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
Also add the corresponding `xs:simpleType name="H265Profile"` in `onvif.xsd` after the `H264Profile` type (after line 329).
**Why:** ONVIF 17.06+ defines H265Profile with Main, Main10, MainStillPicture. Needed for H265Configuration.
**Done when:** `H265Profile` enum exists in both XSD and C# types.
**Type:** task

### Task 1.4 — Add H265Configuration class
**File:** `onvif/onvif.services/onvif.types.cs` (insert after `H264Configuration` at line ~3766)
**What:** Add a new class mirroring `H264Configuration` (lines 3732-3766) but for H265:
```csharp
[System.SerializableAttribute()]
[System.Xml.Serialization.XmlTypeAttribute(TypeName = "H265Configuration", Namespace = "http://www.onvif.org/ver10/schema")]
public partial class H265Configuration {
    private int _govLength;
    private H265Profile _h265Profile;
    // govLength property (same pattern as H264Configuration.govLength)
    // h265Profile property
}
```
Also add the corresponding `xs:complexType name="H265Configuration"` in `onvif.xsd`.
**Why:** When a camera reports an H.265 encoder configuration, the SOAP response includes an `H265` element inside `VideoEncoderConfiguration`. This class deserializes it.
**Done when:** `H265Configuration` class compiles and has `govLength` and `h265Profile` properties.
**Type:** task

### Task 1.5 — Add H265 field to VideoEncoderConfiguration
**File:** `onvif/onvif.services/onvif.types.cs:3411` (class `VideoEncoderConfiguration`)
**What:** Add a private `H265Configuration _h265` field and public property, mirroring the existing `_h264`/`h264` pattern (lines 3431, 3567). Also add `H265Configuration` element to the `VideoEncoderConfiguration` complexType in `onvif.xsd`.
**Why:** When a camera's profile uses H.265 encoding, the VideoEncoderConfiguration SOAP element contains an `H265` child element.
**Done when:** `VideoEncoderConfiguration` has an `h265` property of type `H265Configuration`.
**Type:** task

### Task 1.6 — Add H265Options and H265Options2 classes
**File:** `onvif/onvif.services/onvif.types.cs` (after `H264Options` at ~line 6484 and `H264Options2` at ~line 6867)
**What:** Add `H265Options` class (mirroring `H264Options` structure: resolutions, govLengthRange, frameRateRange, encodingIntervalRange, h265ProfilesSupported) and `H265Options2` (mirroring `H264Options2`). Also add corresponding XSD types.
**Why:** `VideoEncoderConfigurationOptions` needs to report what H.265 settings the camera supports.
**Done when:** `H265Options` and `H265Options2` classes exist with resolution, rate, and profile fields.
**Type:** task

### Task 1.7 — Add H265 field to VideoEncoderConfigurationOptions
**File:** `onvif/onvif.services/onvif.types.cs:6252` (class `VideoEncoderConfigurationOptions`)
**What:** Add `H265Options _h265` field and public property (same pattern as `_h264`/`h264` at lines 6262, 6322-6330). Also add to `VideoEncoderOptionsExtension` (line 6569) an `H265Options2 _h265` field. Update the XSD complexTypes accordingly.
**Why:** When camera capabilities are queried, H.265 options must be deserializable.
**Done when:** `VideoEncoderConfigurationOptions.h265` property exists.
**Type:** task

### Task 1.8 — Mirror XSD changes to Service References copy
**File:** `onvif/onvif.services/Service References/services/onvif.xsd`
**What:** Copy all XSD changes from Task 1.1, 1.3, 1.4, 1.5, 1.6, 1.7 to the mirror XSD file in the Service References directory.
**Why:** Both XSD copies must stay in sync. The Service References copy is used by the WCF/svcutil codegen pipeline.
**Done when:** Both `schemas/onvif.xsd` and `Service References/services/onvif.xsd` are identical in H.265-related additions.
**Type:** task

### Task 1.9 — Verify: ONVIF schema and types compile
**What:** Build the `onvif.services` project to confirm all new types compile and XML serialization attributes are correct.
**Done when:** `onvif.services.csproj` builds without errors.
**Type:** verify

## Phase 2 — Upgrade Media Libraries

### Task 2.1 — Upgrade FFmpeg to a version with HEVC decoder
**File:** `libs/ffmpeg-git-a5c1a0c/` (entire directory)
**What:** Replace the bundled FFmpeg (libavcodec 54.25) with a version that includes the HEVC decoder. Minimum: FFmpeg 2.4 / libavcodec 56. Recommended: FFmpeg 4.4+ / libavcodec 58+ for stability and performance. Must provide:
- Windows x64 and win32 builds (matching current `win32/` and `x64/` layout)
- Static libraries: `avcodec.lib`, `avutil.lib`, `swscale.lib` (at minimum)
- Headers in `include/`
- DLLs for runtime: `avcodec-*.dll`, `avutil-*.dll`, `swscale-*.dll`

**Why:** libavcodec 54 has no `CODEC_ID_HEVC` / `AV_CODEC_ID_HEVC`. The HEVC decoder (`libde265` or built-in) was introduced in libavcodec 55 and stabilized in 56+.
**Done when:** `include/libavcodec/avcodec.h` contains `AV_CODEC_ID_HEVC` (or `CODEC_ID_HEVC`). Libraries link successfully.
**Type:** task

### Task 2.2 — Update FFmpeg API calls for new version
**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/VideoDecoder.hpp`
**What:** The current code uses deprecated FFmpeg APIs:
- Line 12: `av_register_all()` — removed in FFmpeg 4.0+
- Line 13: `avcodec_register_all()` — removed in FFmpeg 4.0+
- Line 43: `avcodec_alloc_context()` — deprecated, use `avcodec_alloc_context3()`
- Line 72: `avcodec_open()` — deprecated, use `avcodec_open2()`
- Line 81: `avcodec_alloc_frame()` — deprecated, use `av_frame_alloc()`

Wrap old API calls in `#if LIBAVCODEC_VERSION_MAJOR < 55` guards or update to new APIs.
**Why:** FFmpeg 4.x removed these deprecated functions entirely. Must use new API or the code won't compile.
**Done when:** VideoDecoder.hpp compiles against the new FFmpeg version.
**Type:** task

### Task 2.3 — Update CODEC_ID constants to AV_CODEC_ID
**File:** `odm/odm.player/odm.player.lib/Live555.cpp:142-151`
**What:** FFmpeg 55+ renamed `CODEC_ID_*` to `AV_CODEC_ID_*`. Update:
- `CODEC_ID_MJPEG` → `AV_CODEC_ID_MJPEG`
- `CODEC_ID_H264` → `AV_CODEC_ID_H264`
- `CODEC_ID_MPEG4` → `AV_CODEC_ID_MPEG4`
- `CODEC_ID_MPEG2VIDEO` → `AV_CODEC_ID_MPEG2VIDEO`

Also update `VideoDecoder.hpp:77` (`CODEC_ID_H264` → `AV_CODEC_ID_H264`).
**Why:** The old `CODEC_ID_*` macros are removed in newer FFmpeg. Code won't compile without this.
**Done when:** No `CODEC_ID_` references remain; all use `AV_CODEC_ID_` prefix.
**Type:** task

### Task 2.4 — Upgrade live555 to a version with H265VideoRTPSource
**File:** `libs/live555-2013.02.11/` (entire directory)
**What:** Replace bundled live555 (2013.02.11) with version 2014.07.04 or later (ideally latest stable). Must include:
- `H265VideoRTPSource.hh` / `H265VideoRTPSource.cpp` in `liveMedia/`
- Updated `MediaSession.cpp` that creates `H265VideoRTPSource` for RTP payload type 96 with H265 encoding
- All existing functionality (H264, JPEG, MPEG4 RTP sources) preserved

**Why:** live555 2013 has no H265 RTP depacketizer. The library automatically creates the correct RTPSource subclass based on SDP codec name; it needs `H265VideoRTPSource` to handle `H265` in SDP.
**Done when:** `liveMedia/include/H265VideoRTPSource.hh` exists. live555 library compiles.
**Type:** task

### Task 2.5 — Update core.h includes for new live555
**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/core.h:17`
**What:** Add `#include "H265VideoRTPSource.hh"` after the existing `#include "H264VideoRTPSource.hh"` (line 17).
**Why:** Needed for the H265VirtualSink (Task 3.2) to reference H265 RTP types.
**Done when:** `core.h` includes both H264 and H265 RTP source headers.
**Type:** task

### Task 2.6 — Verify: media libraries build
**What:** Build `odm.player.lib.vcxproj` and `live555.vcxproj` to confirm the upgraded libraries compile and link.
**Done when:** Both native projects build successfully on x64 and win32.
**Type:** verify

## Phase 3 — C++ Player Pipeline

### Task 3.1 — Add H265 codec branch in Live555::InitSubsession
**File:** `odm/odm.player/odm.player.lib/Live555.cpp:137-154`
**What:** Add an `else if` branch for H265 in `InitSubsession()`:
```cpp
}else if (_stricmp(codecName, "H265")==0){
    return InitVideoSubsession(AV_CODEC_ID_HEVC, sprops);
```
Insert after the H264 branch (line 145) and before the MPEG4 branch (line 146).
**Why:** When live555 parses the SDP and finds an H265 codec, `SetupSubsession` calls `InitSubsession` with `codecName="H265"`. Without this branch, `InitSubsession` returns `nullptr` and the stream is silently dropped.
**Done when:** `InitSubsession("H265", ...)` returns a valid `IFrameProcessorFactory`.
**Type:** task

### Task 3.2 — Create H265VirtualSink class
**File:** New file: `odm/odm.player/odm.player.lib/include/odm.player.lib/H265VirtualSink.hpp`
**What:** Create an H265VirtualSink class modeled on `H264VirtualSink.hpp`. H.265 NAL units also use Annex-B start codes (`0x00 0x00 0x01` or `0x00 0x00 0x00 0x01`), so the logic is nearly identical to `H264VirtualSink`:
- Prepend 4-byte start code `{0x00, 0x00, 0x00, 0x01}` before each NAL unit
- Check if the payload already contains start codes (same logic as H264VirtualSink lines 55-60)

The class structure mirrors `H264VirtualSink` exactly — only the class name changes.
**Why:** H.265 RTP payloads arrive as raw NAL units without Annex-B start codes. The FFmpeg HEVC decoder expects Annex-B format.
**Done when:** `H265VirtualSink.hpp` exists with `CreateNew()` factory method and start-code prepending logic.
**Type:** task

### Task 3.3 — Route H265 codec to H265VirtualSink in SetupSubsession
**File:** `odm/odm.player/odm.player.lib/Live555.cpp:176-182`
**What:** Update the sink selection in `SetupSubsession` to use `H265VirtualSink` for H265:
```cpp
if(_stricmp(codecName, "H264")==0){
    sink = H264VirtualSink::CreateNew(*usageEnvironment);
}else if(_stricmp(codecName, "H265")==0){
    sink = H265VirtualSink::CreateNew(*usageEnvironment);
}else{
    sink = VirtualSink::CreateNew(*usageEnvironment);
}
```
**Why:** H.265 NAL units need start-code prepending, same as H.264.
**Done when:** H265 streams use `H265VirtualSink` for frame processing.
**Type:** task

### Task 3.4 — Add H265 CODEC_FLAG2_CHUNKS handling in VideoDecoder
**File:** `odm/odm.player/odm.player.lib/include/odm.player.lib/VideoDecoder.hpp:77-80`
**What:** Extend the CODEC_FLAG2_CHUNKS check to also apply to HEVC:
```cpp
if (avCodecContext->codec_id == AV_CODEC_ID_H264 || avCodecContext->codec_id == AV_CODEC_ID_HEVC){
    avCodecContext->flags2 |= CODEC_FLAG2_CHUNKS;
}
```
**Why:** Like H.264, H.265 streams over RTP may deliver partial NAL units. `CODEC_FLAG2_CHUNKS` tells FFmpeg to handle incomplete frames gracefully.
**Done when:** HEVC decoder is initialized with CHUNKS flag.
**Type:** task

### Task 3.5 — Include H265VirtualSink.hpp in build
**File:** `odm/odm.player/odm.player.lib/Live555.cpp` (top of file, near includes)
**What:** Add `#include "odm.player.lib/H265VirtualSink.hpp"` to the includes.
**Why:** `Live555.cpp` references `H265VirtualSink::CreateNew` (from Task 3.3).
**Done when:** Live555.cpp compiles with H265VirtualSink reference.
**Type:** task

### Task 3.6 — Verify: C++ player builds and links
**What:** Build `odm.player.lib.vcxproj` to confirm H265 pipeline compiles. Verify no linker errors from FFmpeg or live555 symbol changes.
**Done when:** `odm.player.lib` builds cleanly on x64.
**Type:** verify

## Phase 4 — F# / UI Layer

### Task 4.1 — Handle H265 in VideoSettingsActivity encoder match
**File:** `odm/odm.ui.activities/VideoSettingsActivity.fs:261-266`
**What:** Add H265 case to the `isVecConfigured` match expression:
```fsharp
match model.encoder with
|VideoEncoding.h264 -> validateConfig(options.h264)
|VideoEncoding.jpeg -> validateConfig(options.jpeg)
|VideoEncoding.mpeg4 -> validateConfig(options.mpeg4)
|VideoEncoding.h265 -> validateConfig(options.h265)
|_ -> raise (new ArgumentException(...))
```
**Why:** Currently the `|_` default case throws an `ArgumentException` for any unknown encoding, which would crash the UI when an H.265 profile is selected.
**Done when:** Selecting an H.265 profile in the video settings UI does not throw.
**Type:** task

### Task 4.2 — Handle H265 govLength and configuration in VideoSettingsActivity
**File:** `odm/odm.ui.activities/VideoSettingsActivity.fs:93-134`
**What:** Add H265 handling wherever H264 is handled:
- Lines 94-95: Add `if options.h265 |> NotNull then yield options.h265.frameRateRange` (mirroring h264 pattern)
- Lines 103-104: Add H265 `encodingIntervalRange`
- Lines 112-113: Add H265 `govLengthRange`
- Lines 118-123: Add `elif vec.encoding = VideoEncoding.h265 && NotNull(vec.h265) then vec.h265.govLength`
- Lines 133-134: Add `elif x.Name = @"H265" then yield x.Deserialize<H265Options2>().bitrateRange`
- Lines 248-253: Add `elif model.encoder = VideoEncoding.h265 then` branch to create H265Configuration and set govLength
**Why:** The video settings activity needs to display and apply H.265-specific encoder parameters.
**Done when:** H.265 encoder settings can be viewed and modified in the UI.
**Type:** task

### Task 4.3 — Verify: full solution builds
**What:** Build the entire `odm.sln` solution. All projects must compile.
**Done when:** `odm.sln` builds with 0 errors on x64/Release configuration.
**Type:** verify

## Phase 5 — Integration Testing

### Task 5.1 — Test H265 ONVIF profile negotiation
**What:** Connect to a camera (or ONVIF simulator) that advertises H.265 profiles. Verify:
- `GetProfiles()` returns profiles with `VideoEncoding.h265` without deserialization errors
- `GetVideoEncoderConfigurationOptions()` returns H265 options
- `GetStreamUri()` returns a valid RTSP URI for the H.265 profile
**Done when:** H.265 profiles appear in the ODM profile list without errors.
**Type:** verify

### Task 5.2 — Test H265 stream playback end-to-end
**What:** Play an H.265 RTSP stream from a real camera or test source. Verify:
- RTSP DESCRIBE returns SDP with H265 codec
- live555 creates H265VideoRTPSource
- Frames are decoded by FFmpeg HEVC decoder
- Video renders in the WPF VideoPlayer control
**Done when:** H.265 video plays in ODM with no visual artifacts or crashes.
**Type:** verify

### Task 5.3 — Regression test H264 and MJPEG playback
**What:** Verify that existing H.264 and MJPEG streams still play correctly after all changes.
**Done when:** H.264 and MJPEG streams play without regressions.
**Type:** verify

## Risks

1. **FFmpeg API breakage**: Upgrading from libavcodec 54 to 56+ involves significant API changes (deprecated function removal, renamed constants). Task 2.2 and 2.3 mitigate this, but there may be additional API changes in areas not yet identified.

2. **live555 build complexity**: live555 does not use CMake/MSBuild natively. The current `live555.vcxproj` is hand-crafted. Upgrading live555 will require updating this project file to include new source files (`H265VideoRTPSource.cpp`, etc.) and remove any deleted files.

3. **FFmpeg binary availability**: Pre-built FFmpeg Windows static libraries with HEVC enabled must be sourced or compiled. The HEVC decoder may require `libde265` or be built-in depending on the FFmpeg build configuration.

4. **CODEC_FLAG2_CHUNKS deprecation**: Newer FFmpeg versions may have renamed or changed this flag. Need to verify the equivalent flag name in the target FFmpeg version.

5. **ONVIF spec fidelity**: The H265-related XSD types added manually must match the official ONVIF 17.06+ schema exactly. Mismatch will cause deserialization failures with real cameras.

6. **H.265 patent licensing**: H.265/HEVC has complex patent licensing. Deployment on end-user machines requires awareness of MPEG-LA and HEVC Advance patent pools. This is a distribution concern, not a code concern.
