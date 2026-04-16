# Sprint 6 Phase 1 — Review

**Reviewer:** odm-rev
**Date:** 2026-04-16 02:17:18-0400
**Commit:** 67e943b — "feat(sprint6/phase1): apply ServicePointManager trio before SOAP probe + Media2 test harness parity"
**Verdict:** APPROVED

---

## Task 1.1 — NvtSession.fs: ServicePointManager trio before `raceEndpoints`

**PASS.**

Diff at `onvif/onvif.session/NvtSession.fs:569-573`:
```fsharp
if uris.Length = 0 then return failwith("no uri was passed")

ServicePointManager.SecurityProtocol <- SecurityProtocolType.Tls12 ||| SecurityProtocolType.Tls13
ServicePointManager.Expect100Continue <- false
ServicePointManager.ServerCertificateValidationCallback <- fun _ _ _ _ -> true

// Try original URIs first (SOAP-level probe, not just TCP)
let! httpResult = raceEndpoints uris
```

Checklist:
- **Insertion point (after the `uris.Length = 0` guard, before `raceEndpoints uris`):** Correct. The three settings are the first statements after the guard, and no other work is interleaved before the probe call.
- **`Tls12 ||| Tls13` (additive/bitwise OR, not a single-protocol replacement):** Correct. Matches the risk-register mitigation in PLAN.md that called out `Tls12` alone would replace prior flags and break a TLS 1.3-only camera. Using `|||` preserves both.
- **Callback signature `fun _ _ _ _ -> true`:** Correct F# lambda. `RemoteCertificateValidationCallback` is `(sender, cert, chain, sslErrors) -> bool`, so four discarded arguments returning `true` is the canonical always-accept form.
- **Single-URI `CreateSession(deviceUri:Uri)` overload unchanged:** Confirmed at `NvtSession.fs:662-664`. That overload sets `ServicePointManager.FindServicePoint(deviceUri).Expect100Continue <- false` per-ServicePoint as before; no code was added, removed, or reordered in that member. Minor note: the single-URI overload only sets `Expect100Continue` at the per-ServicePoint level (it does not itself set `SecurityProtocol` or the cert-validation callback), but that pre-existing condition is outside the scope of this phase — `CreateSession(Uri[])` is now the sole entry point required to apply all three.

NOTE (informational, not blocking): the global `ServerCertificateValidationCallback` assignment is a permanent process-wide cert-bypass after the probe runs. PLAN.md risk register (row 6) already documents this and defers a scoped bypass to a future sprint — accepted trade-off.

---

## Task 1.2 — Media2IntegrationTests.cs: ClassInitialize + ODM_TEST_HTTP_PORT

**PASS.**

Verified against `odm/odm.tests/HttpsIntegrationTests.cs:23-43` (the reference implementation).

Checklist:
- **`[ClassInitialize]` loads `.env` like HttpsIntegrationTests.ClassInitialize:** The `.env`-walk-up loop at `Media2IntegrationTests.cs:32-49` is byte-equivalent to the loop in `HttpsIntegrationTests.cs:27-43` — same base-directory seed, same ancestor walk, same guard on pre-existing env vars, same `#` comment skip, same 2-part split. Good parity. (HttpsIntegrationTests additionally pulls `ODM_TEST_HOST`/`USER`/`PASS`/`ODM_TEST_HTTPS_PORT` into static fields inside ClassInitialize; Media2 reads these lazily via property accessors. Both correct — just different styles.)
- **Same `ServicePointManager` trio applied:** `Media2IntegrationTests.cs:51-53` sets `SecurityProtocol`, `Expect100Continue`, and `ServerCertificateValidationCallback` — the three settings. `SecurityProtocol` is `Tls12 | Tls13` (matches the production code in Task 1.1), while HttpsIntegrationTests restricts to `Tls12` only. This divergence is appropriate: HttpsIntegrationTests targets a camera known to be TLS-1.3-incompatible, while Media2IntegrationTests should mirror production behavior (which now supports both). "Same trio" = same three settings structurally applied; values are permitted to differ per test-class intent.
- **`CreateSession()` honours `ODM_TEST_HTTP_PORT` (default `"80"`):** Confirmed at `Media2IntegrationTests.cs:63-64`:
  ```csharp
  var port = Environment.GetEnvironmentVariable("ODM_TEST_HTTP_PORT") ?? "80";
  var uri = new Uri(string.Format("http://{0}:{1}/onvif/device_service", TestHost, port));
  ```
  Matches the PLAN.md snippet verbatim. Class docstring updated to mention the new env var (line 17).

---

## Build & Offline Tests

**PASS.**

- `dotnet build odm/odm.tests/odm.tests.csproj -v quiet` → **Build succeeded, 0 errors, 2 NuGet vuln-feed warnings** (unrelated to this change — transient unreachable BluB0X NuGet feed).
- `dotnet test ... --filter 'TestCategory!=Integration' --no-build` → **80 passed, 0 failed, 0 skipped, duration 12 s.** Baseline preserved per VERIFY 1 criterion.

Integration smoke against Milesight (the third VERIFY 1 criterion — "Media2 tests progress past `CreateSession()`") was not runnable in this review environment; defer confirmation to the developer running the Milesight smoke per PLAN.md Phase 5. Code-level review alone cannot clear that checkpoint, but no evidence in the diff suggests it will fail.

---

## Done-Criteria Alignment (PLAN.md VERIFY 1)

| Criterion | Status |
|-----------|--------|
| Build succeeded, 0 errors | PASS |
| Offline test filter → all pass (baseline preserved) | PASS (80/80) |
| Media2 integration tests progress past `CreateSession()` against 192.168.1.190 | DEFERRED to developer smoke — code change is consistent with the criterion |

---

## Summary

Both Phase 1 tasks are implemented correctly per PLAN.md and requirements.md. Task 1.1 places the ServicePointManager trio at the exact insertion point specified (after the `uris.Length = 0` guard, before `raceEndpoints`), uses the additive `Tls12 ||| Tls13` form the risk register called for, and leaves the single-URI overload untouched. Task 1.2 mirrors `HttpsIntegrationTests.ClassInitialize` for `.env` loading, applies the three-setting trio, and wires `ODM_TEST_HTTP_PORT` (default `"80"`) into `CreateSession()`.

Build is clean (0 errors) and all 80 offline tests pass — baseline preserved. The only outstanding VERIFY 1 item is the live Milesight smoke, which cannot be executed from this review environment and is appropriately deferred to developer-side verification.

**Verdict: APPROVED.** Proceed to Phase 2.
