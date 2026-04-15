# Media2 Full Support — Plan Review

**Reviewer:** odm-rev
**Date:** 2026-04-14 23:30:00-04:00
**Verdict:** APPROVED

---

## 1. Clear "Done" Criteria

**PASS.** Every task has explicit, verifiable "done when" criteria. Task 1.1: "odm.sln builds Release x64. IMedia2 has 7 operation pairs (1 existing + 6 new)." Task 2.1: "Builds. Unit test for profile XML parsing passes." Test tasks specify exact test counts. VERIFY checkpoints add a second layer of acceptance criteria.

---

## 2. High Cohesion / Low Coupling

**PASS.** Each task touches a single concern: Task 1.1 is pure interface, Task 1.2 is a data type, Tasks 2.1/2.2 are each one operation's routing, and so on. No task mixes interface changes with routing implementation or tests with production code. The coupling between tasks is limited to compile-time type dependencies (Phase 2 depends on Phase 1 types).

---

## 3. Key Abstractions and Shared Interfaces in Earliest Tasks

**PASS.** Phase 1 establishes ALL shared infrastructure: the 6 IMedia2 operation contracts, 6 request message types, and the Media2EncoderOptions parsed result type. Every subsequent phase consumes these types. The routing pattern (check Media2 → try → fallback) is documented in the Architecture Overview and first applied in Phase 2, which other phases replicate.

---

## 4. Riskiest Assumption Validated in Task 1

**PASS.** The riskiest assumption is that WCF raw-Message operations with custom request contracts will compile and serialize correctly against the ONVIF Media2 WSDL actions. Task 1.1 validates this at build time. Task 2.1 then validates the full round-trip (WCF call → XML response → domain type mapping) for the most complex operation (GetProfiles with its rich schema). The plan correctly identifies "WCF action URL typo" as the could-block risk for Task 1.1.

---

## 5. Later Tasks Reuse Early Abstractions (DRY)

**PASS.** The routing pattern is established once in Phase 2 (GetProfiles + GetStreamUri) and explicitly reused in Phases 3, 4, and 5 — each task description references the same `GetMedia2Client() → try Media2 → fallback Media1` structure. XML parsing uses the same `Async.FromBeginEnd → raw Message → XDocument → LINQ-to-XML` pipeline throughout. The `FixUrl()` helper is reused for both GetStreamUri (2.2) and GetSnapshotUri (5.1). Request types from Phase 1 are consumed by all subsequent phases.

---

## 6. 2-3 Work Tasks per Phase, Then VERIFY Checkpoint

**PASS.** Phase 1: 2 tasks + VERIFY. Phase 2: 3 tasks + VERIFY. Phase 3: 3 tasks + VERIFY. Phase 4: 3 tasks + VERIFY. Phase 5: 3 tasks + VERIFY. All phases have exactly 2-3 work tasks followed by a VERIFY checkpoint with checkboxes. The verify steps are substantive — they check builds, tests, and behavioral properties (e.g., "H265 camera: Video Settings page shows correct slider ranges").

---

## 7. Each Task Completable in One Session

**PASS.** Tasks are well-scoped. Interface-only tasks (1.1, 1.2) are straightforward WCF boilerplate. Routing tasks (2.1, 2.2, 3.1, 3.2, 4.1, 4.2, 5.1) each add one private helper function and modify one existing method — the plan gives exact line numbers. Test tasks (2.3, 3.3, 4.3) specify exact test counts (4, 4, 3 respectively). The cleanup task (5.2) is bounded by a codebase grep. Each task includes a tier rating (cheap/standard) confirming session-level scope.

---

## 8. Dependencies Satisfied in Order

**PASS.** Dependency chain is clean and linear:
- Phase 1 (types) → Phase 2 (uses types for GetProfiles/GetStreamUri)
- Phase 2 (establishes pattern + tests) → Phase 3 (applies pattern to options/set)
- Phase 3 (routing proven) → Phase 4 (more operations using same pattern)
- Phase 4 (all routing in place) → Phase 5 (GetSnapshotUri + retire old bridge method + integration tests)

Task 5.2 (retire GetVideoEncoderConfigurationsMedia2) correctly depends on Phase 4 completing first — the new routing must be proven before removing the old bridge.

---

## 9. Vague Tasks That Two Developers Would Interpret Differently

**PASS.** Tasks are unusually precise. Each specifies: exact file, exact line numbers in the current codebase, the helper function name and signature, the XML elements to parse, the domain type to return, and the existing method to modify. For example, Task 2.1 specifies parsing `<tr2:Profiles>` elements, extracting `token` (attribute), `name`, `VideoSourceConfiguration`, and `VideoEncoderConfiguration` with specific sub-fields. Task 3.2 specifies that `ForcePersistence` is NOT sent on the Media2 path. No ambiguity found.

---

## 10. Hidden Dependencies Between Tasks

**NOTE.** One minor dependency worth calling out: Task 4.1 mentions "reuse the existing request type but add optional `ProfileToken` and `ConfigurationToken` fields to it (or use the request from Task 1.1 that includes these fields)." This ambiguity could cause a micro-conflict if Task 1.1 doesn't include those optional fields on the existing `GetVideoEncoderConfigurations` request type. However, the plan handles this with the parenthetical "or" — the implementer has a clear fallback path. No hidden blockers found.

---

## 11. Risk Register

**PASS.** The risk register covers 7 risks across all phases with specific mitigations:
- XML namespace vendor differences → namespace-aware parsing + multi-vendor test samples
- Profile schema mapping loss → field audit + warnings for unmapped fields
- SetVideoEncoderConfigurations rejection → spec-conformant XML + early real-camera testing
- Per-operation failure → independent try/catch fallback (not all-or-nothing)
- Retiring bridge method → full codebase grep before removal
- WCF channel state corruption → separate channel factories (with line numbers)
- Performance regression → memoized detection (with line number)

