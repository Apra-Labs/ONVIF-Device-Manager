# Sprint 7 — Camera Compatibility Fixes (#25, #26, #29, #30)

## Goal

Fix four open issues using an integration-test-first approach on odm-ssdev (which has real cameras on the 10.102.10.x network). For each issue:
1. Run an integration test to confirm and understand the bug
2. Write a failing unit test that exposes the root cause
3. Fix the code until both unit and integration tests pass

All work stays on `feat/media2-support`. Reviewer: odm-rev.

---

## Phase 1 — Issue #26: UpgradeScheme Breaks HTTP Sub-Services

**Root cause (from logs):** When the device service is HTTPS, `UpgradeScheme` blindly promotes all sub-service URLs to HTTPS, using the device port (e.g. 443). But sub-services advertised on `http://<host>:80/...` by the camera are genuinely HTTP-only. Upgrading them causes connection-refused on port 443 for those sub-services.

**Fix direction:** `UpgradeScheme` should only upgrade a sub-service URL when the URL is on the same port as the device service (meaning the camera likely intends the same endpoint). Never upgrade when the port differs. Better still: if the URL came from `GetCapabilities`/`GetServices`, it is authoritative — add a `useVerbatim` path that bypasses upgrade entirely for those.

### Tasks

**1.1** [integration] Reproduce #26 with integration test
Write `SchemeUpgradeIntegrationTests.cs` with test `UpgradeScheme_HttpSubservice_IsNotUpgraded`:
- Set `ODM_TEST_HOST=10.102.10.7` (HTTP camera, fails with current code)
- Create session, call `GetCapabilities` or `GetServices`, capture sub-service URLs
- Assert all returned sub-service URLs still use HTTP scheme (not HTTPS)
- Run; confirm test fails with current code to establish the bug

**1.2** [unit] Write failing unit test for port-mismatch guard
Add to `SchemeUpgradeTests.cs`: `UpgradeScheme_SubservicePortDiffersFromDevice_NoUpgrade`
- `deviceUri = https://192.168.1.10:443/...`, `subServiceUri = http://192.168.1.10:80/...`
- Assert result equals input (no upgrade because ports differ: 80 ≠ 443)
- Also add: `UpgradeScheme_SubservicePortMatchesDevice_Upgrades`
- `deviceUri = https://192.168.1.10:8443/...`, `subServiceUri = http://192.168.1.10:8443/...`
- Assert result is upgraded to HTTPS (port matches)
- Run; confirm first test fails, second test passes

**1.3** [fix] Patch `NvtSession.fs` `UpgradeScheme`
In `NvtSessionFactory.UpgradeScheme` (around line 485), add port-matching guard:
Only upgrade when:
  `url.Port = deviceUri.Port`  (same non-standard port — upgrade makes sense)
  OR (`url.IsDefaultPort` OR `url.Port = 80`) AND `deviceUri.IsDefaultPort`
    (both on their respective defaults — the canonical 80→443 upgrade)
Any other port mismatch: return `url` unchanged.

**V1** VERIFY 1 — build Release x64, run all unit tests (offline), run integration test with `ODM_TEST_HOST=10.102.10.7`

---

## Phase 2 — Issue #30: RTSP Regression vs v2.2.252.x

**Root cause (hypothesis from logs):** Cameras that streamed fine in v2.2.252.x now hang or fail in current build. `ServicePointManager.Expect100Continue = false` and `withMedia1HttpFallback` are Sprint 6 candidates. However RTSP is not HTTP so those shouldn't affect RTSP directly — more likely `GetStreamUri` is returning an incorrectly upgraded URI (overlap with #26). Phase 2 first checks whether Phase 1 fix resolves this.

### Tasks

**2.1** [integration] Confirm RTSP regression
Add `RtspRegressionIntegrationTests.cs` with test `GetStreamUri_Camera_ReturnsUnmodifiedRtspUrl`:
- Connect to a camera with RTSP at `ODM_TEST_HOST` (try 10.102.10.97 or similar)
- Call `GetStreamUri`, assert URI scheme is `rtsp://`
- Assert URI host matches `ODM_TEST_HOST` (not been rewritten)
- Run BEFORE applying Phase 1 fix; capture exact URI returned and any failure

**2.2** [diagnose] Check if #26 fix resolves #30
After Phase 1 fix, re-run `GetStreamUri` integration test. If stream URI is now correct and RTSP works, close #30 as fixed-by-#26. If RTSP still fails:
- Add targeted logging: log full stream URI before returning from `GetStreamUri`
- Log any exception during RTSP teardown (look for it in UI activities, not NvtSession)
- Capture fresh logs with cameras connected

