# Sprint 6 — Milesight Camera Compatibility Fixes — Plan Review

**Reviewer:** odm-rev
**Date:** 2026-04-16 14:30:00+05:30
**Branch reviewed:** `feat/media2-support` @ `875b310`
**Verdict:** CHANGES NEEDED

---

## 1. "Done" criteria per task
**PASS** — Every task has a concrete `Done:` block. Build commands, test filters, and Milesight-specific acceptance observations are explicit. No "implement X" without a measurable outcome.

## 2. Cohesion within tasks / coupling between tasks
**PASS** — Phase 1 is harness/session bootstrap, Phase 2 is scheme mapping, Phase 3 is fallback plumbing (pure helpers), Phase 4 is wiring. Each task edits a tight file set and leaves combinators unused until the next phase — clean staging.

## 3. Shared abstractions in earliest tasks
**PASS** — `IsConnectionRefused` detector, `GetMedia{1,2}HttpXAddr`, `createMedia{2,}ClientAt` are introduced in Phase 3 before Phase 4 wires them into 8 call sites. Single-source-of-truth fix for `UpgradeScheme` vs. `UpgradeSchemeIfNeeded` (Task 2.1 bullet 2) is particularly good — eliminates pre-existing drift.

## 4. Riskiest assumption validated first
**PASS** — Task 1.1 proves TLS/Expect100Continue settings reach the SOAP probe against the real Milesight at `192.168.1.190`. This is the load-bearing assumption for everything downstream (without it, you cannot reach `GetServices()` to discover the Media2 xAddr for fallback).

## 5. DRY / reuse of early abstractions in later tasks
**PASS** — `withMedia2HttpFallback` / `withMedia1HttpFallback` both consume the Task 3.1 detector. Task 2.1 collapses two copies of `UpgradeScheme` into one. Task 4.1 call-site pattern is identical for all 8 methods.

## 6. 2–3 work tasks per phase + VERIFY checkpoint
**PASS** — Phase 1: 2 tasks + VERIFY 1. Phase 2: 3 tasks + VERIFY 2. Phase 3: 2 tasks + VERIFY 3. Phase 4: 3 tasks + VERIFY 4. Phase 5: 2 tasks + VERIFY 5. Structurally conformant.

## 7. One-session task sizing
**PASS** — All tasks are "cheap" or "standard". Task 4.1 touches 8 call sites but each is a mechanical one-line wrap; well within one session.

## 8. Dependency ordering
**PASS** — Detector (3.1) precedes combinator (4.1/4.2); `ClassInitialize` harness (1.2) precedes tests that rely on it; `UpgradeScheme` fix (2.1) precedes its test rewrite (2.2).

## 9. Unambiguous task specifications
**NOTE** — Task 4.1 enumerates call sites by line number (1600, 1630, 1650, 1748, 1765, 1881, 1959, 1998). These are fragile under rebase/insertions earlier in the file. Consider adding the matching method name for each line (e.g. `GetProfiles @ 1600`) so a later implementer can re-anchor after drift. Low-severity but easy to harden.

## 10. Hidden dependencies
**NOTE** — Task 3.1's `GetMedia1HttpXAddr` depends on `GetCapabilities()`, and `GetMedia2HttpXAddr` depends on `GetServices()`. The risk register acknowledges the `GetCapabilities` dependency; it does not acknowledge the `GetServices()` dependency for the Media2 path. If `GetServices()` itself is the call that just failed with ConnectionRefused, the memoized result may be absent — worth a one-line note.

## 11. Risk register completeness
**FAIL** — The register is substantive (7 entries) and the coverage on memoization/closure drift is strong, but **three safety-relevant risks are missing**:

### 11a. `ServerCertificateValidationCallback` is globally and permanently permissive (HIGH)
Task 1.1 sets `ServicePointManager.ServerCertificateValidationCallback <- fun _ _ _ _ -> true` — this globally disables cert validation for every HTTPS client in the .NET process for the remainder of its lifetime. The risk register mentions ServicePointManager *mutation* generally but does not call out that cert-chain validation is bypassed for cameras with valid certs as well. Mitigation the plan should reference: narrow the callback to "accept only if the current call is an ONVIF probe" (via the `SslPolicyErrors` / sender context), or document the scope explicitly in a comment at the mutation point.

### 11b. `SecurityProtocol <- Tls12` replaces rather than augments (MEDIUM)
`ServicePointManager.SecurityProtocol <- SecurityProtocolType.Tls12` clobbers whatever was previously set (including Tls13 / SystemDefault). Against a modern camera that supports only TLS 1.3, this would break connectivity mid-process. Should be `SecurityProtocol <- SecurityProtocol ||| Tls12 ||| Tls13` (or `SystemDefault`). Not acknowledged in the register.