All risks have concrete, actionable mitigations tied to specific phases. The register is thorough.

---

## 12. Full Media2 Routing Vision Coverage

**PASS.** The plan covers ALL 7 operations specified in requirements:

| Requirement | Plan Task | Phase |
|-------------|-----------|-------|
| GetProfiles | Task 2.1 | 2 |
| GetStreamUri | Task 2.2 | 2 |
| GetVideoEncoderConfigurationOptions | Task 3.1 | 3 |
| SetVideoEncoderConfiguration | Task 3.2 | 3 |
| GetCompatibleVideoEncoderConfigurations | Task 4.1 | 4 |
| GetVideoSourceConfigurations | Task 4.2 | 4 |
| GetSnapshotUri | Task 5.1 | 5 |

The plan explicitly states the goal: "When a camera supports Media2, ALL video operations route through Media2 transparently. Media1 becomes the fallback." This matches the requirements vision exactly. The plan also includes retiring the temporary `GetVideoEncoderConfigurationsMedia2` bridge (Task 5.2), which the requirements don't explicitly call for but which is the correct cleanup to avoid a dual-API surface.

---

## Summary

**All 12 checks pass.** The revised plan is comprehensive, well-structured, and fully aligned with the Media2 full routing vision. Every video operation in the requirements has a corresponding task with clear implementation details and done criteria. The phasing is logical — interfaces first, then the most-used operations (profiles + streaming), then the issue #21 root cause (video settings), then supporting operations, and finally cleanup and integration tests. The risk register is thorough with actionable mitigations. No changes needed.

**Strengths:**
- Exact line numbers and function signatures throughout — zero ambiguity
- Each phase builds on the previous one's proven pattern
- Independent per-operation fallback (not all-or-nothing) is the right resilience model
- Retiring the temporary bridge method (Task 5.2) is good housekeeping

**Deferred (out of scope, acknowledged):**
- Issue #20: H265 sprop-vps/sps/pps (live555) — separate sprint
- Media2 audio/PTZ/analytics operations

---
---

# Phase 1 Code Review — IMedia2 WCF Interface Expansion (Tasks 1.1 + 1.2)

**Reviewer:** odm-rev
**Date:** 2026-04-14 23:55:00-04:00
**Scope:** Diff from `238eb17..a857e1d` — changes to `onvif/onvif.services/onvif.services.cs`
**Verdict:** APPROVED

---

## 1. All 6 Operation Pairs Present and Correctly Structured

**PASS.** All 6 new operations added to `IMedia2` interface with correct Begin/End async pattern:

| Operation | Action URL | Request Type |
|-----------|-----------|--------------|
| GetProfiles | `.../GetProfiles` | `Media2GetProfilesRequest` |
| GetStreamUri | `.../GetStreamUri` | `Media2GetStreamUriRequest` |
| GetVideoEncoderConfigurationOptions | `.../GetVideoEncoderConfigurationOptions` | `Media2GetVideoEncoderConfigurationOptionsRequest` |
| SetVideoEncoderConfigurations | `.../SetVideoEncoderConfigurations` | `Media2SetVideoEncoderConfigurationsRequest` |
| GetVideoSourceConfigurations | `.../GetVideoSourceConfigurations` | `Media2GetVideoSourceConfigurationsRequest` |
| GetSnapshotUri | `.../GetSnapshotUri` | `Media2GetSnapshotUriRequest` |

All Action URLs correctly use `http://www.onvif.org/ver20/media/wsdl/<OperationName>`. All `ReplyAction = "*"`. All `End` methods return `System.ServiceModel.Channels.Message` (raw message for LINQ-to-XML parsing). The `SetVideoEncoderConfigurations` plural form matches the ONVIF Media2 spec (confirmed in `requirements.md` line 82: `tr2:SetVideoEncoderConfigurations (note: plural)`).

Total IMedia2 operation count: 7 (1 existing + 6 new). Matches Task 1.1 done criteria.

---

## 2. Request Types — MessageContract and MessageBodyMember Attributes

**PASS.** All 6 request classes have correct `[MessageContract]` with:
- `WrapperName` matching the SOAP operation name
- `WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl"` (consistent across all)
- `IsWrapped = true`

`[MessageBodyMember]` attributes verified:
- `Media2GetProfilesRequest`: `Token` (Order=0), `Type` (Order=1) — correct per Media2 GetProfiles schema
- `Media2GetStreamUriRequest`: `ProfileToken` (Order=0), `Protocol` (Order=1) — correct
- `Media2GetVideoEncoderConfigurationOptionsRequest`: `ConfigurationToken` (Order=0), `ProfileToken` (Order=1) — correct
- `Media2SetVideoEncoderConfigurationsRequest`: `Configuration` as `XElement` (Order=0) — correct; raw XML allows constructing the Media2-specific `VideoEncoder2Configuration` without needing generated WCF types
- `Media2GetVideoSourceConfigurationsRequest`: `ConfigurationToken` (Order=0), `ProfileToken` (Order=1) — correct
- `Media2GetSnapshotUriRequest`: `ProfileToken` (Order=0) — correct

All `[MessageBodyMember]` attributes use `Namespace = "http://www.onvif.org/ver20/media/wsdl"`. Order values sequential starting from 0. Default constructors present on all request types.

---

## 3. Media2EncoderOptions Class

