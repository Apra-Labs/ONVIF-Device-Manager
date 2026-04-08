# ODM — HTTPS/TLS/RTSPS Support — Implementation Plan

> Enable ODM to work seamlessly with HTTPS-only cameras by fixing the session binding layer, adding scheme-upgrade fallback, and supporting RTSP-over-HTTPS and RTSPS stream URIs. All changes must preserve backward compatibility with existing HTTP cameras.

## Code Findings (Phase 0 Exploration)

**Already correct — no changes needed:**
- `CreateSession(deviceUri)` (NvtSession.fs:653) already detects `Uri.UriSchemeHttps` and passes `useTls=true` to `getDeviceUnsecureFactory` → `CreateChannelFactory` builds `HttpsTransportBindingElement`. The unsecured time-sync call will use HTTPS when the device URI is `https://`.
- `App.xaml.cs:74-76` — `ServerCertificateValidationCallback` returns `true` unconditionally for self-signed certs. Preserve as-is.
- `factory_wrapper` (NvtSession.fs:305-312) memoizes separate HTTP and HTTPS factory instances. No change needed.

**Must fix:**
- `CreateSession(uris:Uri[])` (NvtSession.fs:468-575) — When WS-Discovery returns HTTP XAddrs and the camera has HTTP disabled, all TCP connectivity checks fail. No fallback to HTTPS scheme. This is the primary blocker.
- `FixUrl` (NvtSession.fs:754-779) — No awareness of `rtsps://` scheme; no port fixup for RTSP-over-HTTP tunneling.
- `GetStreamUri` (NvtSession.fs:1463-1470) — Passes through whatever `StreamSetup` the UI provides. No automatic transport negotiation for HTTPS devices.
- Player layer (`libapi.cpp`) — URI passed directly to Live555 without scheme validation. `rtsps://` will likely fail silently.

## Tasks

### Phase 1: F1 Core — Scheme-Upgrade Fallback + HTTPS Unsecured Factory

#### Task 1: Scheme-upgrade fallback in `CreateSession(uris:Uri[])`
- **Change:** In `NvtSession.fs:CreateSession(uris:Uri[])`, after all TCP connectivity checks fail on the original URIs, generate HTTPS variants (replace `http://` with `https://`, default port 443) and retry connectivity. If a single HTTP URI is provided and TCP connect fails, automatically attempt HTTPS on port 443, then 8443. Log each fallback attempt.
- **Files:** `onvif/onvif.session/NvtSession.fs` (lines 468-575)
- **Tier:** premium
- **Done when:** `CreateSession([| new Uri("http://192.168.1.190/onvif/device_service") |])` on an HTTPS-only camera succeeds by falling back to HTTPS. Log output shows `"HTTP connectivity failed, retrying with HTTPS on port 443"`.
- **Blockers:** F# async control flow in `Async.Race` — must handle the case where all original endpoints fail and then retry HTTPS endpoints without deadlocking.

#### Task 2: Verify HTTPS unsecured factory path (confirm or fix)
- **Change:** Confirm that `getDeviceUnsecureFactory(useTls=true)` at line 314 produces a working HTTPS binding for the time-sync call. If the camera's device URI is `https://`, the `GetSystemDateAndTime` call at line 665 must succeed over HTTPS without WS-Security. Write a focused unit test `HttpsBindingTests` that verifies `CreateChannelFactory<Device>(false, false, false, true)` produces an `HttpsTransportBindingElement`.
- **Files:** `onvif/onvif.session/NvtSession.fs` (lines 314-318, 646-694), `odm/odm.tests/HttpsBindingTests.cs` (new)
- **Tier:** standard
- **Done when:** Unit test `HttpsBindingTests.UnsecureFactory_WithTls_CreatesHttpsBinding` passes. If the existing code is correct (it appears to be), this task is verification + test only.
- **Blockers:** Test project must reference `onvif.session` assembly.

#### Task 3: Add `onvif.session` reference to test project
- **Change:** Update `odm.tests.csproj` to reference the pre-built `onvif.session.dll` and its transitive dependencies (`onvif.services.dll`, `FSharp.Core.dll`, etc.) from the Release output directory so that integration and unit tests can instantiate `NvtSessionFactory` directly.
- **Files:** `odm/odm.tests/odm.tests.csproj`
- **Tier:** cheap
- **Done when:** `dotnet build odm/odm.tests/odm.tests.csproj` succeeds with the new references.
- **Blockers:** Must identify the exact output path for onvif.session Release build.

#### VERIFY: Phase 1
- Build: MSBuild `Release|x64` — must succeed
- Tests: existing `odm.tests` suite (39 AuthFlowTests) — must be green
- New: `HttpsBindingTests` — must pass
- Stop and report

---

### Phase 2: Integration Test Harness

