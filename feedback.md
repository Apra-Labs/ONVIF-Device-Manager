# HTTPS/TLS/RTSPS — Phase 1 Code Review

**Reviewer:** odm-rev
**Date:** 2026-04-08
**Verdict:** APPROVED

## Findings

### TASK-1 (commit 2894ce4): Scheme-upgrade fallback — PASS

**`GenerateHttpsVariants` helper:**
- Correctly handles single URI, multi-URI, and already-HTTPS URIs (passthrough).
- Default port 80 maps to 443 + 8443; non-standard ports keep their port number + 8443; port 8443 deduplicates to a single variant.
- `Seq.distinct` prevents duplicate HTTPS URIs when input contains overlapping endpoints.
- Path is preserved correctly via `UriBuilder` (copies full URI including `/onvif/device_service`).
- Method is `static member` (public), accessible for unit testing. Good.

**HTTP-then-HTTPS sequential fallback:**
- `raceEndpoints uris` (HTTP) runs first. Only on `None` result does `raceEndpoints httpsUris` (HTTPS) execute. Correctly sequential — HTTP race is fully exhausted before HTTPS is attempted. PASS.
- Multi-endpoint race uses existing `Async.Race` pattern. Single-endpoint path avoids unnecessary race overhead. Good.

**Timeout and cancellation:**
- NOTE: Plan specified 3s per-attempt timeouts and `Async.StartChild` with `CancellationToken`. Implementation uses existing 2s socket-level timeouts (SendTimeout/ReceiveTimeout = 2000ms) without `Async.StartChild`. This is a minor deviation from plan — functionally acceptable since the 2s timeouts are the pre-existing behavior and provide adequate deadlock protection. However, there is no explicit cancellation of losing race competitors. Consider adding `Async.StartChild` cancellation in a future pass to prevent socket handle leakage under heavy multi-URI scenarios.

**HTTP camera regression risk:**
- Original HTTP URIs are tried first. If HTTP connectivity succeeds, HTTPS fallback is never triggered. No regression for HTTP-only cameras. PASS.

**Error messages:**
- Clear, distinct error messages for: empty URI array, all-HTTP-failed, and all-HTTPS-failed cases. Good diagnostics. PASS.

**Logging:**
- Logs at fallback decision point: "HTTP connectivity failed for N URIs, retrying with HTTPS variants". PASS.

### TASK-3 (commit bb4e831): Test project references — PASS

- References `onvif.session.dll` from `$(OdmViewsBin)` (Release output of `odm.ui.views`). Path is correct and consistent with other references in the project.
- No circular dependency: `odm.tests` depends on `onvif.session`, not the reverse.
- Includes all necessary transitive dependencies: `FSharp.Core`, `utils.fsharp`, `utils.diagnostics`, `utils.common`, `onvif.services`.
- `System.ServiceModel` reference present for WCF binding types used in tests.
- `CopyLocalLockFileAssemblies=true` ensures test runner finds all DLLs. PASS.

### TASK-2 (commit 5ccec4d): HTTPS binding tests — PASS

**`UnsecureFactory_WithTls_CreatesHttpsBinding`:**
- Uses reflection to access private `CreateChannelFactory` method — correct approach since the method is private static.
- Verifies binding contains `HttpsTransportBindingElement` (not just "no exception thrown"). Good.
- Checks `RequireClientCertificate == false`. Good.

**`UnsecureFactory_WithoutTls_CreatesHttpBinding`:**
- Verifies positive case (`HttpTransportBindingElement` present) AND negative case (`HttpsTransportBindingElement` absent). Thorough. PASS.

**Build and test verification:**
- `dotnet build odm/odm.tests/odm.tests.csproj` succeeds with 0 errors.
- `dotnet vstest odm.tests.dll` — 2/2 tests pass, 0 failures.

## Notes

1. **Minor plan deviation (non-blocking):** Plan called for 3s timeouts and `Async.StartChild` cancellation. Implementation uses pre-existing 2s socket timeouts without explicit child cancellation. Acceptable for Phase 1 — recommend addressing in a later phase if multi-URI HTTPS fallback sees production use with many endpoints.

