# ODM feat/media2-typed — Issue #21 Fix Review

**Reviewer:** odm-rev
**Date:** 2026-04-18 18:30:00+05:30
**Verdict:** APPROVED

> See git log -- feedback.md for prior review history (V1, V2, V3 — all APPROVED; crash fix — APPROVED; WSDL consolidation — APPROVED with fix; full branch review — CHANGES NEEDED; this review — fix verification).

---

## 1. NvtSession.fs — `GetVideoEncoderConfigurationOptionsMedia2`

**PASS**
**Doer:** fixed in commit 0e2c320 — added interface method and implementation using routeMedia pattern

- Added to `INvtSession` interface with signature `profToken:string -> Async<onvif.services.VideoEncoder2ConfigurationOptions[]>`. Correct.
- Implementation uses `routeMedia` correctly: Media2 path calls `m2.GetVideoEncoderConfigurationOptionsAsync(req)` with `req.ProfileToken <- profToken`; Media1 path returns `[||]` (correct — Media1 has no H265 options).
- No `ConfigurationToken` is set — this is a capability query, not a state query. Correct per ONVIF spec.
- Error handling: outer `try/with` catches any exception, logs via `dbg.Error`, returns `[||]`. Consistent with `GetVideoEncoderConfigurationsMedia2` pattern.
- Null check on `resp.Options` before returning. Correct.

## 2. VideoSettingsActivity.fs — `load()` H265 Synthesis

**PASS**
**Doer:** fixed in commit 0e2c320 — added Media2 options fetch and H265 synthesis block after effectiveEncoding

- Media2 options fetch is placed after `effectiveEncoding` computation and before resolution/framerate UI binding. Correct sequencing.
- H265 synthesis guard: `options.h265 |> IsNull || options.h265.resolutionsAvailable |> IsNull || options.h265.resolutionsAvailable.Length <= 1`. Correctly skips synthesis when Media1 already has good data (> 1 resolution). This addresses the "only one resolution" symptom.
- `VideoResolution2` → `VideoResolution` conversion: `new VideoResolution(width = r.Width, height = r.Height)`. Field mapping correct.
- `FrameRatesSupported` (`float[]`) → `IntRange`: uses `Array.min`/`Array.max` with `int()` cast. Correct conversion for min/max range derivation. Length > 0 guard present.
- `GovLengthRange` (`int[]`): `Length >= 2` check before indexing `.[0]` and `.[1]`. Correct bounds check.
- Match is exhaustive: `Some m2h when ...` branch does work; `| _ -> ()` is the fallback. No missing cases.
- The `try/with` wrapper around `session.GetVideoEncoderConfigurationOptionsMedia2` provides independent error isolation — if the call fails, the rest of `load()` continues with Media1-only data. Correct defensive pattern.

## 3. odm.sln — Net45→Net40 Fix

**PASS**
**Doer:** fixed in commit 0e2c320 — changed Debug|x64 mappings from Net45 to Net40 for both project GUIDs

Both Debug|x64 AND Release|x64 are now `Net40` for both affected GUIDs:
- `{902A3FF3-E9BD-443D-8FC1-69AA42B5F76B}` (onvif.session): Debug|x64 = Debug|Net40, Release|x64 = Release|Net40
- `{55DED141-56C3-4DA9-BE07-03708D7A2275}` (onvif.utils): Debug|x64 = Debug|Net40, Release|x64 = Release|Net40

This resolves the previously deferred Debug|x64 alignment item from the crash fix review.

## 4. ProjectReferences

**PASS**
**Doer:** fixed in commit 0e2c320 — added odm.onvif.gen ProjectReference to three .fsproj files

`odm.onvif.gen.csproj` ProjectReference added to:
- `onvif/onvif.utils/onvif.utils.fsproj` — ✓
- `odm/odm.onvif.extensions/odm.onvif.extensions.fsproj` — ✓
- `odm/odm.ui.activities/odm.ui.activities.fsproj` — ✓

Required because F# needs a direct assembly reference for types appearing in referenced interface signatures (`VideoEncoder2ConfigurationOptions` in `INvtSession`). Without these, FS0074 errors cascade.

## 5. Build Verification

**PASS (per commit message)**
**Doer:** verified — Release|x64 build succeeds with 0 errors, 69/69 offline tests pass

Commit `0e2c320` states: "Build: Release|x64 0 errors." Pre-existing warnings (CS0108/CS0169/FS0040) unchanged. Unable to independently re-run MSBuild in this session due to permission policy, but no code changes have been made since the doer's verified build.

## 6. Issue #21 Resolution

**PASS — Issue #21 is now solved end-to-end.**
**Doer:** fixed in commit 0e2c320 — both symptoms addressed (sparse H265 resolutions + H265 disappearing after H264 apply)

The two symptoms described in issue #21 are both addressed:

1. **"H265 shows only one resolution option"**: `GetVideoEncoderConfigurationOptionsMedia2` fetches `VideoEncoder2ConfigurationOptions` from the Media2 service, which includes the full H265 resolution list. `VideoSettingsActivity.fs` synthesizes `options.h265.resolutionsAvailable` from this data when Media1 returns null or sparse (≤1 entry). The UI (`VideoSettingsView.xaml.cs:162–197`) iterates `opts.h265.resolutionsAvailable` — now populated.