#### Task 4: Create `HttpsIntegrationTests.cs` scaffold
- **Change:** Create `odm/odm.tests/HttpsIntegrationTests.cs` with `[TestCategory("Integration")]`. Tests are skipped when `ODM_TEST_HOST` env var is not set. Set `ServicePointManager.ServerCertificateValidationCallback` in `[ClassInitialize]` (mirrors `App.xaml.cs:74` for headless context). Tests: `Connect()`, `GetCapabilities()`, `GetProfiles()`, `GetStreamUri()`.
- **Files:** `odm/odm.tests/HttpsIntegrationTests.cs` (new)
- **Tier:** standard
- **Done when:** Tests skip cleanly when env var is absent. When `ODM_TEST_HOST=192.168.1.190` is set and camera is reachable, all four tests pass.
- **Blockers:** Task 3 (test project references), Task 1 (scheme-upgrade), camera reachability.

#### Task 5: Create `SchemeUpgradeTests.cs` (offline unit tests)
- **Change:** Create offline unit tests that verify the scheme-upgrade logic WITHOUT a real camera. Test cases: (a) single HTTP URI → generates HTTPS URI on 443 then 8443, (b) HTTPS URI → no upgrade needed, (c) mixed HTTP+HTTPS URIs → HTTPS URI preferred. These test the URI-generation logic extracted from Task 1, not the full session.
- **Files:** `odm/odm.tests/SchemeUpgradeTests.cs` (new)
- **Tier:** standard
- **Done when:** All offline scheme-upgrade tests pass in CI (no camera needed).
- **Blockers:** Task 1 (the logic must be testable — may need to extract a static helper method).

#### VERIFY: Phase 2
- Build: MSBuild `Release|x64` — must succeed
- Tests: all existing + new offline tests green
- Integration tests: pass on dev machine with camera, skip in CI
- Stop and report

---

### Phase 3: F2 — RTSP Streaming over HTTPS

#### Task 6: Transport negotiation in `GetStreamUri`
- **Change:** In `NvtSession.fs:GetStreamUri` (line 1463), when the device session URI scheme is `https://`, try `RtspOverHttp` transport if standard RTSP fails. Sequence: (1) call `GetStreamUri` with the original `StreamSetup`, (2) if the returned URI is unreachable or the device is HTTPS-only, retry with `StreamSetup { stream = StreamType.RTPUnicast; transport = { protocol = TransportProtocol.HTTP } }`. Return the first successful URI.
- **Files:** `onvif/onvif.session/NvtSession.fs` (lines 1463-1470)
- **Tier:** premium
- **Done when:** `GetStreamUri` returns a playable URI for HTTPS-only camera 192.168.1.190. Log shows transport negotiation sequence.
- **Blockers:** Camera must support at least one of standard RTSP or RTSP-over-HTTP. If camera returns standard RTSP on a separate port that is open, this negotiation is unnecessary but must not break.

#### Task 7: FixUrl HTTPS + RTSP-over-HTTP awareness
- **Change:** In `FixUrl` (NvtSession.fs:754-779), handle the case where the stream URI uses HTTP/HTTPS scheme (RTSP-over-HTTP tunnel) instead of `rtsp://`. When the device URI is HTTPS and the stream URI is `http://` on port 80/443, upgrade the stream URI scheme to `https://` and apply the same host/port fixup logic. Add `rtsps://` to the scheme-aware host fixup.
- **Files:** `onvif/onvif.session/NvtSession.fs` (lines 754-779)
- **Tier:** standard
- **Done when:** `FixUrl` with input `http://192.168.1.190:80/stream` on an HTTPS device returns `https://192.168.1.190:443/stream`. `FixUrl` with `rtsps://` preserves the scheme.
- **Blockers:** None.

#### Task 8: `FixUrlHttpsTests.cs` + `StreamTransportNegotiationTests.cs`
- **Change:** Unit tests for FixUrl HTTPS behavior and transport negotiation logic. Test matrix: (a) `rtsp://` URI on HTTPS device — host fixup only, (b) `http://` stream URI on HTTPS device — upgrade to `https://`, (c) `rtsps://` URI — preserve scheme + fixup, (d) transport negotiation sequence — mock failure of standard RTSP triggers RtspOverHttp retry.
- **Files:** `odm/odm.tests/FixUrlHttpsTests.cs` (new), `odm/odm.tests/StreamTransportNegotiationTests.cs` (new)
- **Tier:** standard
- **Done when:** All unit tests pass offline.
- **Blockers:** Task 7 (FixUrl changes must be in place).

#### VERIFY: Phase 3
- Build: MSBuild `Release|x64` — must succeed
- Tests: all existing + Phase 2 + Phase 3 tests green
- Integration: `HttpsStreamingTests` (part of Task 4 or extended) returns usable stream URI
- Stop and report

---