2. **`GenerateHttpsVariants` is a static member, not a module-level `let` binding.** Plan specified a module-level function; implementation chose a static member on `NvtSessionFactory`. This is arguably better — keeps the method co-located with its consumer and avoids polluting the module namespace. PASS.

3. **`raceEndpoints` refactor is clean.** The original `CreateSession(uris)` was a single monolithic block with duplicated endpoint logic. The refactored version extracts `findUriForEndpoint` and `raceEndpoints` as local helpers, reducing duplication and making the HTTP/HTTPS two-phase flow readable. Good improvement.

## Summary

All three Phase 1 tasks are correctly implemented. The scheme-upgrade fallback is sequential (HTTP exhausted before HTTPS), preserves URI paths, handles edge cases (already-HTTPS, non-standard ports, deduplication), and does not regress HTTP-only cameras. Tests verify binding types via reflection with both positive and negative assertions. Test project references are correct with no circular dependencies. Build and all tests pass.

One minor plan deviation (2s vs 3s timeouts, no `Async.StartChild` cancellation) is non-blocking and acceptable for Phase 1.

**Verdict: APPROVED** — Phase 2 may proceed.

---

# Prior Plan Reviews

# HTTPS/TLS/RTSPS Support — Plan Review (Round 2)

**Reviewer:** odm-rev
**Date:** 2026-04-08
**Verdict:** APPROVED

> Round 2 re-review. See below for original review and doer responses.

## Round 2 Findings

### 1. Task ordering in Phase 1 (Task 3 before Task 2) — PASS
Phase 1 now lists Task 1 → Task 3 → Task 2. Task 2 explicitly declares Task 3 as a blocker. The summary table reflects the corrected order. Dependency is clear and correct.

### 2. Task 1 control flow — PASS
Task 1 now specifies a 5-step control flow:
- Step 1: extractable `generateHttpsVariants` helper as a module-level `let` binding, publicly accessible for unit testing.
- Step 2: sequential HTTP-then-HTTPS race for multi-URI input, 3s per-attempt timeout. HTTPS variants only after HTTP race is fully exhausted.
- Step 3: single-URI input follows the same sequential logic (HTTP → 443 → 8443).
- Step 4: `Async.StartChild` with `CancellationToken` / `Async.WithCancellation` for deadlock mitigation; cancel outstanding children on first success.
- Step 5: logging at fallback decision point.

Two developers reading this spec would produce functionally equivalent implementations. No ambiguity remains.

### 3. Task 6 trigger condition — PASS
Task 6 is rewritten with a clean dual-transport strategy: for HTTPS devices, always request both standard RTSP and RtspOverHttp transports, return both URIs. The session layer explicitly does NOT perform TCP probing — that responsibility is left to the player/caller. The "unreachable" language that conflated SOAP-level and network-level failures is gone.

### 4. Task 6 done criterion — PASS
Done criterion now requires: non-null URI with valid scheme (`rtsp://`, `rtsps://`, `http://`, `https://`), logged transport type, and offline coverage via `StreamTransportNegotiationTests`. No "playable" language remains. Testable without a live camera.

### 5. Task 9 error path verification + offline test — PASS
Task 9 now includes a pre-step: read `LiveVideoView.xaml.cs` to confirm `Error(string)` exists; create it if missing. Done criterion includes offline unit test `RtspsUriTests.SchemeDetection_Rtsps_ReturnsErrorState`. Both the WPF concern and headless testability are addressed.

### 6. Task 1 deadlock mitigation — PASS
Step 4 explicitly specifies `Async.StartChild` with `CancellationToken` (or `Async.WithCancellation`), cancellation of outstanding children on first success, and the requirement that no hanging async tasks remain. This is a concrete, implementable mitigation.

## Summary

All 4 blocking issues and 3 recommended fixes from Round 1 have been resolved. The plan is now unambiguous, correctly ordered, and has testable done criteria for every task. No residual issues found.

**Verdict: APPROVED** — implementation may proceed.

---

# Original Review (Round 1)

# HTTPS/TLS/RTSPS Support — Plan Review

**Reviewer:** odm-rev
**Date:** 2026-04-08
**Verdict:** CHANGES NEEDED

> See git history of this file for review context.

---

## 1. Done Criteria Clarity

Most tasks have clear, testable "done when" criteria. **PASS** for Tasks 1-5, 7-8, 10-12.

