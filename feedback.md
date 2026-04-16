# Sprint 6 — Milesight Camera Compatibility Fixes — Plan Re-Review

**Reviewer:** odm-rev
**Date:** 2026-04-16 15:00:00+05:30
**Branch reviewed:** `feat/media2-support` @ `3e8e7b6`
**Verdict:** APPROVED

---

## Context

Re-review of `PLAN.md` after the author addressed the 3 CHANGES NEEDED items from `dae1026`:
1. Fix A scope disclaimer (Milesight unblocker attribution)
2. Exception taxonomy alignment between Task 3.1 detector and risk register
3. Risk register entries for `ServerCertificateValidationCallback` and `SecurityProtocol` mutation

---

## 1. Fix A scope — explicit non-unblocker callout
**PASS** — PLAN.md:69 (Task 2.1) now contains:

> "For the Milesight camera (`https://192.168.1.190:443`), `deviceUri.IsDefaultPort = true` so `httpsPort = 443` — Fix A produces the same URL as today. Fix A is a correctness improvement for cameras with non-default HTTPS ports (e.g. 8443). Fix B (HTTP fallback, Phase 3-4) is the actual Milesight unblocker."

Unambiguous. An implementer shipping only Phase 2 and testing against Milesight will no longer conclude Fix A is broken. The note sits directly inside the task where the mis-attribution risk lives, which is the right location.

## 2. Exception taxonomy — detector ↔ risk register reconciled
**PASS** — Task 3.1 (PLAN.md:117-118) now enumerates exactly:
- `WebException` with `Status ∈ {ConnectFailure}`
- `SocketException` with `SocketErrorCode ∈ {ConnectionRefused; ConnectionReset}`
- `CommunicationException` whose inner matches

`SecureChannelFailure`, `HostUnreachable`, and `TimedOut` are removed. Matches the risk register entry at PLAN.md:15 verbatim. The five unit tests in the Done block (PLAN.md:124-128) are still consistent with the narrowed set. The "HTTPS→HTTP silent downgrade masking a TLS regression" scenario is now closed off.

## 3. Risk register — two new entries present
**PASS** — Two new rows at PLAN.md:21-22:

### 3a. ServerCertificateValidationCallback permanent/process-wide bypass
> "`ServerCertificateValidationCallback <- fun _ _ _ _ -> true` is a permanent, process-wide bypass — not scoped to the probe. Any subsequent WCF call in the process will accept invalid certs. | Matches existing behavior in `HttpsIntegrationTests.ClassInitialize`; production entry point is application startup before any other WCF traffic. Document a TODO to scope this narrowly (e.g. restore previous callback after probe) in a future sprint."

Acknowledges scope (process-wide), consistency with existing harness, and defers tightening to a future sprint with a concrete mitigation sketch. Acceptable.

### 3b. SecurityProtocol replacement (not additive)
> "`SecurityProtocol <- Tls12` replaces prior `SecurityProtocol` flags (not additive). If a TLS 1.3-only camera is connected mid-session after this runs, it will fail. | Use `|||` (bitwise OR): `ServicePointManager.SecurityProtocol <- SecurityProtocolType.Tls12 ||| SecurityProtocolType.Tls13` to preserve existing capabilities rather than replacing them."

Mitigation is correctly propagated into Task 1.1 (PLAN.md:31), which now sets `SecurityProtocolType.Tls12 ||| SecurityProtocolType.Tls13` rather than the replacement form. Fix ↔ mitigation alignment holds.