**PASS.** All required fields present with correct types:
- `Encoding` (`string`) — encoding identifier (H264, H265, JPEG, etc.)
- `ResolutionsAvailable` (`VideoResolution[]`) — reuses existing domain type
- `GovLengthRange` (`IntRange`) — reference type, implicitly nullable; comment documents this correctly
- `FrameRateRange` (`IntRange`) — required range
- `BitrateRange` (`IntRange`) — required range

The class is a plain POCO with auto-properties, suitable for population by the LINQ-to-XML parser in NvtSession.fs (Phase 2+).

---

## 4. No Changes to INvtSession or Activity Files

**PASS.** The diff (`238eb17..a857e1d`) touches exactly two files:
- `onvif/onvif.services/onvif.services.cs` — the IMedia2 interface expansion (expected)
- `feedback.md` — plan review output (expected)

No changes to:
- `onvif/onvif.session/NvtSession.fs` (INvtSession implementation)
- `onvif/onvif.session/Services/MediaAsync.fs` (Media1 proxy)
- `odm/odm.ui.activities/VideoSettingsActivity.fs` or any activity files
- Any `.fs` or `.fsi` files whatsoever

This is correct — Phase 1 is interface-only. Routing and consumption come in Phase 2+.

---

## 5. Build Clean and Tests Pass

**PASS.** Per progress.json V1 entry and commit `901a066`:
- Release x64 build: **0 errors** (warnings only)
- Unit tests: **69/69 passed** (TestCategory!=Integration)
- No regressions introduced

---

## Summary

**All 5 checks pass.** Phase 1 implementation is correct and complete. The IMedia2 WCF interface now has all 7 operation pairs needed for the full Media2 routing vision. Request types are correctly attributed for WCF serialization. The Media2EncoderOptions data class is ready for consumption by the XML parser. No scope creep — changes are confined to the service interface layer as planned.

**No findings. No changes needed. Proceed to Phase 2.**

---
---

# Phase 2 Code Review — GetProfiles + GetStreamUri Routing (Tasks 2.1, 2.2, 2.3)

**Reviewer:** odm-rev
**Date:** 2026-04-14 23:59:00-04:00
**Scope:** Commits `7e53ff2`, `f87b32e`, `45ebd00`, `97da512` on `feat/media2-support` (diff from `c5704a7..97da512`)
**Verdict:** APPROVED

---

## 1. GetProfiles Routing

**PASS.** `GetProfiles()` in `NvtSession.fs` correctly implements the Media2-first routing pattern:

1. Calls `GetMedia2Client()` first
2. If Media2 is available (`NotNull`), calls `getProfilesViaMedia2` inside a `try` block
3. On failure, logs via `dbg.Error(err)` and falls back to `GetMediaClient() → med.GetProfiles()`
4. If Media2 is null, goes directly to Media1
5. Returns `[||]` if neither client is available

The `getProfilesViaMedia2` helper uses `Async.FromBeginEnd` with `Media2GetProfilesRequest`, reads the raw `Message` body via `GetReaderAtBodyContents().ReadOuterXml()`, and delegates to `Media2XmlParser.ParseGetProfilesResponse`. The response message is correctly disposed with `use response = response`. Pattern is clean and matches the plan.

---

## 2. XML Parser — Namespace and Field Coverage

**PASS.** `Media2XmlParser` (static class in `onvif.services.cs`) uses correct namespaces:

- `NsTr2 = "http://www.onvif.org/ver20/media/wsdl"` — Media2 service namespace for response wrapper elements (`Profiles`, `Uri`)
- `NsTt = "http://www.onvif.org/ver10/schema"` — ONVIF common schema for data elements (`Name`, `VideoSourceConfiguration`, etc.)

`ParseGetProfilesResponse` maps all fields the activities need:

| Field | Source | Mapped |
|-------|--------|--------|
| `token` | `Profiles/@token` attribute | ✓ |
| `name` | `tt:Name` element | ✓ |
| `videoSourceConfiguration.token` | `tt:VideoSourceConfiguration/@token` | ✓ |
| `videoSourceConfiguration.name` | `tt:VideoSourceConfiguration/tt:Name` | ✓ |
| `videoSourceConfiguration.sourceToken` | `tt:VideoSourceConfiguration/tt:SourceToken` | ✓ |
| `videoEncoderConfiguration.*` | Full parse via `ParseVideoEncoderConfigElement` | ✓ |

`ParseVideoEncoderConfigElement` covers: `token`, `name`, `Encoding` (with H264/H265/JPEG/MPEG4 switch), `Resolution` (width/height), `RateControl` (frameRateLimit/bitrateLimit/encodingInterval), `H264.GovLength`, and `H265.GovLength`. All fields used by `VideoSettingsActivity` and `ProfilesActivity` are present.

Defensive behavior: profiles without a `token` attribute are skipped. Null/empty body XML returns `new Profile[0]`. The encoding switch defaults unknown values to `h264` — reasonable, though a debug log would improve diagnostics. Minor, not blocking.

---

## 3. GetStreamUri Routing

**PASS.** `GetStreamUri()` follows the same routing pattern as `GetProfiles()`:

1. `GetMedia2Client()` → if available, try `getStreamUriViaMedia2 media2 token`
2. On failure, fall back to Media1 path with `FixUrl()` applied
3. If Media2 null, use Media1 directly

The `getStreamUriViaMedia2` helper:
- Sets `request.Protocol <- "RtspUnicast"` — correct Media2 protocol string
- Calls `FixUrl()` on the parsed URI — **confirmed**, the URI is wrapped in `new Uri(uriStr, UriKind.RelativeOrAbsolute)`, passed to `FixUrl()`, and the result's `OriginalString` is assigned to `mediaUri.uri`
- Returns a `MediaUri` object matching the Media1 return type
- Handles empty/null URI by setting `mediaUri.uri <- null`

