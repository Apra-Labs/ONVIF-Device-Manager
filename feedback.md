# H265 ODM Support - Code Review

**Reviewer:** cicd-reviewer
**Date:** 2026-04-10
**Branch:** feat/h265-support
**Reviewed SHA:** 53097e271f5e58b3d566adaad9e7eec8536096b8

---

## Phase 1 Review: ONVIF Schema and Type Bindings (Tasks 1.1–1.8)

### Task 1.1 - VideoEncoding enum in XSD
PASS. H265 enumeration added after H264 in VideoEncoding simpleType. Correct placement and indentation.

### Task 1.2 - VideoEncoding enum in C# types
PASS. h265 member with XmlEnumAttribute(Name="H265"). Follows h264/mpeg4/jpeg pattern exactly.

### Task 1.3 - H265Profile enum (XSD + C#)
PASS. Main/Main10/MainStillPicture in both XSD and C#. Correct XmlTypeAttribute and XmlEnumAttribute decorators. Aligns with ONVIF 17.06+.

### Task 1.4 - H265Configuration class (XSD + C#)
PASS. GovLength and H265Profile properties. Mirrors H264Configuration structure. Correct namespace and attribute names.

### Task 1.5 - H265 field in VideoEncoderConfiguration
PASS. Optional element (minOccurs=0) placed after H264, before Multicast. C# property with correct XmlElementAttribute.

### Task 1.6 - H265Options and H265Options2 classes
PASS. H265Options: 5 elements matching H264Options. H265Options2: flattened class pattern consistent with H264Options2 implementation.

### Task 1.7 - H265 field in VideoEncoderConfigurationOptions
PASS. Added to both VideoEncoderConfigurationOptions and VideoEncoderOptionsExtension. Order comments updated correctly.

### Task 1.8 - Mirror XSD changes to Service References copy
PASS. Both XSD copies identical in semantic content.

### Task 1.9 - Build verification
NOTE. Build blocked by missing .NET 4.5 targeting pack (environment issue). Manual review thorough. Deferred to Phase 4.

---

## Cross-cutting checks (Phase 1)

- **Additive-only**: PASS. No removals or renames.
- **Namespace consistency**: PASS. All use http://www.onvif.org/ver10/schema.
- **XML attribute correctness**: PASS. All patterns match H264 equivalents.
- **XSD sync**: PASS. Both copies identical.

### Phase 1 Verdict: APPROVED — proceed to Phase 2.

---

## Phase 2 Review: Media Library Upgrades (Tasks 2.1–2.6)

**Verdict: CHANGES NEEDED**

### What passed

**Task 2.1 — FFmpeg n7.1 (libavcodec 61):** PASS. All 4 vcxproj files reference `libs/ffmpeg-n7.1-lgpl-shared/`. `AV_CODEC_ID_HEVC` confirmed in headers.

**Task 2.3 — CODEC_ID_* constants:** PASS. No remaining `CODEC_ID_*` references. All replaced with `AV_CODEC_ID_*`.

**Task 2.4 — live555 upgrade:** PASS. `H265VideoRTPSource.cpp` and `H265VideoRTPSource.hh` exist in `liveMedia/`. Both vcxproj files include them. `liveMedia.hh` includes the header. `MediaSession.cpp` has H265 branch calling `H265VideoRTPSource::createNew()`.

**Task 2.5 — core.h include:** PASS. `#include "H265VideoRTPSource.hh"` at line 18, correctly placed after H264VideoRTPSource.hh.

**General — existing codecs:** PASS. H264/MJPEG/MPEG4 codec paths preserved. vcxproj structure valid.

### What failed — Required fixes before re-verify

| # | Severity | File | Issue | Required Fix |
|---|----------|------|-------|--------------|
| **1** | Compile error | `VideoDecoder.hpp:79` | `CODEC_FLAG2_CHUNKS` removed in FFmpeg 4.0+; only `AV_CODEC_FLAG2_CHUNKS` exists in 7.1 | Rename to `AV_CODEC_FLAG2_CHUNKS` |
| **2** | Compile error | `VideoDecoder.hpp:38,120` | `AVCodec*` must be `const AVCodec*` — `avcodec_find_decoder()` returns `const AVCodec*` in FFmpeg 7.1 | Add `const` qualifier |
| **3** | Compile error | `core.h:53`, `VideoRenderer.hpp:27` | `PixelFormat` type removed in FFmpeg 4.0+ | Change to `AVPixelFormat` |
| **4** | Compile error | `libapi.cpp:127–133` | `PIX_FMT_RGB24` etc. removed in FFmpeg 4.0+ | Change to `AV_PIX_FMT_*` equivalents |

Issues 1–2 are direct Task 2.2 regressions (FFmpeg API update incomplete). Issues 3–4 are pre-existing code broken by the FFmpeg version jump — must be fixed as part of Task 2.2 before Task 2.6 verification is valid.

### Next steps for doer

1. Fix all 4 issues above in a single commit (extend Task 2.2 scope)
2. Re-run Task 2.6 verification
3. Request Phase 2 re-review

### Phase 2 Verdict: CHANGES NEEDED
