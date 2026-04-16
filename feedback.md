# ODM Sprint 6 — Plan Review

**Reviewer:** odm-rev
**Date:** 2026-04-16 12:00:00+00:00
**Verdict:** CHANGES NEEDED

---

## Critical Finding: PLAN.md does not address Sprint 6

`PLAN.md` in the repo root is the **Sprint 3 — Auth UX Polish** plan (branch `feat/credentials-ui`, version `2.2.252.2`, three phases covering `AccountManager.LoggedOutExplicitly`, `AuthView`, `TogglePasswordBox`, `CredentialManagerView`, 39 tests). Every task in it is already marked ✅ DONE and the work is committed (see `28d3c23`, `aa51d76`, `1512f3f`).

`requirements.md` is **Sprint 6 — Milesight Camera Compatibility Fixes** (branch `feat/media2-support`, issues #25 and #26, `NvtSession.fs` TLS/Expect100Continue/cert-callback fix, `UpgradeScheme` port mapping, HTTP fallback, `HttpsIntegrationTests.cs:65` hardcoded port, `ODM_TEST_HTTP_PORT` env var). The requirements also contain the Media2 full-routing vision as context.

**There is no Sprint 6 plan to review.** The plan file in the repo is stale from a prior sprint. None of the Sprint 6 criteria below can be evaluated against it.

---

## 1. Every task has clear "done" criteria?
**FAIL** — no Sprint 6 tasks exist in the plan. Criteria below cannot be applied.

## 2. High cohesion / low coupling?
**N/A** — no Sprint 6 tasks to assess.

## 3. Riskiest assumptions front-loaded?
**FAIL** — the riskiest assumption in Sprint 6 is that a `ServicePointManager` mutation inside `CreateSession(Uri[])` is both effective on the racing HTTP/HTTPS probes and safe against concurrent sessions. This is not mentioned anywhere in a plan because there is no plan. It should be Task 1 / probe-spike.

## 4. 2–3 tasks per phase with VERIFY checkpoints?
**FAIL** — no phases defined for Sprint 6.

## 5. Each task completable in one session?
**N/A**.

## 6. Dependencies satisfied in order?
**N/A**.

## 7. Any vague tasks?
**N/A** — but when the Sprint 6 plan is written, the following are likely to be ambiguous and must be disambiguated up front:
- "Fallback to HTTP when upgraded HTTPS media call fails" — which exceptions trigger fallback (`WebException`, `EndpointNotFoundException`, `CommunicationException`, `SocketException` inner, `TimeoutException`)? On which retry attempts? Is the fallback per-operation or cached on the session after the first failure?
- "Fallback must cover … all other media service calls in NvtSession.fs" — enumerate them (minimum: `GetProfiles`, `GetStreamUri`, `GetSnapshotUri`, `GetVideoEncoderConfigurations`, `GetVideoEncoderConfigurationOptions`, `GetVideoSourceConfigurations`, `GetCompatibleVideoEncoderConfigurations`, `SetVideoEncoderConfiguration`). Two developers will otherwise ship different coverage.
- "Apply TLS/Expect100Continue settings in `Media2IntegrationTests.ClassInitialize` (a shared test helper would prevent duplication)" — leaves helper existence and location unspecified.

## 8. Hidden dependencies?
**FAIL to surface** — the plan must flag these:
- Fix A (`UpgradeScheme` port mapping) and Fix B (HTTP fallback) interact. If A is correct, B triggers less often; if A is wrong, B masks the bug. Land A first, observe integration tests, then add B only if a camera genuinely separates schemes.
- `ServicePointManager` is process-global. Any test that depends on specific TLS/Expect100Continue/cert-callback behaviour will bleed into subsequent tests unless a teardown restores defaults. Integration tests running in a shared vstest host will contaminate each other.
- `HttpsIntegrationTests.cs:65` depends on `_httpsPort` being populated before `CreateSession()` runs — verify the `ClassInitialize` order in the test fixture.
- The `ODM_TEST_HTTP_PORT` env var introduces a second env-var contract alongside `ODM_TEST_HOST` — document both in one place.

## 9. Risk register present and complete?
**FAIL** — no risk register. A Sprint 6 plan must include, at minimum:

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `ServicePointManager` mutation leaks across tests | High | Integration suite flaky | `ClassCleanup` restores defaults; or use a helper that snapshots/restores on scope |
| TLS 1.2 forced globally breaks newer cameras needing TLS 1.3 | Medium | Regression on modern cameras | Use `Tls12 \|\|\| Tls13` instead of replacing the flag |
| `ServerCertificateValidationCallback = fun _ _ _ _ -> true` accepts any cert in the whole process | High | Security regression | Scope the accept-all callback narrowly, or gate behind opt-in flag; document in threat model |
| HTTP fallback silently hides real HTTPS regressions | Medium | Hard-to-debug prod issues | Log every fallback at Warn level; emit a single "camera served media on HTTP after HTTPS rejected" event per session |
| Media2 integration tests require a live camera at `192.168.1.190` | High | Tests not runnable in CI | Skip via `[TestCategory("Integration")]` and `ODM_TEST_HOST` gate — already present, confirm it still gates all seven |
| `UpgradeScheme` with `deviceUri.IsDefaultPort` is subtle (default HTTP is 80 but default HTTPS is 443) | Low | Wrong port for HTTPS-on-custom-port cameras | Unit test at least: `https://host/…` + `http://host:80/x` → `https://host:443/x`; `https://host:8443/…` + `http://host:80/x` → `https://host:8443/x` |
| Adding HTTP fallback doubles latency for every media call on cameras that truly reject HTTP | Low | UX regression | Cache scheme choice on the session after first success |

## 10. Will all 13 integration tests pass against 192.168.1.190?
**CANNOT ASSESS** — no plan. From requirements alone the predicted outcome is plausible (the three `ServicePointManager` settings unblock the HTTP probe on port 80, `UpgradeScheme` port mapping plus HTTP fallback unblock the two `HttpsIntegrationTests` media calls), but there is no plan to review for completeness. The plan must explicitly list all 13 tests with expected pass state, and describe how each of Fix A / Fix B / Fix C contributes.

## 11. Is the `ServicePointManager` global mutation safe (thread safety / test isolation)?
**FAIL — this is the highest-risk aspect of the sprint and must be called out explicitly in the plan.**

Concerns the plan must address:
- `ServicePointManager` is process-wide static state. Mutating it inside `CreateSession(Uri[])` sets it for every subsequent HTTP client in the AppDataDomain, not just the probe.
- `ServerCertificateValidationCallback <- fun _ _ _ _ -> true` is especially dangerous: it disables cert validation for the entire process for all subsequent HTTPS, including any WCF channel opened by a different user of the library. The single-URI overload does the same today, but replicating it in the multi-URI path compounds the exposure. At minimum, document this decision; ideally, set the callback only if one is not already set, and scope it via a per-request `ServicePoint` or `HttpClientHandler` where feasible.
- Test isolation: `Media2IntegrationTests.ClassInitialize` and `HttpsIntegrationTests.ClassInitialize` run in the same vstest process. Whichever runs last wins; tests that assume defaults will fail non-deterministically. `ClassCleanup` in both classes should snapshot and restore the prior values, or the shared helper should refuse to reset if a previous fixture has already installed it.
- `SecurityProtocol <- Tls12` *replaces* the flag set; if another part of ODM needs Tls13 (or the .NET default on a newer framework target) the overwrite is a silent regression. Use `SecurityProtocolType.Tls12 ||| SecurityProtocolType.Tls13` or OR into the current value.
- Thread safety: `ServicePointManager` property setters are not atomic with respect to observers; two concurrent `CreateSession` calls in a multi-device scenario could interleave. Consider a single `Lazy<unit>` that sets them exactly once.

## 12. Does the HTTP fallback correctly handle Milesight (device HTTPS:443, media HTTP:80)?
**PARTIALLY — the requirement describes the right shape, but the plan must nail down details:**

- Fix A changes `UpgradeScheme` so the scheme-upgrade preserves the device's HTTPS port. Against Milesight this produces `https://192.168.1.190:443/onvif/Media` from an `http://192.168.1.190:80/onvif/Media` xAddr — which is exactly the URL that currently fails with connection refused. Fix A **does not fix Milesight on its own**; Fix B is required.
- Fix B (HTTP fallback on `WebException`/`SocketException`) is the actual unblocker. The plan must:
  1. Specify exceptions precisely. `CommunicationException` wraps socket/WCF faults — inspect `InnerException` chain. `EndpointNotFoundException` is a separate WCF case. TCP RST surfaces as `SocketException` with `ConnectionRefused`. HTTPS-on-HTTP-port typically surfaces as an SSL handshake failure, not connection refused — plan for both.
  2. Decide fallback scope: per-call retry, or session-level "sticky" scheme after first success. Sticky is strongly preferred — otherwise every media call pays two round-trips on Milesight.
  3. Preserve the original `UriBuilder`'s path, query, and username/password — do not just swap the scheme on a new URI.
  4. Emit a single Warn-level log line per session describing the scheme downgrade, plus the camera vendor/model if known, so ops can tell Milesight apart from a real HTTPS outage.
- Edge case: cameras that serve media only on a non-default HTTP port (e.g. `http://host:8080/onvif/Media`). The xAddr already carries the port, so fallback just needs to use the xAddr verbatim — the plan should state this explicitly.
- Edge case: xAddr scheme is `https` already. No upgrade happens, no fallback path applies — confirmed safe, but worth a unit test.

## Additional observations

- **Branch mismatch.** Current branch is `docs/sprint4-harvest` (Sprint 4 docs). Requirements state work goes on `feat/media2-support`. The plan must explicitly pick the branch — starting Sprint 6 on the docs branch is wrong.
- **Requirements.md has two sprints concatenated.** Lines 1–128 are Sprint 6; lines 129–312 are the Sprint 5 Media2 full-routing vision. The Sprint 6 plan should explicitly state that the Media2 routing work is **out of scope** for this sprint (the "Out of Scope" block on line 124 already says so; the plan should echo this), and reference the Sprint 5 content only as architectural background.
- **`PLAN.md` is stale.** It should either be replaced with a Sprint 6 plan or deleted and replaced. Leaving a completed Sprint 3 plan in the repo root while working on Sprint 6 will mislead anyone who opens it.
- **`progress.json` and `sprint3-progress.json` are both present.** If Sprint 6 uses a progress tracker, name it `sprint6-progress.json` to avoid overwriting Sprint 3's final record; Sprint 4 already set the precedent with `sprint3-*` suffixes.
- **Test count baseline.** Sprint 3 ended at 39/39 unit tests. The plan should state the new expected count after Sprint 6 (unit tests for `UpgradeScheme` port mapping and the fallback decision logic should be added — realistically +4 to +8 tests). The 13 integration tests are separate and camera-gated.

---

## Summary

**Sprint 6 cannot be reviewed because there is no Sprint 6 plan.** `PLAN.md` is the Sprint 3 auth UX plan, completed three commits ago. `requirements.md` correctly describes Sprint 6 (issues #25 and #26) plus Sprint 5 architectural context. A plan must be authored before review can proceed.

**When the plan is written, it must include:**

1. Branch: `feat/media2-support` (switch from `docs/sprint4-harvest`).
2. Task 1 — a probe-spike that validates the three `ServicePointManager` settings actually unblock the camera's HTTP:80 endpoint (this is the riskiest assumption, front-load it).
3. Task 2 — `UpgradeScheme` port mapping (Fix A) with unit tests covering default-port, non-default-port, and already-HTTPS cases.
4. Task 3 — HTTP fallback on WCF/HTTP exceptions (Fix B) with decisions on exception taxonomy, session-stickiness, and log emission.
5. Task 4 — `HttpsIntegrationTests.cs:65` hardcoded port (Fix C) and `Media2IntegrationTests` `ODM_TEST_HTTP_PORT` env var + shared TLS helper.
6. VERIFY checkpoint — 13 integration tests pass against 192.168.1.190, all pre-existing unit tests still pass, Release x64 build clean.
7. A risk register that at minimum covers `ServicePointManager` global-state contamination, cert-validation bypass scope, test isolation, and Tls12/Tls13 flag replacement.
8. Explicit acknowledgement that Media2 full routing (Sprint 5) is **out of scope** — Sprint 6 is only #25 and #26.

**Verdict: CHANGES NEEDED.** The plan must be written and re-submitted.