The Media1 fallback path preserves the original `FixUrl()` logic exactly as it existed before the change. No regression.

---

## 4. Unit Tests

**PASS.** `Media2XmlParserTests.cs` contains 4 well-structured tests covering the right scenarios:

| Test | What it validates |
|------|-------------------|
| `ParseGetProfilesResponse_TwoProfiles_ReturnsBothWithCorrectFields` | Two profiles (H264 + H265) with full field assertions: token, name, VSC, VEC, encoding enum, resolution, rate control, GOV length |
| `ParseGetProfilesResponse_NoProfiles_ReturnsEmptyArray` | Empty response returns `Profile[0]`, not null |
| `ParseGetStreamUriResponse_ValidUri_ReturnsUriString` | Extracts `rtsp://` URI from valid response |
| `ParseGetStreamUriResponse_EmptyUri_ReturnsNull` | Empty `<tr2:Uri>` returns null, not empty string |

Test XML uses correct `tr2`/`tt` namespace prefixes matching real camera responses. The two-profile test asserts on nearly every parsed field — high confidence that the parser is correct. All tests are tagged `[TestCategory("Unit")]` for test filtering.

---

## 5. No Changes to INvtSession Interface or Activity Files

**PASS.** The Phase 2 diff (`c5704a7..97da512`) touches exactly:
- `onvif/onvif.services/onvif.services.cs` — new `Media2XmlParser` class (expected)
- `onvif/onvif.session/NvtSession.fs` — routing logic + helpers (expected)
- `odm/odm.tests/Media2XmlParserTests.cs` — new test file (expected)
- `progress.json` — status tracking (expected)

No changes to:
- `onvif/onvif.session/INvtSession.fs` — interface unchanged ✓
- `odm/odm.ui.activities/` — no activity files touched ✓
- Any `.fsi` signature files

---

## 6. Build Clean, 73 Tests Pass

**PASS.** Per progress.json V2 entry: Release x64 build passed (warnings only, 0 errors). 73/73 tests passed (69 existing + 4 new Media2XmlParser tests). No regressions.

---

## Summary

**All 6 checks pass.** Phase 2 implementation is correct, clean, and complete. The Media2-first routing pattern is established for `GetProfiles` and `GetStreamUri` with proper error handling and transparent Media1 fallback. The `Media2XmlParser` is well-tested and maps all fields needed by downstream activities. `FixUrl()` is correctly applied on both Media2 and Media1 paths for `GetStreamUri`. No interface changes, no scope creep.

**Minor observations (not blocking):**
- The encoding switch in `ParseVideoEncoderConfigElement` defaults unknown encodings to `h264` silently. A `dbg.Warning` for unrecognized encoding strings would aid debugging with unusual cameras. Low priority — can be added in a later phase.
- `int.Parse()` calls in the XML parser could throw on malformed XML. Since this is wrapped in the `try/with` at the routing level (which falls back to Media1), this is safe in practice. No action needed.

**No changes needed. Proceed to Phase 3.**

---
---

# Phase 3 Code Review — GetVideoEncoderConfigurationOptions + SetVideoEncoderConfiguration

**Reviewer:** odm-rev
**Date:** 2026-04-15 10:00:00-04:00
**Verdict:** APPROVED

**Commits reviewed:**
- `0b93951` — Tasks 3.1 + 3.2: GetVideoEncoderConfigurationOptions + SetVideoEncoderConfiguration Media2 routing
- `94ce966` — Task 3.3: Tests 5–8 appended to Media2XmlParserTests.cs
- `99a244d` — VERIFY 3 progress.json update

---

## 1. GetVideoEncoderConfigurationOptions Routing

**PASS.** `NvtSession.fs` routes `GetVideoEncoderConfigurationOptions` through Media2 first via the new `getVideoEncoderConfigurationOptionsViaMedia2` helper. On failure, `dbg.Error(err)` logs the exception and falls back to the Media1 `med.GetVideoEncoderConfigurationOptions(configToken, profToken)` call. When no Media2 client is available (`media2` is null), it goes directly to Media1. This matches the established pattern from Phase 2.

The helper correctly constructs `Media2GetVideoEncoderConfigurationOptionsRequest`, optionally sets `ConfigurationToken` and `ProfileToken` only when non-empty, reads the raw body XML from the WCF `Message`, and delegates parsing to `Media2XmlParser.ParseGetVideoEncoderConfigurationOptionsResponse`.

---

## 2. H265 Ranges (Issue #21 Root Fix)

**PASS.** `ParseGetVideoEncoderConfigurationOptionsResponse` iterates `tr2:Options` blocks, reads the `tt:Encoding` element, and maps each block into the correct sub-object on `VideoEncoderConfigurationOptions`:

| Encoding | Target sub-object | Fields populated |
|----------|-------------------|------------------|
| H265 | `opts.h265` (H265Options) | resolutionsAvailable, govLengthRange, frameRateRange, encodingIntervalRange |
| H264 | `opts.h264` (H264Options) | resolutionsAvailable, govLengthRange, frameRateRange, encodingIntervalRange |
| JPEG | `opts.jpeg` (JpegOptions) | resolutionsAvailable, frameRateRange, encodingIntervalRange |

GovLengthRange, FrameRateRange, and ResolutionsAvailable are correctly populated using the `ParseIntRange` and `ParseResolutionsAvailable` private helpers. When an element is absent from the XML, the range is `null` (not default-initialized) — confirmed by Test 6.