### 11c. HTTP fallback masking a real HTTPS TLS regression (MEDIUM — contradicts risk-register claim)
Task 3.1 declares the detector matches `WebException` with **`SecureChannelFailure`** (TLS handshake failure) and `SocketException` with **`TimedOut`**. The risk register, however, states "Fallback only triggers on `WebException` with `ConnectFailure` or `SocketException` with `ConnectionRefused`/`ConnectionReset`; other exceptions propagate unchanged." These are in direct conflict. The broader taxonomy in Task 3.1 means a camera with a cert/TLS mismatch (or a transient TLS timeout) would be silently downgraded to plain HTTP — exactly the "HTTPS regression masked" case the register meant to exclude. Either:
- restrict the detector to `{ConnectFailure, ConnectionRefused, ConnectionReset}` (matching the register), or
- explicitly document that HTTPS→HTTP silent downgrade is acceptable under `SecureChannelFailure` / `TimedOut` and justify why.

## 12. Fix A vs Fix B ordering / Milesight unblocker attribution
**FAIL** — This is the central semantic finding.

For the Milesight at `192.168.1.190`, the device URI is `https://192.168.1.190:443/device_service`. Because `443` is the default HTTPS port, `deviceUri.IsDefaultPort = true`, so Task 2.1's new formula yields `httpsPort = 443` — **identical to the current hardcoded `if b.Port = 80 then b.Port <- 443`**. Fix A therefore produces exactly the same URL (`https://192.168.1.190:443/onvif/Media`) as today on this camera. The camera still RSTs that port. **Fix A alone does not unblock Milesight.** Fix B (HTTP fallback) is the actual unblocker.

The plan's structure (Phase 2 then Phases 3–4) is correctly ordered, but this non-obvious point is implicit and easy to miss. An implementer who ships Phase 2 and tests against Milesight in isolation will conclude Fix A is broken. Require:
- One sentence in the Issue #26 section stating "Fix A addresses cameras with non-default HTTPS ports (e.g. `8443`); Fix B is what unblocks the Milesight case where device is HTTPS on the default port 443 and media is HTTP on port 80."
- Matching note in the risk register entry for Fix A behavior change.

## 13. Test coverage — required artifacts
**PASS** — All four named artifacts are present in the plan:
- `ConnectionRefusedDetectorTests` — Task 3.1 (5 cases listed)
- `MediaHttpFallbackTests` — Task 4.3 (3 cases listed)
- Updated `FixUrlHttpsTests` — Task 2.2 (2 new/rewritten cases, existing coverage preserved)
- `ODM_TEST_HTTP_PORT` plumbing — Task 1.2

Gap worth noting: no test asserts that a `SecureChannelFailure` does **not** trigger fallback (or does, if the broader taxonomy is kept). Add one test that pins the decision.

---

## Summary

**Passed (10):** Done-criteria, cohesion/coupling, shared abstractions, risk-first validation, DRY, phase structure, task sizing, dependency order, test coverage, `UpgradeScheme`/closure drift elimination.

**Must change (3):**
1. **Fix A does not unblock Milesight** — add an explicit callout that Fix A is for non-default HTTPS ports and Fix B is the Milesight unblocker. (Section 12)
2. **Exception taxonomy in Task 3.1 contradicts the risk register** — reconcile: either narrow the detector to `{ConnectFailure, ConnectionRefused, ConnectionReset}` or update the risk register to acknowledge and justify HTTPS→HTTP silent downgrade under `SecureChannelFailure`/`TimedOut`. (Section 11c)
3. **Risk register must add two entries** — (a) `ServerCertificateValidationCallback` is globally permissive for the life of the process; (b) `SecurityProtocol <- Tls12` replaces rather than augments. Prefer `|||` OR with existing setting. (Section 11a, 11b)

**Should change (low):**
- Task 4.1 line-number anchors should be paired with method names (Section 9).
- Add `GetServices()` dependency note to the Media2 HttpXAddr memoization risk entry (Section 10).
- Add a test that pins the detector's behavior on `SecureChannelFailure` (Section 13).

**Deferred / out of scope:** Acknowledged Out-of-Scope list is accurate and consistent with requirements.md.

Recommend a small revision on the plan (section 12 and section 11 fixes) before implementation starts. None of the findings require restructuring the phases.