## 4. Carryover from previous "should change (low)" findings
**NOTE** — Partial credit:
- **Previous Section 9 (line-number anchors with method names):** RESOLVED. Task 4.1 at PLAN.md:168 now enumerates call sites as `GetProfiles (1600), GetStreamUri (1630), GetSnapshotUri (1650), GetVideoSourceConfigurations (1748), GetVideoEncoderConfigurations (1765), GetCompatibleVideoEncoderConfigurations (1881), SetVideoEncoderConfiguration (1959), GetVideoEncoderConfigurationOptions (1998)`. Re-anchoring after drift is now trivial.
- **Previous Section 10 (`GetServices()` dependency on Media2 path):** UNRESOLVED. The risk register still only names the `GetCapabilities()` dependency for the Media1 HttpXAddr path (row 6); the symmetric dependency on `GetServices()` for the Media2 HttpXAddr is not called out. Low severity — does not block approval.
- **Previous Section 13 (test pinning decision on `SecureChannelFailure`):** N/A. Now that the detector's taxonomy is narrow and explicit, the existing `FaultException → false` and `new Exception("random") → false` tests already anchor "non-listed exception → no fallback." A dedicated `SecureChannelFailure` test would be marginal; not a blocker.

---

## 5. Final pass on overall plan quality

### Phase structure
**PASS** — 5 phases with a VERIFY after each. Phase 1 bootstraps harness + session settings; Phase 2 fixes scheme mapping; Phase 3 lays fallback plumbing (unused); Phase 4 wires it; Phase 5 closes with Release build + integration. Structurally clean.

### Task ordering
**PASS** — Dependencies flow: Task 1.1 (global settings) → Task 1.2 (test harness) → Phase 2 scheme fix → Task 3.1/3.2 (detector + factories) → Task 4.1/4.2 (combinators) → Task 4.3 (tests) → Phase 5. No forward references.

### Done criteria
**PASS** — Every task has measurable, concrete `Done:` outcomes (build exit codes, specific test names, grep checks, Milesight-specific acceptance observations). No "implement X" without an exit condition.

### Test coverage
**PASS** — Required artifacts all present:
- `ConnectionRefusedDetectorTests` — Task 3.1 (5 cases)
- `MediaHttpFallbackTests` — Task 4.3 (3 cases: primary refused + fallback success, fault propagates, null xAddr propagates)
- Updated `FixUrlHttpsTests` — Task 2.2 (2 rewritten cases, existing 3 preserved)
- `ODM_TEST_HTTP_PORT` plumbing — Task 1.2
- Integration acceptance tied to 13/13 on 192.168.1.190 in VERIFY 5

### Coverage of the stated Milesight failure set
**PASS** — Plan addresses all 9 failing integration tests from the diagnosis in `requirements.md`: Phase 1 unblocks the 7 `Media2IntegrationTests` (SOAP probe); Phase 3–4 unblock the 2 `HttpsIntegrationTests` failures (`GetProfiles`, `GetStreamUri`). The 4 already-passing tests are protected by baseline checks in each VERIFY.

### Single-source-of-truth preserved
**PASS** — Task 2.1 bullet 2 still rewrites `UpgradeSchemeIfNeeded` to delegate to the static `UpgradeScheme`, eliminating a pre-existing drift hazard. Good hygiene.

---

## Summary

**Passed (all critical):**
- Fix A scope note explicitly deflates the Milesight unblocker expectation (Section 1).
- Detector taxonomy is narrowed and matches the risk register (Section 2).
- Risk register gains the two safety-relevant entries; Task 1.1 carries the `|||` mitigation through (Section 3).
- Task 4.1 call-site enumeration now includes method names (Section 4).
- Overall plan quality: phases, ordering, done criteria, and test coverage hold (Section 5).

**Not blocking:**
- `GetServices()` dependency for the Media2 HttpXAddr memoization is still not called out in the risk register (Section 4). Author may want to add one line; not a merge blocker.

**Deferred:**
- Scoped cert-validation callback (acknowledged as future-sprint TODO in risk register row 8).
- Out-of-Scope list (issues #20, #19, #14, Media2 audio/PTZ/analytics) remains accurate.

**Recommendation:** APPROVED to proceed with implementation. Phase 1 (TLS/Expect settings) is correctly positioned as the load-bearing first task — failing there halts the sprint before any further plumbing work is wasted. All three CHANGES NEEDED items from the prior review are cleanly resolved.