**NOTE (non-blocking):** `BitrateRange` is parsed from the XML into the local `bpsRange` variable but never assigned to any sub-object. This is because `H265Options` / `H264Options` do not have a `bitrateRange` property (only `H265Options2` / `H264Options2` do). The variable is effectively dead code. Not a bug — the existing ODM type system cannot store it — but the unused variable should be removed in a future cleanup pass to avoid confusion.

---

## 3. SetVideoEncoderConfiguration Routing

**PASS.** `NvtSession.fs` routes `SetVideoEncoderConfiguration` through Media2 first via `setVideoEncoderConfigurationViaMedia2`. Key observations:

- **ForcePersistence correctly omitted:** The Media2 spec does not support `ForcePersistence`. The helper does not pass it. The fallback Media1 path correctly preserves the original `forcePersistence` parameter.
- **XML body construction:** `BuildSetVideoEncoderConfigurationElement` produces a `tt:Configuration` XElement with the correct structure: `token` attribute, `Name`, `Encoding` (mapped to string: H264/H265/JPEG/MPEG4), `Resolution`, `RateControl`, codec-specific block (H264 or H265 with `GovLength`), and `Quality`.
- **Response handling:** The set operation correctly consumes the response body (which is empty on success). The `response.IsEmpty` check before attempting to read prevents errors on truly empty responses.
- **Fallback:** On Media2 failure, logs error and falls back to `med.SetVideoEncoderConfiguration(config, forcePersistence)`.

---

## 4. Unit Tests 5–8

**PASS.** All four tests have meaningful, specific assertions — not just "no exception" checks.

| Test | Scenario | Key assertions |
|------|----------|---------------|
| Test 5 | H265 + H264 options parsing | `opts.h265` and `opts.h264` populated with correct resolution counts/values, govLengthRange min/max, frameRateRange min/max; `opts.jpeg` is null |
| Test 6 | Missing GovLengthRange | `opts.h265.govLengthRange` is null (not default), no exception thrown |
| Test 7 | Empty response body | Returns non-null default options, all sub-objects null |
| Test 8 | BuildSetVEC H265 XML | Verifies token attribute, Name, Encoding="H265", Resolution Width/Height, RateControl fields, H265/GovLength=60, H264 element absent |

Test XML uses correct `tr2`/`tt` namespace prefixes. Test 5 is the critical one for issue #21 — it directly verifies that H265 options are correctly parsed from a Media2 response. Test 8 verifies round-trip correctness of the SetVEC XML builder with explicit element-by-element assertions.

---

## 5. No Changes to INvtSession Interface or Activity Files

**PASS.** The Phase 3 diff (`24f997f..94ce966`) touches exactly:
- `onvif/onvif.services/onvif.services.cs` — new parser/builder methods on `Media2XmlParser` (expected)
- `onvif/onvif.session/NvtSession.fs` — routing logic + helpers (expected)
- `odm/odm.tests/Media2XmlParserTests.cs` — tests 5–8 appended (expected)
- `progress.json` — status tracking (expected)

No changes to:
- `onvif/onvif.session/INvtSession.fs` — interface unchanged
- `odm/odm.ui.activities/` — no activity files touched
- `odm/odm.ui.views/` — no view files touched

---

## 6. Build Clean, 77 Tests Pass

**PASS.** Per progress.json V3 entry: Release x64 build passed (warnings only, 0 errors). 77/77 tests passed (73 prior + 4 new Phase 3 tests). No regressions from Phase 2.

---

## Summary

**All 6 checks pass.** Phase 3 correctly implements the root fix for issue #21: `GetVideoEncoderConfigurationOptions` now routes through Media2, properly mapping H265/H264/JPEG option blocks into the existing ODM type system. `SetVideoEncoderConfiguration` correctly routes through Media2 without ForcePersistence. Both operations have transparent Media1 fallback. Tests are thorough with meaningful assertions.

**Minor observation (not blocking):**
- The `bpsRange` variable in `ParseGetVideoEncoderConfigurationOptionsResponse` is computed from `BitrateRange` XML but never assigned. This is a dead variable since the target types (`H265Options`, `H264Options`) lack a `bitrateRange` field. Remove the variable in a future cleanup to avoid confusion.

**No changes needed. Proceed to Phase 4.**

---
---

# Phase 4 Code Review — GetCompatibleVideoEncoderConfigurations + GetVideoEncoderConfigurations + GetVideoSourceConfigurations (Tasks 4.1, 4.2, 4.3)

**Reviewer:** odm-rev
**Date:** 2026-04-15 12:00:00-04:00
**Scope:** Commits `49282f3`, `9306552` on `feat/media2-support` (diff from `f667a06..9306552`)
**Verdict:** APPROVED

---

## 1. GetVideoEncoderConfigurations — Shared Helper + Full-Field Parser + Media1 Fallback

**PASS.** `NvtSession.fs` introduces `getEncoderConfigurationsViaMedia2` as a shared helper used by both `GetVideoEncoderConfigurations()` and `GetCompatibleVideoEncoderConfigurations()`. The helper:

- Constructs `Media2GetVideoEncoderConfigurationsRequest` and optionally sets `ProfileToken` when non-empty (for compatible-configs filtering)
- Uses the standard `Async.FromBeginEnd → raw Message → ReadOuterXml` pipeline
- Delegates parsing to `Media2XmlParser.ParseGetVideoEncoderConfigurationsResponse`

The parser (`onvif.services.cs` line ~994) iterates `tr2:Configurations` elements and reuses the shared `ParseVideoEncoderConfigElement` helper established in Phase 2 — **DRY confirmed**. This means every encoder config field (token, name, encoding, resolution, rateControl, govLength, h264/h265 profile) is populated via the same code path that parses profiles.

