# ODM H.265 Sprint — Cumulative Review: Phases 1–4

**Reviewer:** cicd-reviewer
**Date:** 2026-04-10
**Branch:** `feat/h265-support`
**HEAD SHA:** `86ebd5d1e525c5c0f7b8b92738cba6a0b9150f1a`
**Verdict:** APPROVED

---

## Phase 1 — ONVIF Schema & Type Bindings

Previously reviewed and approved (commit `e9ef914`). No regressions detected in current diff. Phase 1 changes are stable.

**Status: APPROVED (carried forward)**

---

## Phase 2 — Media Library Upgrades

Previously reviewed, initially flagged, fixed, and re-approved (commit `28c514e`). Confirmed fixes:
- FFmpeg API renamed: `CodecID` → `AVCodecID`, `PixelFormat` → `AVPixelFormat`, `CODEC_ID_*` → `AV_CODEC_ID_*`, `CODEC_FLAG2_CHUNKS` → `AV_CODEC_FLAG2_CHUNKS`
- Deprecated functions removed: `av_register_all`, `avcodec_register_all`, `avcodec_open`, `avcodec_alloc_frame`, `avcodec_alloc_context`, `avcodec_decode_video2`
- Replaced with: `avcodec_open2`, `av_frame_alloc`, `avcodec_alloc_context3`, `avcodec_send_packet`/`avcodec_receive_frame`
- `avcodec_close + av_free` → `avcodec_free_context`
- `av_free(avFrame)` → `av_frame_free(&avFrame)`

**Status: APPROVED (carried forward)**

---

## Phase 3 — C++ Player Pipeline

### Task 3.1 — H265 branch in InitSubsession()

**PASS.**

```cpp
}else if (_stricmp(codecName, "H265")==0){
    return InitVideoSubsession(AV_CODEC_ID_HEVC, sprops);
```

- H265 branch is present in `Live555.cpp`.
- Uses `_stricmp` — matches existing pattern for all other codec branches.
- Correctly positioned: after H264 branch, before MPEG4 branch.
- Calls `InitVideoSubsession(AV_CODEC_ID_HEVC, sprops)` — correct codec ID for HEVC.
- `InitVideoSubsession` signature updated to `AVCodecID` (in both `.cpp` and `Live555.hpp`) — consistent with Phase 2 API rename.

### Task 3.2 — H265VirtualSink.hpp

**PASS.**

File exists at: `odm/odm.player/odm.player.lib/include/odm.player.lib/H265VirtualSink.hpp`

Verified:
- `CreateNew()` static factory method present, matches H264VirtualSink signature exactly (same default `bufferSize = 4*1024*1024`).
- Constructor prepends 4-byte Annex-B start code `{0x00, 0x00, 0x00, 0x01}` — identical logic to H264VirtualSink.
- Destructor reverses `bufferPtr` adjustment — identical to H264VirtualSink.
- `AfterGettingFrame` includes start-code-already-present check:
  - Checks for 4-byte start code first (`startCode4`)
  - Falls through to check 3-byte start code (`startCode3`)
  - Only prepends if neither is present
- Logic is a byte-for-byte mirror of H264VirtualSink. No behavioral divergence.
- `#pragma once` guard present.
- Inherits from `VirtualSink` — correct base class.

### Task 3.3 — SetupSubsession routing

**PASS.**

```cpp
if(_stricmp(codecName, "H264")==0){
    sink = H264VirtualSink::CreateNew(*usageEnvironment);
}else if(_stricmp(codecName, "H265")==0){
    sink = H265VirtualSink::CreateNew(*usageEnvironment);
}else{
    sink = VirtualSink::CreateNew(*usageEnvironment);
}
```

- H265 case correctly routes to `H265VirtualSink::CreateNew()`.
- H264 path unchanged — no regression.
- Generic `VirtualSink` fallback retained for all other codecs.

### Task 3.4 — AV_CODEC_FLAG2_CHUNKS for HEVC

**PASS (pre-applied in Phase 2).**

```cpp
if (avCodecContext->codec_id == AV_CODEC_ID_H264 || avCodecContext->codec_id == AV_CODEC_ID_HEVC){
    avCodecContext->flags2 |= AV_CODEC_FLAG2_CHUNKS;
}
```

- `AV_CODEC_ID_HEVC` explicitly included in the CHUNKS flag block.
- Flag name also updated to `AV_CODEC_FLAG2_CHUNKS` (from deprecated `CODEC_FLAG2_CHUNKS`) — correct for FFmpeg 4.x+.

### Task 3.5 — Include wiring

**PASS.**

Include chain verified:
1. `Live555.cpp` line 1: `#include "odm.player.lib/all.h"`
2. `all.h` line 131: `#include "odm.player.lib/H265VirtualSink.hpp"` (inserted after H264VirtualSink, before VideoDecoder)
3. `H265VirtualSink.hpp` line 2: `#include "odm.player.lib/all.h"` (provides VirtualSink base, UsageEnvironment, etc.)

