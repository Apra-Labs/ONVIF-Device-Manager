# ODM Sprint 8 — Full Branch Review

**Reviewer:** odm-rev
**Date:** 2026-04-18 23:55:00+05:30
**Verdict:** CHANGES NEEDED

> See git history of this file for prior phase verdicts (V1, V2, V3 — all APPROVED; crash fix addendum — APPROVED; WSDL consolidation — APPROVED with fix).

---

## Issue #21 Coverage

**Issue:** "VideoSettings: H265 shows only one resolution option; disappears from encoder list after switching to H264"

**Does the branch solve it? NO — partially addressed, critical gap remains.**

What IS done:
- `GetVideoEncoderConfigurationsMedia2()` (NvtSession.fs:1278–1302) now uses `routeMedia` with typed Media2 proxy to detect H265 encoder configurations. This correctly identifies that a camera has H265 encoding.
- `VideoSettingsActivity.fs` lines 56–79 use the Media2 configurations to override the effective encoding from `h264` to `h265` when appropriate.
- The `isMedia2OnlyH265` guard (VideoSettingsActivity.fs:254–261) prevents sending H265 encoding to Media1 (which would fail).

What is MISSING:
- **`GetVideoEncoderConfigurationOptionsMedia2` does not exist.** The Media2 service defines `GetVideoEncoderConfigurationOptionsAsync` (OnvifMedia2Gen.cs:180), but no wrapper method exists in NvtSession that uses `routeMedia` to call it.
- `GetVideoEncoderConfigurationOptions` (NvtSession.fs:1927–1930) calls **Media1 only** — it goes directly to `GetMediaClient()` without any Media2 routing.
- `VideoSettingsActivity.fs` line 54 calls `session.GetVideoEncoderConfigurationOptions(vec.token, profile.token)` — this returns Media1-only options.
- The UI (VideoSettingsView.xaml.cs:162–197) iterates `opts.h265.resolutionsAvailable` to populate the H265 resolution dropdown. When options come from Media1 only, `opts.h265` is NULL.
- **Result:** H265 is detected as the active encoding, but no H265 resolutions are available to display. The encoder appears with zero or one resolution option, and may disappear when switching to H264 and back.

**To fix issue #21 end-to-end, the branch needs:**
1. A `GetVideoEncoderConfigurationOptionsMedia2` method in NvtSession.fs that uses `routeMedia` to try Media2 `GetVideoEncoderConfigurationOptionsAsync` first, then fall back to Media1's `GetVideoEncoderConfigurationOptions`.
2. `VideoSettingsActivity.fs` must call the Media2-aware options method so that `opts.h265.resolutionsAvailable` is populated from Media2 when available.

---

## Phase 1 — onvif.gen proxy

PASS — Previously APPROVED (V1). `OnvifMedia2Gen.cs` (17,814 lines) generated via `dotnet-svcutil` from ONVIF Media2 WSDL. Contains typed `Media2` interface, `Media2Client`, `MediaProfile`, `ConfigurationSet`, `VideoEncoder2Configuration` with `string Encoding`. The generated code includes `GetVideoEncoderConfigurationOptionsAsync` — the method needed to complete issue #21.

WSDL files consolidated to `onvif/wsdl/` (APPROVED with fix — net45→net48 corrected in f7f1ec5). `odm.onvif.gen.csproj` is SDK-style targeting `net48`.

---

## Phase 2 — NvtSession integration

PARTIAL — The `routeMedia` helper (NvtSession.fs:1128–1142) is well-designed: tries typed Media2 first, catches exceptions and falls back to Media1. `GetVideoEncoderConfigurationsMedia2` correctly replaces the LINQ-to-XML raw message parsing with typed `GetVideoEncoderConfigurationsAsync` calls. Old raw `IMedia2` interface and `Media2GetVideoEncoderConfigurationsRequest` removed from `onvif.services.cs`.

However, `routeMedia` is only used for one operation (`GetVideoEncoderConfigurationsMedia2`). The PLAN.md task 2.4 says "Replace 8 per-operation if/else forks with routeMedia calls" but progress.json notes "Only GetVideoEncoderConfigurationsMedia2 had a GetMedia2Client if/else fork." The branch from `development` indeed only had one Media2 fork in NvtSession, so this is accurate — but it means `GetVideoEncoderConfigurationOptions` was never routed through Media2. This is the root cause of the issue #21 gap.

The `ChannelFactory` type was correctly changed from `IMedia2` to `onvif.services.Media2` (NvtSession.fs:440).