`GetVideoEncoderConfigurations()` routing: Media2 first → on failure falls back to Media1 with the original `FaultException` handling for `ActionNotSupported`. `GetCompatibleVideoEncoderConfigurations(profToken)` routing: Media2 first (passing `profToken` to filter) → on failure falls back to Media1's `GetCompatibleVideoEncoderConfigurations(profToken)` → on `ActionNotSupported`, further falls back to `this.GetVideoEncoderConfigurations()`. Fallback chain is correct and preserves pre-existing behavior.

**NOTE (non-blocking):** The `ConfigurationToken` and `ProfileToken` fields were added to `Media2GetVideoEncoderConfigurationsRequest` in this phase (commit `49282f3`), resolving the ambiguity noted in Check 10 of the plan review. This is the correct approach — the Phase 1 request type was extended rather than creating a new one.

---

## 2. GetVideoSourceConfigurations — Field Mapping + Media1 Fallback

**PASS.** `getVideoSourceConfigurationsViaMedia2` helper follows the established pattern. `ParseGetVideoSourceConfigurationsResponse` correctly maps:

| XML Source | Domain Field |
|------------|-------------|
| `tr2:Configurations/@token` | `vsc.token` |
| `tt:Name` | `vsc.name` |
| `tt:SourceToken` | `vsc.sourceToken` |
| `tt:Bounds/@x` | `vsc.bounds.x` |
| `tt:Bounds/@y` | `vsc.bounds.y` |
| `tt:Bounds/@width` | `vsc.bounds.width` |
| `tt:Bounds/@height` | `vsc.bounds.height` |

All bounds attributes use `int.TryParse` for safe parsing — no exception risk from malformed XML. The `IntRectangle` is only assigned when the `tt:Bounds` element is present. Configurations without a `token` attribute are skipped (consistent with the encoder config parser).

`GetVideoSourceConfigurations()` routing: Media2 first → on failure falls back to Media1 → returns `[||]` if neither client is available. Correct.

---

## 3. Unit Tests 9–11

**PASS.** Three well-structured tests with meaningful field-level assertions:

| Test | Scenario | Key Assertions |
|------|----------|---------------|
| Test 9 | Two encoder configs (H265 + H264) | Full field coverage: token, name, encoding enum, resolution width/height, rateControl frameRateLimit/bitrateLimit, h265.govLength=60, h264.govLength=30, cross-codec null checks (h264 null on H265 config and vice versa) |
| Test 10 | Two video source configs | token, name, sourceToken, bounds x/y/width/height for both configs |
| Test 11 | Empty encoder + source responses | Both return empty arrays (not null), no exceptions thrown |

Test 9 is particularly thorough — it validates that the full-field parser (via shared `ParseVideoEncoderConfigElement`) correctly populates all fields when called through `ParseGetVideoEncoderConfigurationsResponse`. This is a stronger test than the Phase 2 profile test because it isolates the encoder config parsing from the profile wrapper.

All tests use correct `tr2`/`tt` namespace prefixes and are tagged `[TestCategory("Unit")]`.

---

## 4. Build and Test Results

**PASS.** Per progress.json V4 entry: Release x64 build passed (warnings only, 0 errors). 80/80 unit tests passed (77 prior + 3 new Phase 4 tests). No regressions.

---

## 5. Code Duplication Observation

**NOTE (non-blocking).** The Media2-first routing pattern in `NvtSession.fs` duplicates the Media1 fallback code in the `else` branch (when `media2` is null) versus the `with` branch (when Media2 call fails). Both branches call `GetMediaClient()` and execute the same Media1 logic. This is a recurring pattern across all routed operations (Phases 2–4). While it would be cleaner to extract a `fallbackToMedia1` helper, this is a style concern, not a correctness issue. The duplication is consistent and easy to follow. Deferring to a future cleanup pass is acceptable.

---

## Summary

**All checks pass.** Phase 4 correctly routes `GetCompatibleVideoEncoderConfigurations`, `GetVideoEncoderConfigurations`, and `GetVideoSourceConfigurations` through Media2 with transparent Media1 fallback. The shared `getEncoderConfigurationsViaMedia2` helper avoids code duplication between the two encoder config operations. The full-field parser reuses `ParseVideoEncoderConfigElement` from Phase 2 (DRY). Video source config parsing correctly maps all fields including bounds. Tests are thorough with field-level assertions.

**No changes needed. Proceed to Phase 5.**

---
---

# Phase 5 Code Review — GetSnapshotUri + Retire Bridge + Integration Tests (Tasks 5.1, 5.2, 5.3)

**Reviewer:** odm-rev
**Date:** 2026-04-15 12:00:00-04:00
**Scope:** Commits `65e5ca9`, `4c0dc2d`, `1cd5bf4` on `feat/media2-support` (diff from `9306552..1cd5bf4`)
**Verdict:** APPROVED

---

## 1. GetSnapshotUri — URI Parsing Pattern + FixUrl()

**PASS.** `getSnapshotUriViaMedia2` in `NvtSession.fs` correctly:

- Constructs `Media2GetSnapshotUriRequest` with `ProfileToken`
- Uses the standard raw-Message pipeline
- **Reuses `Media2XmlParser.ParseGetStreamUriResponse`** for URI extraction — confirmed at line 1161. The Media2 `GetSnapshotUri` response has the same `<tr2:Uri>` structure as `GetStreamUri`, so parser reuse is correct and DRY.
- Applies `FixUrl()` on the parsed URI — same pattern as `getStreamUriViaMedia2`
- Sets `mediaUri.uri <- null` when URI is empty — correct null handling