**FAIL — Task 6 (Transport negotiation):** The done criterion says *"GetStreamUri returns a playable URI."* But "playable" is never defined in a headless context — the test scaffold (Task 4) does `GetStreamUri()` but doesn't validate the URI is actually playable without a Live555 instance. The criterion should be: *"GetStreamUri returns a non-null URI whose scheme is `rtsp://`, `rtsps://`, or `http[s]://` and whose host:port is TCP-reachable."*

**FAIL — Task 9 (RTSPS detection):** Done criterion says *"rtsps:// URI produces a visible error message in the video panel."* This is a WPF-dependent visual outcome that the plan itself acknowledges cannot be integration-tested headlessly. The criterion must include a testable offline component — e.g., a unit test that verifies the scheme-detection helper returns the correct error state.

---

## 2. Cohesion and Coupling

Tasks are generally well-scoped. Each phase groups related work.

**NOTE:** Tasks 1 and 3 are tightly coupled — Task 1 (scheme-upgrade logic) and Task 3 (test project references) must both land before Task 2's test can run. The plan lists Task 2 before Task 3, but Task 2's unit test requires the reference from Task 3. **Reorder: Task 3 should precede Task 2 within Phase 1, or Task 2 should state that the test file is created but not built until Task 3 completes.** FAIL — dependency ordering within Phase 1 is incorrect as written.

---

## 3. Key Abstractions in Earliest Tasks

**PASS.** The scheme-upgrade fallback (Task 1) and the HTTPS binding verification (Task 2) form the foundation. All later tasks (transport negotiation, FixUrl, RTSPS) build on the assumption that the session can connect over HTTPS. Correct ordering.

---

## 4. Riskiest Assumption First

**PASS.** Task 1 directly addresses the riskiest scenario: HTTP XAddr on an HTTPS-only camera. The risk register in requirements.md identifies this as "High likelihood / F1 blocked" and the plan places it first. Good alignment.

---

## 5. DRY — Later Tasks Reuse Early Abstractions

**PASS.** Task 5 (SchemeUpgradeTests) explicitly tests logic extracted from Task 1. Task 7 (FixUrl) extends scheme awareness established in Task 1. Task 8 tests Task 7. The chain is clean.

**NOTE:** The plan says Task 5 may need *"to extract a static helper method"* from Task 1 for testability. This extraction should be specified in Task 1 itself, not deferred to Task 5 as a blocker. If Task 1 doesn't extract the method, Task 5 is blocked by a design decision that wasn't made.

---

## 6. Phase Structure (2-3 tasks + VERIFY)

| Phase | Work Tasks | VERIFY | Structure |
|-------|-----------|--------|-----------|
| 1     | 3 tasks   | Yes    | PASS      |
| 2     | 2 tasks   | Yes    | PASS      |
| 3     | 3 tasks   | Yes    | PASS      |
| 4     | 2 tasks   | Yes    | PASS      |
| 5     | 2 tasks   | Yes    | PASS      |

**PASS.** Every phase has 2-3 work tasks followed by a VERIFY checkpoint.

---

## 7. Session Completability

**PASS** for most tasks. Tasks 1, 6, and 9 are marked "premium" tier which is appropriate — they involve non-trivial F# async work or WPF error-path changes.

**NOTE:** Task 1's blocker explicitly calls out `Async.Race` deadlock risk. The mitigation is not specified — the plan should state the fallback strategy (e.g., sequential retry after race failure, with a timeout per attempt).

---

## 8. Dependency Ordering

**FAIL — Task 3 before Task 2.** As noted above, Task 2 requires the test project reference from Task 3. The plan has them in the wrong order.

All other dependencies are correctly ordered:
- Task 3 before Tasks 4-5 (test project refs needed)
- Task 1 before Tasks 4-6 (scheme-upgrade must exist)
- Task 7 before Tasks 8, 10 (FixUrl changes needed for tests)
- Task 9 before Task 10 (scheme detection helper needed)

**PASS** for inter-phase dependencies, **FAIL** for intra-Phase-1 ordering.

---

## 9. Ambiguity Check — Task 1 (Scheme-Upgrade in CreateSession)

**FAIL — Underspecified.** The plan says:

> *"after all TCP connectivity checks fail on the original URIs, generate HTTPS variants (replace http:// with https://, default port 443) and retry connectivity"*

Two developers would implement this differently because:

1. **Where in the Async.Race flow?** The current code races all URIs in parallel. Should the HTTPS fallback be a second race after the first fails? Or should each URI's individual connectivity check include an HTTPS retry? The plan doesn't say.

2. **Port strategy is ambiguous.** "Default port 443" for the multi-URI case, but "443, then 8443" for the single-URI case. Why the difference? Is 8443 tried for multi-URI too?

3. **No timeout specified.** How long to wait before declaring HTTP "failed" and trying HTTPS? The existing `Async.Race` presumably has a timeout — should the HTTPS retry use the same timeout or a separate one?

**Fix needed:** Specify the exact control flow — sequential retry after race timeout, per-attempt timeout values, and port list for both single and multi-URI cases.

---

## 10. Ambiguity Check — Task 6 (Transport Negotiation)

**FAIL — Trigger condition undefined.** The plan says:

> *"if the returned URI is unreachable or the device is HTTPS-only, retry with RtspOverHttp"*

The ONVIF `GetStreamUri` call always succeeds at the SOAP level — it returns a URI string. The camera doesn't refuse; it answers with whatever URI it has. The plan conflates "GetStreamUri fails" (SOAP error) with "returned URI is unreachable" (network-level check).

To know the returned RTSP URI is "unreachable," you'd need to attempt a TCP connect to the RTSP port — but that's a side-effect in the session layer. The plan doesn't specify:

- **Who checks reachability?** The session layer (`NvtSession`) or the caller?
- **When?** At `GetStreamUri` time or at player-launch time?
- **What constitutes "unreachable"?** TCP connect refused? Timeout after N ms?

**Fix needed:** Either (a) define a `TcpProbe(uri, timeoutMs)` helper that GetStreamUri calls before returning, or (b) change the strategy to always request both transports from the camera and let the caller pick, or (c) move the fallback to VideoPlayerActivity where player failure is observable.

---

## 11. Ambiguity Check — Task 9 (RTSPS / Live555)

**NOTE — Acceptable for now, but fragile.** The plan says:

> *"assume [Live555] was NOT [compiled with OpenSSL] for safety; provide a clear in-panel error"*

Requirements.md says: *"If the current player binary/library supports it: pass through and verify. If not: show a clear in-panel error."*

The plan hardcodes the assumption that Live555 lacks RTSPS support, skipping the probe. This is acceptable as a v1 approach since probing Live555 at runtime adds complexity, and the error message suggests an alternative ("try enabling RTSP-over-HTTPS"). However:

- **The error display path may not work.** Code inspection shows `LiveVideoView.xaml.cs` line 152 calls `Error(err)` but this method's definition is unclear — it may be inherited from `BasePropertyControl` or undefined. The plan should verify this error path exists before relying on it.
- **Future iteration** should probe Live555's TLS capability (e.g., attempt connection, check for OpenSSL symbols) rather than hardcoding the assumption.

---

## 12. Phase Ordering — Tests Before Code?

**PASS with NOTE.** The plan puts F1 code (Phase 1) before integration tests (Phase 2). This is correct because:

- Phase 1 includes Task 2 (unit test for HTTPS binding) and Task 3 (test project setup), so the test infrastructure is scaffolded in Phase 1.
- Phase 2 adds integration tests that exercise Phase 1's code.
- Offline unit tests (Task 5) are in Phase 2, not Phase 1 — this is fine since Task 1 already has a "done when" criterion that validates the logic.

An alternative (test-first) would put Task 3 and the test scaffold in Phase 1 and the F1 code in Phase 2. The current ordering is reasonable given that Task 1's done criterion is integration-verifiable.

---

## 13. Task 3 — Circular Dependency Risk

**PASS.** Code inspection confirms `onvif.session.dll` does not depend on `odm.ui.views.dll`. The test project (`odm.tests`) already references `odm.ui.views.dll` — adding `onvif.session.dll` does not create a cycle. The plan's concern is valid but the risk is low. Build paths should use the Release output directory (e.g., `onvif/onvif.session/bin/Release/`), which the plan correctly identifies as needing verification.

---

