# Sprint 6 Phase 2 — Review

**Reviewer:** odm-rev
**Date:** 2026-04-16
**Verdict:** APPROVED

---

## Scope under review

Phase 2 commits on `feat/media2-support`:

- `d0ac716` feat(sprint6/phase2/task2.1): UpgradeScheme honours device HTTPS port; delegate closure
- `6e58eec` feat(sprint6/phase2/task2.2): update FixUrlHttpsTests for new port-mapping contract
- `e10a01e` feat(sprint6/phase2/task2.3): remove hardcoded :443 from HttpsIntegrationTests
- `c48a25a` chore: mark Phase 2 tasks done in progress.json (VERIFY 2 passed)

Files touched (excluding progress.json):

- `onvif/onvif.session/NvtSession.fs`
- `odm/odm.tests/FixUrlHttpsTests.cs`
- `odm/odm.tests/HttpsIntegrationTests.cs`

---

## Task 2.1 — UpgradeScheme + UpgradeSchemeIfNeeded

**PASS.**

`NvtSession.fs:478-486` — `UpgradeScheme` static now derives the HTTPS port
from `deviceUri` exactly as specified:

```fsharp
let httpsPort = if deviceUri.IsDefaultPort then 443 else deviceUri.Port
b.Port <- httpsPort
```

The prior hard-coded `if b.Port = 80 then b.Port <- 443` is gone, so a device
reached via `https://host:8443` no longer silently collapses probe URLs back
to 443. Milesight-at-443 behavior is preserved because `IsDefaultPort` is
true for an implicit `https://host` URI.

`NvtSession.fs:776` — `UpgradeSchemeIfNeeded` is a true one-liner:

```fsharp
let UpgradeSchemeIfNeeded (url: Uri) = NvtSessionFactory.UpgradeScheme deviceUri url
```

No duplicated logic. Single source of truth. Drift risk eliminated.

---

## Task 2.2 — FixUrlHttpsTests

**PASS.**

All six tests in `FixUrlHttpsTests.cs` pass (the original five, plus the new
non-default-port test — the `NonStandardHttpPort` test was rewritten into
`UpgradeScheme_HttpUrl_MapsToDeviceHttpsPort` to reflect the corrected
contract that the input URL's port is ignored when the device URI is
authoritative).

Key coverage:

- `UpgradeScheme_HttpUrl_Port80_WhenSessionIsHttps_ReturnsHttpsPort443`
  (FixUrlHttpsTests.cs:44-55) — default-port path: device `https://host` +
  input `http://host:80` -> port 443. Passes.
- `UpgradeScheme_HttpUrl_WithNonDefaultHttpsDevicePort_MapsToDevicePort`
  (FixUrlHttpsTests.cs:94-109) — non-default-port path: device
  `https://host:8443` + input `http://host:80` -> port 8443. Passes.
- `UpgradeScheme_HttpUrl_MapsToDeviceHttpsPort` (FixUrlHttpsTests.cs:77-92)
  — clarifies that result port derives from device, not input (input
  `:8080` + device `https://host` -> `:443`). Passes.

Test class XML doc comment (FixUrlHttpsTests.cs:7-19) was updated in step
with the new contract.

**NOTE:** The task brief says "All 5 tests pass"; there are actually 6
after the rewrite. Not a defect — the brief was a minor undercount.

---

## Task 2.3 — HttpsIntegrationTests hardcode removed

**PASS.**

`grep -n "https://.*:443" odm/odm.tests/HttpsIntegrationTests.cs` returns
zero matches.

The `ServicePointManager.FindServicePoint` call at
`HttpsIntegrationTests.cs:64-65` now composes the URL from `_host` and
`_httpsPort`:

```csharp
var sp = ServicePointManager.FindServicePoint(
    new Uri(string.Format("https://{0}:{1}/onvif/device_service", _host, _httpsPort)));
```

`_httpsPort` is parsed from `ODM_TEST_HTTPS_PORT` (default 443) in
`ClassInitialize` (HttpsIntegrationTests.cs:51-52), so the pre-warmed
ServicePoint now matches the device URI used for the actual session
(HttpsIntegrationTests.cs:72-74), which is what this call exists to
configure.

---

## Build and tests

- `dotnet build odm/odm.tests/odm.tests.csproj -v quiet` — **Build succeeded**,
  0 errors, 2 unrelated `NU1900` package-feed warnings.
- `vstest.console.exe odm.tests.dll --TestCaseFilter:"TestCategory!=Integration"`
  — **81 passed / 0 failed / 0 skipped**, total 13.8 s. All six
  `FixUrlHttpsTests` methods pass, including the new
  `UpgradeScheme_HttpUrl_WithNonDefaultHttpsDevicePort_MapsToDevicePort`.

---

## Minor observation (non-blocking)

`NvtSession.fs:475-477` — the XML doc comment on the `UpgradeScheme` static
is now stale:

```fsharp
/// Upgrades an HTTP URL to HTTPS when the session's device was reached via HTTPS.
/// Maps port 80 -> 443; keeps other non-standard ports unchanged.
/// Publicly accessible for unit testing (mirrors the private UpgradeSchemeIfNeeded closure).
```

Two drifts vs. the new implementation:

1. "Maps port 80 -> 443; keeps other non-standard ports unchanged" — the
   function no longer maps 80->443 at all; it substitutes `deviceUri`'s
   port (defaulting to 443).
2. "mirrors the private UpgradeSchemeIfNeeded closure" — the relationship
   is now inverted: the closure delegates to this static.

Refreshing this docblock in a follow-up is strictly tidy-up and not a gate
on approval.

---

## Summary

Phase 2 delivers exactly the port-mapping correctness fix and
deduplication specified. Implementation is clean: the port is now derived
from the authoritative `deviceUri`, and the closure no longer carries a
parallel copy that could drift. Tests assert both the default-port and
non-default-port paths, and the `:443` hardcode in the HTTPS integration
test is gone. Build clean, 81/81 offline tests pass.

**Verdict: APPROVED.**

Suggested follow-up (non-blocking): refresh the XML doc comment on
`UpgradeScheme` in `NvtSession.fs` to match the new behavior.