`GetSnapshotUri(token)` routing: Media2 first → on failure logs via `dbg.Error(err)` and falls back to Media1 with the original `FixUrl()` logic preserved identically. The Media1 fallback code is a direct copy of the pre-existing implementation. No regression risk.

---

## 2. GetVideoEncoderConfigurationsMedia2 Retired — Zero Remaining References

**PASS.** Verification:

1. **INvtSession interface (line 88):** `abstract GetVideoEncoderConfigurationsMedia2` member **removed**. The interface now has only `GetAllCapabilities` as an abstract member (confirmed by grep).
2. **NvtSession.fs implementation:** The entire 44-line `GetVideoEncoderConfigurationsMedia2()` method (the old inline parser that only extracted token+encoding) has been **deleted** and replaced with whitespace. All its functionality is now subsumed by `getEncoderConfigurationsViaMedia2` (Phase 4) which parses all fields.
3. **Caller migration:**
   - `VideoSettingsActivity.fs` line 72: `session.GetVideoEncoderConfigurationsMedia2()` → `session.GetVideoEncoderConfigurations()` ✓
   - `ProfileManagementActivity.fs` line 130: `session.GetVideoEncoderConfigurationsMedia2()` → `session.GetVideoEncoderConfigurations()` ✓
4. **Codebase grep:** Zero references to `GetVideoEncoderConfigurationsMedia2` in any `.fs` or `.cs` source file. Only references remain in documentation files (`feedback.md`, `PLAN.md`, `requirements.md`, `progress.json`, `docs/features/h265-hevc.md`) — these are historical references, not code.

**Net interface change:** One member removed (`GetVideoEncoderConfigurationsMedia2`). All 7 video operations now route transparently through the internal Media2-first layer without any public interface change. This is the correct outcome.

---

## 3. Integration Tests — Structure, Skip Logic, Coverage

**PASS.** `Media2IntegrationTests.cs` is well-structured:

- **Test category:** All 7 tests tagged `[TestCategory("Integration")]` — confirmed. Offline test runs with `TestCaseFilter:"TestCategory!=Integration"` will skip them automatically.
- **Skip mechanism:** `SkipIfNoHost()` calls `Assert.Inconclusive("ODM_TEST_HOST not set — skipping integration test")` when the environment variable is absent. This is the correct MSTest pattern — inconclusive tests are reported as skipped, not failed.
- **Session factory:** `CreateSession()` constructs a `NvtSessionFactory` with optional credentials from `ODM_TEST_USER`/`ODM_TEST_PASS`, creates a session against `http://{host}/onvif/device_service`. Clean.
- **F# async interop:** `Run<T>` helper correctly uses `FSharpAsync.RunSynchronously` to bridge F# `Async<T>` to synchronous MSTest execution.

**7 tests covering all routed operations:**

| Test | Operation | Key Assertions |
|------|-----------|---------------|
| 1 | GetProfiles | Non-null, non-empty, all tokens non-empty |
| 2 | GetStreamUri | Non-null, non-empty URI, starts with "rtsp" |
| 3 | GetVideoEncoderConfigurationOptions | Non-null result |
| 4 | GetVideoEncoderConfigurations | Non-null, non-empty, all tokens non-empty |
| 5 | GetVideoSourceConfigurations | Non-null, non-empty, tokens and sourceTokens non-empty |
| 6 | GetSnapshotUri | Gracefully handles null/empty URI; if present, validates HTTP(S) prefix |
| 7 | GetCompatibleVideoEncoderConfigurations | Non-null result (empty is acceptable) |

Tests that depend on profiles (2, 3, 6, 7) correctly check for profile availability and mark inconclusive if none found. Test 6 is particularly well-designed — it wraps the call in try/catch and marks inconclusive for cameras that don't support snapshot, while still validating the URI scheme when a snapshot URI is returned.

---

## 4. Build and Test Results

**PASS.** Per progress.json V5 entry: Release x64 build passed (warnings only, 0 errors). 80/80 unit tests passed (TestCategory!=Integration). The 7 integration tests are correctly excluded from offline runs. No regressions.

---

## 5. Full Routing Layer — Completeness Audit

**PASS.** After Phase 5, all 7 video operations specified in requirements now route through Media2 when available:

| Operation | Routing Helper | Phase Added |
|-----------|---------------|-------------|
| GetProfiles | `getProfilesViaMedia2` | Phase 2 |
| GetStreamUri | `getStreamUriViaMedia2` | Phase 2 |
| GetVideoEncoderConfigurationOptions | `getVideoEncoderConfigurationOptionsViaMedia2` | Phase 3 |
| SetVideoEncoderConfiguration | `setVideoEncoderConfigurationViaMedia2` | Phase 3 |
| GetCompatibleVideoEncoderConfigurations | `getEncoderConfigurationsViaMedia2` (shared) | Phase 4 |
| GetVideoEncoderConfigurations | `getEncoderConfigurationsViaMedia2` (shared) | Phase 4 |
| GetVideoSourceConfigurations | `getVideoSourceConfigurationsViaMedia2` | Phase 4 |
| GetSnapshotUri | `getSnapshotUriViaMedia2` | Phase 5 |

The temporary `GetVideoEncoderConfigurationsMedia2` bridge has been retired. All callers migrated. The public `INvtSession` interface is cleaner (one member removed). The routing is fully transparent — callers use the same methods as before, and Media2 vs Media1 selection is an internal concern.

---

## Summary

