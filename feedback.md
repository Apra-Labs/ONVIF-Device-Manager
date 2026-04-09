# ODM H.265 Sprint — Cumulative Review: Phases 1, 2, and 3

**Reviewer:** cicd-reviewer (automated, via orchestrator fallback)
**Date:** 2026-04-10
**Branch:** `feat/h265-support`
**HEAD SHA:** `5f7ee6f634bb531a4668986fccb14d6e6196edc6`
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

## Cumulative Verdict: APPROVED

All Phase 3 tasks verified against PLAN.md done criteria. No regressions against Phase 1 or Phase 2. H265VirtualSink mirrors H264VirtualSink exactly. Include chain complete. HEVC codec ID correctly used throughout. Ready to proceed to Phase 4 (F# / UI layer).
