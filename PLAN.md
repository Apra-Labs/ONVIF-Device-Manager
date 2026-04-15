# PLAN: Media2 Full Support — GetVideoEncoderConfigurationOptions

**Tracking issue:** https://github.com/Apra-Labs/ONVIF-Device-Manager/issues/21
**Branch:** feat/media2-support (from development)
**Goal:** Fix H265 cameras whose frame rate / resolution / bitrate / govLength sliders show wrong ranges because ODM only fetches options via Media1.

---

## Phase 1 — WCF Interface + Request Type + Response Model

### Task 1.1 — Add GetVideoEncoderConfigurationOptions to IMedia2 + request type
- **File:** `onvif\onvif.services\onvif.services.cs`
- **Change:**
  - Add `BeginGetVideoEncoderConfigurationOptions` and `EndGetVideoEncoderConfigurationOptions` to the `IMedia2` interface (same raw-Message return pattern as `BeginGetVideoEncoderConfigurations`)
  - Add `Media2GetVideoEncoderConfigurationOptionsRequest` message contract with optional `ProfileToken` and `ConfigurationToken` fields
- **Done when:** Project builds. `IMedia2` has two operation pairs.
- **Could block:** WCF namespace mismatch — unlikely since we use raw Message.
- **Tier:** cheap

### Task 1.2 — Add Media2EncoderOptions parsed result type
- **File:** `onvif\onvif.services\onvif.services.cs`
- **Change:**
  - Add `Media2EncoderOptions` class with fields: `Encoding` (string), `ResolutionsAvailable` (VideoResolution[]), `GovLengthRange` (IntRange, nullable), `FrameRateRange` (IntRange), `BitrateRange` (IntRange)
  - This is a plain C# class — no WCF/XML attributes needed (populated by LINQ-to-XML parser, not WCF deserialization)
- **Done when:** Builds. Type is usable from F# code.
- **Could block:** Nothing — pure data class.
- **Tier:** cheap

### VERIFY 1
- [ ] `odm.sln` builds Release x64 with no errors
- [ ] `IMedia2` has both operation pairs
- [ ] `Media2EncoderOptions` and `Media2GetVideoEncoderConfigurationOptionsRequest` types exist

---

## Phase 2 — NvtSession Implementation + XML Parser

### Task 2.1 — Add GetVideoEncoderConfigurationOptionsMedia2 to INvtSession + implementation
- **File:** `onvif\onvif.session\NvtSession.fs`
- **Change:**
  - Add `abstract GetVideoEncoderConfigurationOptionsMedia2: configToken:string -> profileToken:string -> Async<Media2EncoderOptions[]>` to the `INvtSession` interface
  - Implement in the NvtSession class following the exact pattern of `GetVideoEncoderConfigurationsMedia2` (lines 1187–1230):
    1. Call `GetMedia2Client()`; return `[||]` if null
    2. Create `Media2GetVideoEncoderConfigurationOptionsRequest` with configToken/profileToken
    3. `Async.FromBeginEnd` → raw Message
    4. `GetReaderAtBodyContents()` → `ReadOuterXml()` → `XDocument.Parse()`
    5. Parse `<tr2:Options>` elements: extract `<tt:Encoding>`, `<tt:ResolutionsAvailable>` (Width/Height), `<tt:GovLengthRange>` (Min/Max), `<tt:FrameRateRange>` (Min/Max), `<tt:BitrateRange>` (Min/Max)
    6. Return `Media2EncoderOptions[]`
  - Wrap in `try/with err -> return [||]` (graceful fallback, never throw)
- **Done when:** Builds. Unit test (Task 2.2) passes with sample XML.
- **Could block:** XML namespace differences across camera vendors — mitigate by testing with real camera XML captured from issue #21.
- **Tier:** standard

### Task 2.2 — Unit test: XML parsing + fallback
- **File:** `odm\odm.tests\Media2OptionsParserTests.cs` (new)
- **Change:**
  - Extract the LINQ-to-XML parsing logic from Task 2.1 into a static helper method (e.g., `NvtSessionFactory.ParseMedia2EncoderOptions(string xml)`) so it's testable without a WCF channel
  - Test 1: Given a sample `GetVideoEncoderConfigurationOptionsResponse` XML with H265 + H264 Options elements, parser returns correct `Media2EncoderOptions[]` — right encoding, resolutions, ranges
  - Test 2: Given empty/malformed XML, parser returns empty array without throwing
  - Test 3: Given XML with missing optional elements (no GovLengthRange), the corresponding field is null