No missing header chain. H265VirtualSink is fully reachable from Live555.cpp.

### Regression Check

**PASS.**

- `H264VirtualSink` path in SetupSubsession: unchanged.
- `VirtualSink` fallback path: unchanged.
- MJPEG path (`AV_CODEC_ID_MJPEG`) in InitSubsession: unchanged (Phase 2 rename intact).
- MPEG4, MP4V-ES, MPV paths: unchanged.
- Phase 1 changes (XSD/C# types): no modifications in Phase 3 diff.
- Phase 2 changes (FFmpeg API, VideoDecoder, VideoRenderer, VideoRecorder, core.h): no reversions or modifications in Phase 3 diff.

---

## Commit History (development..feat/h265-support)

All 19 commits account for Phases 1, 2, and 3 in full:

| SHA | Description |
|-----|-------------|
| `5f7ee6f` | chore: progress — Task 3.5 complete, VERIFY 3.6 complete |
| `22e240f` | feat(h265): Task 3.5 — Include H265VirtualSink.hpp |
| `71406b8` | chore: progress — Tasks 3.1-3.4 completed |
| `38fee67` | feat(h265): Task 3.3 — Route H265 to H265VirtualSink |
| `2f76607` | feat(h265): Task 3.2 — H265VirtualSink class |
| `edf2f94` | feat(h265): Task 3.1 — H265 branch in InitSubsession |
| `28c514e` | review: Phase 2 re-review — APPROVED |
| `05d6d21` | chore: annotate Phase 2 review findings as fixed |
| `319c424` | fix(h265): Phase 2 review — FFmpeg API renames |
| `0382aba` | review: Phase 2 media library upgrades |
| ... | (Phase 2 and Phase 1 tasks) |

---

---

## Phase 4 — F# / UI Layer (VideoSettingsActivity)

### Task 4.1 — H265 match in isVecConfigured

**PASS.**

```fsharp
|VideoEncoding.h265 -> validateConfig(options.h265)
|_ -> raise (new ArgumentException(LocalVideoSettings.instance.errorEncoder))
```

- `|VideoEncoding.h265 -> validateConfig(options.h265)` is present at line 279.
- Indentation matches surrounding match arms exactly (leading `|` aligned with h264/jpeg/mpeg4 arms).
- `|_` fallback still present immediately after the h265 case (line 280).
- `VideoEncoding.h265` enum member confirmed in `onvif.types.cs:3647` with correct `XmlEnumAttribute(Name = "H265")`.
- `options.h265` resolves to `VideoEncoderConfigurationOptions.h265` property (type `H265Options`, `onvif.types.cs:6409`). Confirmed property has `resolutionsAvailable`, `frameRateRange`, `encodingIntervalRange`, `govLengthRange` — all members required by the `validateConfig` inline function's structural type constraint.

### Task 4.2 — 6 H265 branches in VideoSettingsActivity

**PASS — all 6 branches verified.**

**1. frameRateRange (line 100-101):**
```fsharp
if options.h265 |> NotNull then
    yield options.h265.frameRateRange
```
Mirrors h264/jpeg/mpeg4 pattern exactly. `H265Options.frameRateRange` confirmed (`onvif.types.cs:6706`, type `IntRange`).

**2. encodingIntervalRange (line 111-112):**
```fsharp
if options.h265 |> NotNull then
    yield options.h265.encodingIntervalRange
```
Mirrors existing pattern. `H265Options.encodingIntervalRange` confirmed (`onvif.types.cs:6718`, type `IntRange`).

**3. govLengthRange (line 120-121):**
```fsharp
if options.h265 |> NotNull then
    yield options.h265.govLengthRange
```
Mirrors h264/mpeg4 pattern. `H265Options.govLengthRange` confirmed (`onvif.types.cs:6694`, type `IntRange`).

**4. govLength read (line 128-129):**
```fsharp
elif vec.encoding = VideoEncoding.h265 && NotNull(vec.h265) then
    vec.h265.govLength
```
Correctly positioned after the mpeg4 branch, before the `else -1` fallback. Null guard on `vec.h265` present. `VideoEncoderConfiguration.h265` confirmed (`onvif.types.cs:3583`, type `H265Configuration`). `H265Configuration.govLength` confirmed (`onvif.types.cs:3803`, type `int`).

**5. H265Options2 bitrateRange (line 143-144):**
```fsharp
elif x.Name = @"H265" then
    yield x.Deserialize<H265Options2>().bitrateRange
```
Mirrors H264Options2 pattern exactly. `H265Options2` class confirmed (`onvif.types.cs:7180`). `H265Options2.bitrateRange` confirmed (`onvif.types.cs:7284`, type `IntRange`). Element name `"H265"` matches the XSD element name in `VideoEncoderOptionsExtension`.

**6. H265Configuration govLength write (line 264-266):**
```fsharp
elif model.encoder = VideoEncoding.h265 then
    if vec.h265 |> IsNull then vec.h265 <- new H265Configuration()
    vec.h265.govLength <- model.govLength |> CoerceGovLength(options.h265)
```
Mirrors h264/mpeg4 pattern exactly. Creates `H265Configuration` if null (defensive), sets govLength with coercion. `CoerceGovLength` uses structural typing (`member govLengthRange:IntRange`) — `H265Options.govLengthRange` satisfies this constraint.

### Task 4.3 — Manual review passed

**PASS.** msbuild unavailable; manual inspection confirmed no F# syntax issues or type mismatches.

### Type Consistency (Cross-Phase)

**PASS.**

All H265 types referenced in Phase 4 F# code match exactly what was added in Phase 1:

| F# Reference | C# Type | Location | Status |
|---|---|---|---|
| `VideoEncoding.h265` | `VideoEncoding.h265` | `onvif.types.cs:3647` | Correct |
| `options.h265` | `VideoEncoderConfigurationOptions.h265` (`H265Options`) | `onvif.types.cs:6409` | Correct |
| `vec.h265` | `VideoEncoderConfiguration.h265` (`H265Configuration`) | `onvif.types.cs:3583` | Correct |
| `H265Options2` | `H265Options2` class | `onvif.types.cs:7180` | Correct |
| `H265Configuration()` | `H265Configuration` class | `onvif.types.cs:3788` | Correct |
| `.frameRateRange` | `H265Options.frameRateRange` (`IntRange`) | `onvif.types.cs:6706` | Correct |
| `.encodingIntervalRange` | `H265Options.encodingIntervalRange` (`IntRange`) | `onvif.types.cs:6718` | Correct |
| `.govLengthRange` | `H265Options.govLengthRange` (`IntRange`) | `onvif.types.cs:6694` | Correct |
| `.govLength` | `H265Configuration.govLength` (`int`) | `onvif.types.cs:3803` | Correct |
| `.bitrateRange` | `H265Options2.bitrateRange` (`IntRange`) | `onvif.types.cs:7284` | Correct |
| `.resolutionsAvailable` | `H265Options.resolutionsAvailable` (`VideoResolution[]`) | `onvif.types.cs:6674` | Correct |

Casing and property names match exactly between F# usage and C# definitions. No spelling discrepancies.

### Regression Check

**PASS.**

- Only one line removed in the diff: trailing whitespace on `let govLength =` — cosmetic only.
- All h264, jpeg, mpeg4 branches in every match expression remain intact and unmodified.
- Phase 1 types: no modifications in Phase 4 diff.
- Phase 2 FFmpeg/live555 files: no modifications in Phase 4 diff.
- Phase 3 C++ pipeline files: no modifications in Phase 4 diff.

---

## Commit History (development..feat/h265-support)

22 commits total, covering Phases 1–4 plus reviews:

| SHA | Description |
|-----|-------------|
| `86ebd5d` | chore: progress — VERIFY 4.3 complete (manual review) |
| `a5a2b02` | feat(h265): Task 4.2 — H265 govLength and configuration in VideoSettingsActivity |
| `04b30d2` | feat(h265): Task 4.1 — Handle h265 in VideoSettingsActivity encoder match |
| `8dbe95c` | review: Phase 3 cumulative review — APPROVED |
| `5f7ee6f` | chore: progress — Task 3.5 complete, VERIFY 3.6 complete |
| `22e240f` | feat(h265): Task 3.5 — Include H265VirtualSink.hpp |
| `71406b8` | chore: progress — Tasks 3.1-3.4 completed |
| `38fee67` | feat(h265): Task 3.3 — Route H265 to H265VirtualSink |
| `2f76607` | feat(h265): Task 3.2 — H265VirtualSink class |
| `edf2f94` | feat(h265): Task 3.1 — H265 branch in InitSubsession |
| `28c514e` | review: Phase 2 re-review — APPROVED |
| `319c424` | fix(h265): Phase 2 review — FFmpeg API renames |
| `...` | (Phase 2 and Phase 1 tasks) |

---

## Cumulative Verdict: APPROVED

**Phases 1–3:** Carried forward, all previously approved. No regressions detected.

**Phase 4:** All three tasks verified.
- **Task 4.1:** H265 match arm added correctly in `isVecConfigured`, with proper indentation and `|_` fallback preserved.
- **Task 4.2:** All 6 H265 branches added in VideoSettingsActivity.fs — frameRateRange, encodingIntervalRange, govLengthRange, govLength read, H265Options2 bitrateRange, and H265Configuration govLength write. Each mirrors the existing h264/mpeg4 pattern exactly.
- **Task 4.3:** Manual review passed (msbuild unavailable). No F# syntax issues or type mismatches identified.

All H265 type references cross-checked against Phase 1 C# definitions — casing, property names, and types match exactly. No regressions in existing codec paths.

Ready to proceed to Phase 5 (Integration Testing).