**2.3** [unit] Write unit test for stream URI fidelity
Add `StreamUriTests.cs`:
- `GetStreamUri_HttpCamera_ReturnsUnmodifiedRtspUri`: given session over HTTP camera returning `rtsp://10.102.10.97:554/stream1` from Media1, assert the URI is returned unchanged (not scheme-upgraded)
- This test should pass after Phase 1 fix

**2.4** [fix — if #26 fix is not sufficient]
If RTSP still fails after Phase 1:
- Investigate `withMedia1HttpFallback` — confirm it is not retrying RTSP-related SOAP calls in a way that corrupts session state
- Investigate whether `Expect100Continue = false` affects any HTTP tunnelled RTSP path
- Write targeted fix; add unit test; document root cause in issue #30

**V2** VERIFY 2 — build Release x64, run all unit tests, run RTSP integration test

---

## Phase 3 — Issue #29: Startup Race Condition (Credential Store vs Auto-Connect)

**Root cause:** ODM launches discovery and auto-connect in parallel with DPAPI credential store loading from disk. If the store hasn't finished loading (`storeCount = 0`), auto-connect uses no credentials — all cameras fail auth. Retrying after ~10 seconds works because the store has loaded by then.

### Tasks

**3.1** [diagnose] Add timing logs to credential store and auto-connect
- Find where `CredentialStore` loads from disk (constructor or `Load()`)
- Find where auto-connect / discovery-triggered connection fires (main window `Loaded` event or `DiscoveryActivity`)
- Add `log.WriteInfo` with timestamps at: store load start, store load complete, storeCount at complete, auto-connect trigger, credentialCount at trigger
- Build, launch ODM on odm-ssdev, immediately watch `logs/net.log` — measure gap between store-load-complete and auto-connect trigger

**3.2** [unit] Write failing unit test for race condition
Add `StartupRaceTests.cs`:
- `AutoConnect_BeforeStoreLoaded_DoesNotConnect`: set up `CredentialStore` via reflection so `_credentials` is empty but store file exists with content (simulating "not yet loaded")
- Call the auto-connect path; assert it defers / returns without attempting connection
- This test should fail with current code (it proceeds with storeCount=0)

**3.3** [fix] Gate auto-connect on credential store readiness
Pick the simplest option that works:
- Add `bool IsLoaded` to `CredentialStore`; set to `true` after `Load()` completes
- In auto-connect trigger, check `IsLoaded`; if false, subscribe to `Loaded` event and defer
- OR: load credential store synchronously before starting discovery (DPAPI is usually <100ms — acceptable)
- Make unit test 3.2 pass

**V3** VERIFY 3 — build Release x64, run unit tests, launch ODM 5 times: camera must connect on first try every time

---

## Phase 4 — Issue #25: TLS Probe / Milesight Compatibility

**Context:** Milesight camera at `192.168.1.190` may not be on odm-ssdev's network (10.102.10.x subnet). Handle both cases.

### Tasks

**4.1** [probe] Check network reachability
Run: `Test-NetConnection -ComputerName 192.168.1.190 -Port 80`
- If NOT reachable: set task 4.2–4.4 and V4 to `blocked` in progress.json with note "192.168.1.190 not reachable from odm-ssdev — needs testing on odm-dev". STOP Phase 4. Push and notify PM.
- If reachable: continue to 4.2

**4.2** [integration] Reproduce TLS probe failure
Write `TlsProbeIntegrationTests.cs`:
- `TlsProbe_MilesightCamera_ConnectsSuccessfully`: create session against `ODM_TEST_HOST=192.168.1.190` with Milesight credentials
- Assert session is not null; assert GetProfiles returns at least one profile
- Run; capture exact exception from logs

**4.3** [diagnose] Identify TLS probe root cause
Candidates from logs:
- `GenerateHttpsVariants` produces wrong port for Milesight
- Milesight responds slowly — SOAP probe timeouts
- TLS certificate rejection (should be bypassed globally)
- Authentication failure at probe time

**4.4** [fix] Fix TLS probe
Targeted fix in `NvtSession.fs` or `NvtSessionFactory` based on 4.3. Add unit test.

**V4** VERIFY 4 — build Release x64, run all unit tests, run TLS probe integration test against 192.168.1.190 (if reachable)

---

## Completion Criteria

All verify checkpoints pass:
- Release x64 build: clean
- Unit tests (offline): all pass, no regression against existing 91 tests
- Integration tests: all new tests pass against real cameras on 10.102.10.x
- After V4: push `feat/media2-support`, notify PM for odm-rev review