---

## Phase 3 — Docs and tests

PASS — Previously APPROVED (V3).

- `docs/features/media2-routing.md` — accurate description of typed proxy and `routeMedia` helper.
- `docs/architecture.md` — Media2 Service Detection subsection added under Transport Layer.
- `docs/features/media2-testing.md` — references non-existent `Media2IntegrationTests.cs` (aspirational; noted in V3 review as minor, non-blocking).
- No stale test files to remove (confirmed in V3).

---

## Crash fix (d21df8a)

Previously APPROVED. System.Reactive assembly key mismatch resolved by changing `onvif.session` and `onvif.utils` Release|x64 solution mappings from `Release|Net45` to `Release|Net40`. System.Runtime binding redirect added. Crash.log writer added to `App.xaml.cs`.

**Outstanding item (non-blocking):** `Debug|x64` still maps to `Debug|Net45` for both `onvif.session` (GUID 902A3FF3) and `onvif.utils` (GUID 55DED141). This could cause the same key mismatch in Debug builds. Recommended for follow-up.

---

## Build & Tests

UNABLE TO VERIFY — MSBuild and vstest.console.exe invocations were blocked by permission policy in this review session. Prior verification (V3 commit efa2135) reported: "Release x64 build 0 errors, 69/69 offline tests passed." No code changes have been made since then (only feedback.md and review commits added), so the build state is expected to be unchanged.

---

## CI

UNABLE TO VERIFY — `gh pr checks 34` was blocked by permission policy. CI status should be confirmed independently before merge.

---

## Summary

**What passed:**
- Phase 1 (typed proxy generation) — complete and correct
- Phase 3 (docs and test cleanup) — complete
- Crash fix — complete and approved
- WSDL consolidation — complete
- `routeMedia` helper design — correct pattern
- Swiss-army-knife philosophy preserved (graceful fallback, no camera dropped)
- No security issues, no regressions in other views

**What must change before merge:**
- **Issue #21 is not solved end-to-end.** The branch detects H265 encoding via Media2 but does not fetch H265 OPTIONS (resolutions, frame rates) via Media2. The `GetVideoEncoderConfigurationOptions` call in `VideoSettingsActivity.fs` still uses Media1 only, leaving `opts.h265` as NULL. A `GetVideoEncoderConfigurationOptionsMedia2` method using `routeMedia` is needed, and `VideoSettingsActivity.fs` must call it.

**Deferred (non-blocking):**
- Debug|x64 Net45→Net40 alignment for `onvif.session` and `onvif.utils`
- Aspirational test file reference in `media2-testing.md`

---

## Doer Response — Issue #21 Fix (2026-04-18)

**Doer:** odm-dev

### Tasks completed

1. **`GetVideoEncoderConfigurationOptionsMedia2` added to `INvtSession` interface and `NvtSession.fs` implementation** — uses `routeMedia` to call `GetVideoEncoderConfigurationOptionsAsync` on the Media2 proxy, falling back to empty array on failure or Media1-only cameras. Located at `onvif/onvif.session/NvtSession.fs`.

2. **`VideoSettingsActivity.fs` `load()` updated** — after loading the existing encoder config and options, calls `session.GetVideoEncoderConfigurationOptionsMedia2(profile.token)` and synthesizes `options.h265` (resolutions, frame rate range, GOV length range) from the Media2 response whenever the existing `options.h265` is null or has ≤1 resolution entry. Located at `odm/odm.ui.activities/VideoSettingsActivity.fs`.

3. **`odm.sln` Debug|x64 Net45→Net40 fixed** for the two affected GUIDs:
   - `{902A3FF3-E9BD-443D-8FC1-69AA42B5F76B}` (onvif.session)
   - `{55DED141-56C3-4DA9-BE07-03708D7A2275}` (onvif.utils)

### Additional fixes required during build
- Added `odm.onvif.gen` `ProjectReference` to three downstream fsproj files that reference `INvtSession` — `onvif.utils`, `odm.onvif.extensions`, and `odm.ui.activities` — because F# requires a direct assembly reference for any type that appears in a referenced interface's signature. Without this, FS0074 errors cascade through the build graph.
- Corrected indentation of new `member` in `NvtSession.fs` (was 16 spaces, needed 20 to match the enclosing interface block).

### Build result
Release|x64: **0 errors, warnings only** (pre-existing CS0108/CS0169/FS0040 warnings, unchanged from prior sprint).