- **Done when:** `dotnet test` passes all 3 tests.
- **Could block:** If parser is tightly coupled to NvtSession internals — mitigate by extracting to static method.
- **Tier:** standard

### VERIFY 2
- [ ] `odm.sln` builds Release x64
- [ ] `dotnet test` — all Media2OptionsParserTests pass
- [ ] `INvtSession` has `GetVideoEncoderConfigurationOptionsMedia2` member

---

## Phase 3 — VideoSettingsActivity Integration

### Task 3.1 — Use Media2 options in load()
- **File:** `odm\odm.ui.activities\VideoSettingsActivity.fs`
- **Change:**
  - After determining `effectiveEncoding` (around line 80), if effectiveEncoding = H265:
    1. Call `session.GetVideoEncoderConfigurationOptionsMedia2(vec.token, profile.token)`
    2. Find the H265 entry in the returned `Media2EncoderOptions[]`
    3. If found, use its `FrameRateRange`, `BitrateRange`, `GovLengthRange`, `ResolutionsAvailable` to populate the ranges (append to `frameRateRanges`, `bitrateRanges`, `govLengthRanges` lists, or override them)
    4. If not found or empty, fall back to existing Media1 options (no behavior change)
  - Store the Media2 options result in a local binding so `apply_changes` can access it (pass via model or re-fetch)
- **Done when:** Builds. H265 camera shows correct slider ranges from Media2 options.
- **Could block:** Threading/async sequencing — follow existing async pattern.
- **Tier:** standard

### Task 3.2 — Fix apply_changes() H265 hack
- **File:** `odm\odm.ui.activities\VideoSettingsActivity.fs`
- **Change:**
  - In `apply_changes()`, for the `isMedia2OnlyH265` case (lines 311–316):
    1. Fetch Media2 options: `session.GetVideoEncoderConfigurationOptionsMedia2(vec.token, null)`
    2. Find H265 entry → use its ranges for rate/quality/govLength validation instead of `options.h264`
    3. If Media2 options unavailable, fall back to `options.h264` (preserve current behavior as last resort)
  - Keep `vec.encoding <- vec.encoding` (don't send H265 to Media1 endpoint) — this part is correct
  - Set `vec.h265.govLength` using Media2 GovLengthRange when available
- **Done when:** Builds. Applying settings on H265 camera uses correct ranges.
- **Could block:** Some cameras may not support SetVideoEncoderConfiguration with H265 govLength via Media1 — out of scope per requirements.
- **Tier:** standard

### VERIFY 3
- [ ] `odm.sln` builds Release x64
- [ ] All existing tests still pass (no regressions)
- [ ] Manual test with H265 camera: slider ranges match Media2 options, apply succeeds

---

## Phase 4 — Integration Tests

### Task 4.1 — Integration test: Media2 options from real camera
- **File:** `odm\odm.tests\Media2OptionsIntegrationTests.cs` (new)
- **Change:**
  - Guard with `[TestCategory("Integration")]` — skipped when `ODM_TEST_HOST` not set
  - Test 1: Connect to camera, call `GetVideoEncoderConfigurationOptionsMedia2`, verify non-empty result for Media2-capable camera
  - Test 2: Verify returned ranges are reasonable (min < max, positive values, at least one resolution)
- **Done when:** Tests pass against a Media2-capable H265 camera.
- **Could block:** Camera availability — tests are skippable by design.
- **Tier:** standard

### VERIFY 4
- [ ] All unit tests pass
- [ ] Integration tests pass when camera is available, skip cleanly when not

---

## Risk Register

| Risk | Mitigation | Phase |
|------|-----------|-------|
| XML namespace differences across vendors | Parse with namespace-aware XName; test with multiple sample XMLs | 2 |
| Media2 options call fails on some cameras | Graceful fallback to empty array; existing Media1 behavior preserved | 2, 3 |
| WCF channel reuse issues with new operation | Same raw-Message pattern proven by existing GetVideoEncoderConfigurations | 1 |
| F# inline constraint resolution with Media2EncoderOptions | Media2EncoderOptions implements same field names; test at build time | 3 |

---

## Out of Scope
- H265 sprop-vps/sps/pps (issue #20) — separate sprint
- Media2 audio encoder options
- Switching to Media2 SetVideoEncoderConfigurations — keep Media1 apply path