### Phase 4: F3 — RTSPS Native Streaming (Best-Effort)

#### Task 9: Detect `rtsps://` scheme before player launch
- **Change:** In `VideoPlayerActivity.fs`, before passing the media URI to the player view, check if the scheme is `rtsps://`. If the native player (Live555) does not support it, set an error message on the view model instead of passing the URI. The detection can be a simple scheme check. Live555's RTSPS support depends on whether it was compiled with OpenSSL — assume it was NOT for safety; provide a clear in-panel error.
- **Files:** `odm/odm.ui.activities/VideoPlayerActivity.fs` (line 49), `odm/odm.ui.views/views/SectionNVT/LiveVideoView.xaml.cs` (error display)
- **Tier:** premium
- **Done when:** An `rtsps://` URI produces a visible error message in the video panel instead of a blank/crash. Standard `rtsp://` URIs continue to work.
- **Blockers:** Need to verify the error display path in `LiveVideoView.xaml.cs`. WPF-dependent — cannot be integration-tested headlessly.

#### Task 10: `RtspsUriTests.cs` (offline)
- **Change:** Unit tests: (a) `FixUrl` with `rtsps://` input produces correct host/port fixup, (b) scheme detection helper identifies `rtsps://` correctly, (c) fallback message content validation.
- **Files:** `odm/odm.tests/RtspsUriTests.cs` (new)
- **Tier:** cheap
- **Done when:** All offline tests pass.
- **Blockers:** Task 7 (FixUrl rtsps:// handling), Task 9 (scheme detection helper).

#### VERIFY: Phase 4
- Build: MSBuild `Release|x64` — must succeed
- Tests: full suite green (offline + integration where available)
- Manual: launch ODM against 192.168.1.190, verify video panel shows stream or clear error
- Stop and report

---

### Phase 5: Regression + Cleanup

#### Task 11: Full regression pass
- **Change:** Run all 39 existing `AuthFlowTests` + all new tests. Verify ODM connects to a standard HTTP camera (if available) to confirm no regression. Review all changed F# files for unintended side effects on HTTP path.
- **Files:** No code changes — test execution only
- **Tier:** cheap
- **Done when:** All tests green. HTTP camera (if available) connects normally.
- **Blockers:** None.

#### Task 12: Logging and documentation
- **Change:** Add `log.WriteInfo` calls at scheme-upgrade decision points and transport negotiation fallbacks. Add inline comments in F# code explaining the HTTPS fallback logic for future maintainers.
- **Files:** `onvif/onvif.session/NvtSession.fs` (logging additions only)
- **Tier:** cheap
- **Done when:** Log output when connecting to HTTPS-only camera shows: scheme detection, fallback attempts, final binding used, transport negotiation result.
- **Blockers:** None.

#### VERIFY: Phase 5 (Final)
- Build: MSBuild `Release|x64` — must succeed
- Tests: complete suite green
- Integration: all `HttpsIntegrationTests` pass against 192.168.1.190
- Stop and report — sprint complete

---

## Summary Table

| Task | Phase | Feature | Tier | Key File |
|------|-------|---------|------|----------|
| 1 — Scheme-upgrade fallback | 1 | F1 | premium | NvtSession.fs:468-575 |
| 2 — Verify HTTPS unsecured factory | 1 | F1 | standard | NvtSession.fs:314-318 |
| 3 — Test project references | 1 | F1 | cheap | odm.tests.csproj |
| 4 — Integration test scaffold | 2 | F1 | standard | HttpsIntegrationTests.cs |
| 5 — Scheme-upgrade unit tests | 2 | F1 | standard | SchemeUpgradeTests.cs |
| 6 — Transport negotiation | 3 | F2 | premium | NvtSession.fs:1463-1470 |
| 7 — FixUrl HTTPS awareness | 3 | F2 | standard | NvtSession.fs:754-779 |
| 8 — FixUrl + transport tests | 3 | F2 | standard | FixUrlHttpsTests.cs |
| 9 — RTSPS scheme detection | 4 | F3 | premium | VideoPlayerActivity.fs:49 |
| 10 — RTSPS unit tests | 4 | F3 | cheap | RtspsUriTests.cs |
| 11 — Regression pass | 5 | All | cheap | (test-only) |
| 12 — Logging + comments | 5 | All | cheap | NvtSession.fs |

## Constraints Checklist
- [x] All features preserve existing HTTP camera path (factory_wrapper serves both)
- [x] WPF app buildable at every VERIFY (`Release|x64`)
- [x] Tests headless — no WPF Dispatcher, no Application instance
- [x] Integration tests gated by `[TestCategory("Integration")]` + env var
- [x] No changes to generated ONVIF SOAP stubs
- [x] F3 scoped as best-effort (graceful error if Live555 lacks RTSPS)
- [x] Each task = one git commit
