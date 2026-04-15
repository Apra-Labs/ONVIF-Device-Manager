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
