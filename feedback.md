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