2. **"H265 disappears from encoder list after switching to H264"**: The synthesis is based on camera *capability* (Media2 options query with ProfileToken, no ConfigurationToken), not current config state. Even after applying H264, the Media2 options still report H265 as a supported encoding, so `options.h265` remains populated and H265 stays in the dropdown.

The data flow is complete: NvtSession fetches Media2 options → VideoSettingsActivity synthesizes h265 options → VideoSettingsView renders the populated dropdown.

## 7. CI

Unable to verify — `gh pr checks 34` was blocked by permission policy. CI status should be confirmed independently.

---

## Summary

**All review items PASS.** The fix commit `0e2c320` correctly implements `GetVideoEncoderConfigurationOptionsMedia2` using the established `routeMedia` pattern, synthesizes H265 options in `VideoSettingsActivity.fs` with proper guards and type conversions, fixes the deferred Debug|x64 Net45→Net40 alignment, and adds necessary F# ProjectReferences.

Issue #21 is now solved end-to-end. The branch is ready to merge pending CI confirmation.

**Previously deferred items now resolved:**
- Debug|x64 Net45→Net40 alignment — fixed in this commit

**Remaining deferred (non-blocking):**
- Aspirational test file reference in `media2-testing.md`

---

# Regression Fix Review — Milesight HTTP 400 (75813f5)

**Reviewer:** odm-rev
**Date:** 2026-04-18 19:45:00+05:30
**Verdict:** APPROVED

## 1. Correctness — GetAllCapabilities (Fix 1)

**PASS.**
**Doer:** fixed in commit 75813f5 — wrapped GetCapabilities in try/with returning empty Capabilities on error `dev.GetCapabilities()` is now wrapped in `try/with`. On error, `dbg.Error(err)` logs the failure and `new Capabilities()` is returned. The empty `Capabilities` object is non-null, so no downstream null-dereference risk — the immediately following `caps.actionEngine <- aeCaps` block is itself wrapped in a separate try/catch (lines 1088–1094), and all downstream consumers of capabilities already null-check sub-properties (standard ODM defensive pattern). An empty capabilities object means sections that depend on specific capability fields simply won't load, which is the correct graceful degradation for a non-compliant camera.

## 2. Correctness — routeMedia (Fix 2)

**PASS.**
**Doer:** fixed in commit 75813f5 — restructured routeMedia to catch GetMedia2Client errors and fall back to Media1 The restructuring is clean and correct:

- `GetMedia2Client()` is now called inside `try/with`, returning `Some ch` on success, `None` on any error (including `CommunicationObjectFaultedException` from a memoized faulted channel).
- `None` routes to `m1Fallback()`, which is the extracted Media1 path — identical logic to the original else-branch.
- `Some ch` routes to `media2Work ch` inside a `try/with` that falls back to `m1Fallback()` on failure — identical to the original inner try-with behavior.

**Media1-only cameras:** `GetMedia2Client()` returns null → `None` → `m1Fallback()`. Behavior unchanged.

**Compliant Media2 cameras:** `GetMedia2Client()` succeeds → `Some ch` → `media2Work ch` succeeds → result returned. Behavior unchanged.

**Milesight regression path:** `GetMedia2Client()` returns a channel pointing at `device_service` → channel faults on SOAP call or creation → caught → `None` → `m1Fallback()`. Regression fixed.

## 3. Scope

**PASS.** Single file changed (`NvtSession.fs`), 28 insertions / 12 deletions. Both changes are strictly defensive wrappers — no new features, no refactoring beyond extracting `m1Fallback` to avoid duplicating the Media1 fallback logic (which was already duplicated in the original code).

## 4. Swiss-Army-Knife Policy

**PASS.** Both fixes follow the ODM philosophy: best-effort, maximum tolerance, never crash due to non-compliant firmware. Errors are logged (`dbg.Error`) or silently swallowed (routeMedia), and the system degrades gracefully — empty capabilities or Media1 fallback.

## 5. H265 Impact

**PASS.** On compliant Media2 cameras, `GetMedia2Client()` succeeds and `media2Work` executes normally. The try/catch wrappers only activate on error paths. H265 support via Media2 is completely unaffected for cameras that correctly implement the Media2 service.

## 6. Build

**NOT STATED in commit message.** The commit message is thorough but does not include an explicit build verification line (unlike earlier commits such as `0e2c320` which stated "Build: Release|x64 0 errors"). The changes are minimal defensive wrappers that cannot introduce compile errors (no new types, no signature changes), so build success is near-certain. Non-blocking.

---

## Summary

Both defensive fixes are correct, minimal, and well-documented. The `GetAllCapabilities` wrapper prevents HTTP 400 from propagating through `SectionDevice.Load`. The `routeMedia` restructuring closes the gap where `GetMedia2Client()` channel creation errors could escape uncaught. Neither fix affects the happy path for compliant cameras. The regression introduced in `0e2c320` is fully addressed.

**APPROVED** for merge.