## 14. Risk Register Coverage

**PASS.** The risk register in requirements.md lists 6 risks. The plan addresses each:

| Risk | Plan Coverage |
|------|--------------|
| HTTP XAddr scheme mismatch | Task 1 (first) |
| Unsecured time-sync HTTPS failure | Task 2 |
| Live555 lacks RTSPS | Task 9 (graceful error) |
| RTSP-over-HTTP port mismatch | Task 7 (FixUrl) |
| Regression on HTTP cameras | Task 11 + VERIFY at each phase |
| Camera not configured | Integration tests skip via env var |

---

## 15. Requirements Alignment

**PASS.** The plan covers all three features (F1, F2, F3) in priority order. Out-of-scope items are respected. Constraints (no SOAP stub changes, headless tests, Release|x64 build) are checked at every VERIFY. The plan adds logging (Task 12) which requirements.md doesn't explicitly call for but which supports debuggability — acceptable.

---

## Summary

**Verdict: CHANGES NEEDED** — 4 issues must be resolved before implementation begins.

### Must Fix (blocking)

1. **Task ordering in Phase 1:** Move Task 3 (test project references) before Task 2 (HTTPS binding test), or explicitly state Task 2's test file is created but not compiled until Task 3 completes.
   **Doer:** fixed — Phase 1 reordered to Task 1 → Task 3 → Task 2. Task 2 now lists Task 3 as explicit blocker.

2. **Task 1 underspecified:** Add exact control flow for HTTPS fallback — sequential retry after race timeout, per-attempt timeout values, port list for both single and multi-URI cases, and require extraction of a static `GenerateHttpsVariants` helper for testability.
   **Doer:** fixed — Task 1 rewritten with 5-step control flow: extractable `generateHttpsVariants` helper, sequential HTTP-then-HTTPS race, 3s per-attempt timeout, port 443+8443 for all cases, `Async.StartChild` + `CancellationToken` deadlock mitigation.

3. **Task 6 trigger condition undefined:** Specify who performs reachability checks on the returned RTSP URI, when, and with what timeout. Current description conflates SOAP-level failure with network-level unreachability.
   **Doer:** fixed — Task 6 rewritten with dual-transport strategy: always request both transports for HTTPS devices, return both URIs (primary=RtspOverHttp, fallback=standard RTSP). Session layer does NOT do TCP probing. All "unreachable" language removed.

4. **Task 6 done criterion:** Replace "playable URI" with a testable definition (e.g., non-null URI with valid scheme and TCP-reachable host:port).
   **Doer:** fixed — Done criterion now requires non-null URI with valid scheme (`rtsp://`, `rtsps://`, `http://`, `https://`) + logged transport type. No "playable" language. References `StreamTransportNegotiationTests` for offline coverage.

### Should Fix (non-blocking but recommended)

5. **Task 9 error display path:** Verify that `LiveVideoView.Error()` method exists and is callable before relying on it for the RTSPS error message.
   **Doer:** fixed — Task 9 now includes pre-step: "read `LiveVideoView.xaml.cs` to confirm `Error(string)` or equivalent exists. If not, create it as a simple label overlay on the video panel."

6. **Task 9 done criterion:** Add an offline-testable component (scheme detection unit test) since the WPF visual outcome cannot be tested headlessly.
   **Doer:** fixed — Task 9 done criterion now includes: "AND unit test `RtspsUriTests.SchemeDetection_Rtsps_ReturnsErrorState` passes offline (no WPF required)."

7. **Task 1 blocker mitigation:** Specify the `Async.Race` deadlock mitigation strategy (e.g., `Async.StartChild` with cancellation token, or sequential fallback with per-URI timeout).
   **Doer:** fixed — Task 1 step 4 now explicitly specifies `Async.StartChild` with `CancellationToken` / `Async.WithCancellation`, cancel outstanding children on first success, no hanging tasks.

### Passed

- Phase structure (2-3 tasks + VERIFY per phase)
- Risk-first ordering (riskiest assumption in Task 1)
- Abstraction layering (foundations first, consumers later)
- DRY (later tasks reuse early extractions)
- Dependency ordering (inter-phase)
- Risk register fully addressed
- Requirements alignment complete
- No circular dependency risk in test project
- Session completability reasonable