**All 5 checks pass.** Phase 5 completes the Media2 full routing layer. `GetSnapshotUri` correctly reuses the `ParseGetStreamUriResponse` parser and applies `FixUrl()`. The `GetVideoEncoderConfigurationsMedia2` bridge method has been cleanly retired with zero remaining source references and both callers migrated. The 7 integration tests provide good coverage of all routed operations with correct skip logic for offline runs. The build is clean and all 80 unit tests pass.

**Cumulative verdict for Phases 4+5: APPROVED.** The Media2 full routing vision from the requirements is now fully implemented. All video operations route through Media2 when available with transparent Media1 fallback. No changes needed.

**Minor observations carried forward (not blocking):**
- Phase 3: Dead `bpsRange` variable in options parser — cleanup candidate
- Phase 4: Media1 fallback code duplication across routed operations — style cleanup candidate
- Both are acceptable technical debt for a future pass.

---
---

# Sprint 4 Documentation Harvest — Review

**Reviewer:** odm-rev
**Date:** 2026-04-15 14:00:00-04:00
**Scope:** Commit `0e5e7d6` — `docs/features/media2-routing.md` and `docs/features/media2-testing.md`
**Verdict:** APPROVED

---

## 1. Durable Knowledge — Architecture Decisions, API Contracts, Design Rationale

**PASS.** Both documents capture knowledge that will outlast the sprint.

`media2-routing.md` covers:
- The detection mechanism (`GetMedia2Client()` with `Async.Memoize`)
- The routing pattern with a concrete F# code example
- A complete table of all 8 routed operations with their Media2 request types
- WCF interface design rationale (why raw `Message` instead of generated proxies)
- The two XML namespaces (`tr2`, `tt`) and their usage
- Why `INvtSession` was kept unchanged (transparent routing)
- Why the bridge method was retired (two paths to same data, partial vs full parse)
- Root cause of issue #21 (Media1 has no H265 options sub-object)

`media2-testing.md` covers:
- Test file locations and counts
- Exact commands to run unit and integration tests
- Environment variable contracts for live camera testing
- What each of the 18 tests verifies (11 unit + 7 integration)
- How to add new tests of each type, with a skeleton example

This is the kind of documentation that saves a future developer hours of code archaeology.

---

## 2. Free of Transient Content

**PASS.** No task lists, no code-line references ("line 1234"), no debug notes, no implementation steps. The phrase "retired in Phase 5" in the routing doc is the only sprint reference — it reads as historical context explaining a design evolution, not a transient task marker. Acceptable.

The test counts (11 unit, 7 integration, 80 total) will become stale as tests are added, but these are useful baseline references and easy to update. Not a blocking concern.

---

## 3. Routing Doc — Useful for a New Developer?

**PASS.** A developer joining the project could understand the entire Media2 routing layer from this document alone. The structure follows a natural learning path: what is Media2 detection → how does routing work (with code) → what operations are routed → how is WCF wired up → why were certain design decisions made. The "Why" sections (INvtSession unchanged, bridge retired, issue #21 root cause) anticipate exactly the questions a newcomer would ask.

**NOTE (minor):** The doc does not mention the error-logging behavior on fallback (`dbg.Error(err)` before falling back to Media1). This is relevant for debugging — a developer seeing Media1-quality responses from a Media2 camera would want to know where to look. Not blocking, but a one-line addition to the "Routing Pattern" section would be valuable:

> When Media2 fails, the exception is logged via `dbg.Error(err)` before falling back to Media1.

---

## 4. Testing Doc — Actionable for Running Integration Tests?

**PASS.** The testing doc is fully actionable. A developer could run integration tests against a real camera by following the doc step by step:

1. Environment variables are clearly documented with required/optional flags and defaults
2. Exact `vstest.console.exe` commands are provided for both unit-only and integration-only runs
3. The skip mechanism is explained (Assert.Inconclusive when `ODM_TEST_HOST` is unset)
4. Each integration test's expected behavior is documented in a table
5. The "Adding New Integration Tests" section includes a complete skeleton with the correct pattern (`SkipIfNoHost`, `CreateSession`, `Run(async)`)

**NOTE (minor):** The `vstest.console.exe` path is hardcoded to VS2022 Community edition. If a developer uses Professional/Enterprise or a different VS version, the path differs. A one-line note ("adjust the VS edition in the path if needed") would prevent confusion. Not blocking.

---

## 5. Anything Important Missing?

**NOTE (minor, not blocking).** Two small gaps:

1. **Fallback logging:** As noted in check 3, the routing doc doesn't mention that Media2 failures are logged before fallback. One sentence in the "Routing Pattern" section would help debugging.

2. **`Media2XmlParser` location:** The routing doc mentions `Media2XmlParser` is "in `onvif.services`" and the testing doc references it in test files, but neither doc gives the exact file path (`onvif/onvif.services/onvif.services.cs`). A developer searching for "Media2XmlParser.cs" would not find it because it lives inside `onvif.services.cs`. Adding the full path once would save a grep.

Neither gap is blocking. The documents are comprehensive and well-structured as-is.

---

## Summary

**APPROVED.** Both documentation files are high quality: they capture durable architectural knowledge, are free of transient sprint artifacts, and are immediately useful to developers who weren't on this sprint. The routing doc provides a complete mental model of the Media2 routing layer. The testing doc is fully actionable for running tests against a live camera.

**Minor suggestions (not blocking):**
- Add a one-line note about `dbg.Error` logging on Media2 fallback in the routing pattern section
- Mention the full file path for `Media2XmlParser` (`onvif/onvif.services/onvif.services.cs`)
- Add a note about VS edition in the `vstest.console.exe` path
