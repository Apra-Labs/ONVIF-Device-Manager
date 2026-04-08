# Requirements — HTTPS/TLS/RTSPS Camera Support

## Base Branch
`development` — branch to fork from and merge back to

## Sprint Branch
`feature/https_support`

## Goal
ODM must work seamlessly with cameras where HTTP is disabled and only HTTPS is available. All current features (discovery, connection, PTZ, imaging, events, live video) must function against such cameras. Additionally, where HTTPS is active, RTSP streaming over HTTPS tunneling and native RTSPS (`rtsps://`) must be supported.

## Test Camera
- **IP:** 192.168.1.190 (local network, on-device)
- **Configuration:** HTTP disabled, HTTPS enabled with self-signed certificate
- **Credentials:** passed via environment variables — never hardcoded
  - `ODM_TEST_HOST` = `192.168.1.190`
  - `ODM_TEST_USER` = camera username
  - `ODM_TEST_PASS` = camera password
  - `ODM_TEST_HTTPS_PORT` = HTTPS port (default 443)
- **Test approach:** headless integration tests (no WPF UI) hitting the real camera directly via the ONVIF session layer (`NvtSession`)

## Features (priority order — top to bottom)

---

### F1 — HTTPS-Only Camera: Discovery + All Features (HIGHEST PRIORITY)

**Problem — Root causes identified in code:**

1. **Initial time-sync call uses HTTP unconditionally (`NvtSession.fs` ~lines 662-694):**
   `GetDeviceUnsecureFactory` always creates an `HttpTransportBindingElement` (plain HTTP). For an HTTPS-only camera this first call (needed to sync device time for WS-Security digest) will fail with connection refused. The unsecured factory must use `HttpsTransportBindingElement` when the device URI scheme is `https`. No WS-Security digest is needed here — TLS transport alone is sufficient for the time-sync call.

2. **Discovery XAddr scheme (`NvtDiscovery.fs:28-150`, `NvtSession.fs:791`):**
   WS-Discovery returns whatever XAddr the camera advertises. An HTTPS-only camera may advertise:
   - `https://...` XAddr → `NvtSession.fs:791` already detects `Uri.UriSchemeHttps` → uses HTTPS binding ✓
   - `http://...` XAddr with port 80 closed → scheme detection returns `useTls=false` → HTTP binding used → connection fails ✗
   
   A scheme-upgrade fallback is required: if the discovered XAddr is `http://` and TCP connection is refused/timeout, retry automatically with `https://` on the standard HTTPS port (443 first, then 8443).

3. **Certificate validation (`App.xaml.cs:74-76`):**
   `ServerCertificateValidationCallback` returns `true` unconditionally — this is **intentionally correct** for ONVIF cameras using self-signed certificates. Must be preserved and documented.

**Code locations:**
```
onvif/onvif.session/NvtSession.fs:418-466   ← binding factories (HTTPS path exists)
onvif/onvif.session/NvtSession.fs:646-694   ← CreateSession + unsecured time-sync
onvif/onvif.session/NvtSession.fs:791       ← scheme detection (useTls)
onvif/onvif.discovery/NvtDiscovery.fs:28-150 ← WS-Discovery client
odm/odm.ui.app/App.xaml.cs:74-76           ← cert validation bypass (preserve)
```

**Acceptance Criteria:**
- [ ] Camera 192.168.1.190 (HTTP disabled) discovered via WS-Discovery
- [ ] ODM connects successfully — no timeout/refused/TLS errors
- [ ] Device time sync succeeds over HTTPS
- [ ] GetCapabilities, GetDeviceInformation, GetProfiles all succeed
- [ ] PTZ, Imaging, Events (if supported by camera) load correctly
- [ ] Scheme-upgrade fallback fires when HTTP XAddr is unreachable
- [ ] Headless integration test `HttpsOnlyCameraTests` — connect, get capabilities, enumerate profiles — all pass against 192.168.1.190

---

### F2 — RTSP Streaming over HTTPS for HTTPS Cameras

**Problem — Root causes identified in code:**

1. **GetStreamUri always requests plain RTSP (`NvtSession.fs:1463-1470`):**
   `GetStreamUri` is called with the `StreamSetup` from the UI. `VideoPlayerActivity.fs:49-50` passes through whatever `StreamSetup` the view provides. The view currently never requests `RtspOverHttp` transport. For HTTPS-only cameras, the camera may serve RTSP on a separate port (works as-is) or only via RTSP-over-HTTP tunneling on port 443 (requires `Transport.Protocol = RtspOverHttp`).

2. **FixUrl logic (`NvtSession.fs:754-779`):**
   Host/port fixup runs for mismatched IPs but does not account for RTSP-over-HTTP tunnel port. When the device uses HTTPS and returns an RTSP-over-HTTP stream URI on port 443/80, the FixUrl must preserve the tunneled port correctly.

3. **Transport negotiation strategy:**
   For HTTPS devices, ODM should request stream URIs in this order until one succeeds:
   1. Standard RTSP (existing behaviour)
   2. `RtspOverHttp` (RTSP tunneled over HTTP/HTTPS)
   
   The retry must be transparent — same UI, no modal dialogs.

**Code locations:**
```
onvif/onvif.session/NvtSession.fs:1463-1470   ← GetStreamUri wrapper
onvif/onvif.session/NvtSession.fs:754-779     ← FixUrl
onvif/onvif.session/Services/MediaAsync.fs:~120 ← SOAP GetStreamUri call
odm/odm.ui.activities/VideoPlayerActivity.fs:49 ← stream setup passed in
```

**Acceptance Criteria:**
- [ ] Live video streams from 192.168.1.190 (HTTPS-only) using the best available transport
- [ ] When plain RTSP fails, `RtspOverHttp` is tried automatically
- [ ] FixUrl produces a well-formed, player-usable URI for both RTSP and RTSP-over-HTTP
- [ ] Headless test `HttpsStreamingTests` — GetStreamUri returns a usable URI; FixUrl produces correct URL for HTTPS device

---

### F3 — RTSPS (`rtsps://`) Native Streaming Support

**Problem — Root causes identified in code:**

1. **Native player (`odm.player.net/libapi.cpp`) handles only `rtsp://`:**
   The C++ player pipeline does not handle `rtsps://`. The media library (DirectShow/VLC) may or may not support `rtsps://` natively.

2. **URI not inspected before player launch:**
   `VideoPlayerActivity.fs:49-50` passes the URI string directly. If the camera returns `rtsps://`, the player receives it but likely fails silently or crashes.

3. **No graceful error path:**
   No user-visible error is shown when the player cannot open a stream URI — the video panel just stays blank.

**Implementation strategy:**
- Detect `rtsps://` scheme before passing to player
- If the current player binary/library supports it: pass through and verify
- If not: show a clear in-panel error message ("RTSPS not supported by this build — try enabling RTSP-over-HTTPS in stream settings") instead of blank/crash
- Add `rtsps://` to FixUrl scheme-aware handling

**Code locations:**
```
odm/odm.player/odm.player.net/libapi.cpp           ← native player, scheme handling
odm/odm.ui.activities/VideoPlayerActivity.fs:49    ← URI passed to player
onvif/onvif.session/NvtSession.fs:754-779          ← FixUrl (add rtsps: scheme)
odm/odm.ui.views/views/SectionNVT/LiveVideoView.xaml.cs ← error display path
```

**Acceptance Criteria:**
- [ ] `rtsps://` URI from camera does not crash or hang ODM
- [ ] If player supports `rtsps://`: live video plays
- [ ] If player does not support `rtsps://`: in-panel error message displayed
- [ ] FixUrl handles `rtsps://` scheme correctly (host/port fixup)
- [ ] Headless test `RtspsUriTests` — FixUrl with `rtsps://` URIs produces correct output; player path routing verified

---

## Out of Scope
- Client certificate authentication (mutual TLS) — cameras rarely require it; deferred
- Custom CA trust store UI — self-signed cert bypass is sufficient
- ONVIF Profile T / streaming analytics over HTTPS — next sprint
- WS-Discovery over HTTPS (DPWS) — cameras still use UDP multicast for initial discovery
- NAT traversal or port forwarding — separate concern

## Constraints
- All features must not break existing HTTP cameras — full regression required
- WPF application must remain buildable (`Release|x64`) at every VERIFY checkpoint
- Tests must be headless — no WPF Dispatcher, no `Application` instance
- Integration tests depend on 192.168.1.190 being reachable; must be skippable via `[TestCategory("Integration")]` when camera is absent (CI skips them, dev machine runs them explicitly)
- F# (session layer) and C# (UI/test layer) both in play — changes span both
- Do NOT alter generated ONVIF SOAP stubs (`onvif.services.cs`, `onvif.types.cs`)
- Native player (C++) changes are acceptable for F3 but must not break existing RTSP playback

## Testing Strategy

### Headless Integration Tests (`odm.tests/HttpsIntegrationTests.cs`, new file)
Connect to 192.168.1.190 directly via `NvtSession` (no WPF):
```
[TestClass]
[TestCategory("Integration")]
public class HttpsIntegrationTests {
    // Skipped automatically if ODM_TEST_HOST env var not set
    // Tests: Connect(), GetCapabilities(), GetProfiles(), GetStreamUri()
}
```

### Offline Unit Tests (extend existing `odm.tests`)
- `SchemeUpgradeTests` — HTTP XAddr → HTTPS fallback logic (mock HTTP failure)
- `HttpsBindingTests` — `GetDeviceUnsecureFactory` produces HTTPS binding when device URI is `https://`
- `FixUrlHttpsTests` — FixUrl with HTTPS device URI + RTSP stream URI combinations
- `RtspsUriTests` — `rtsps://` scheme detection and routing
- `StreamTransportNegotiationTests` — plain RTSP → RtspOverHttp fallback sequencing

### Regression
- All existing `AuthFlowTests` (39 tests) must stay green
- Full `Release|x64` build must pass after every phase

## Risk Register
| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| HTTPS-only camera advertises HTTP XAddr (scheme mismatch) | High | F1 blocked | Scheme-upgrade fallback is TASK-1 (riskiest, first) |
| Unsecured time-sync call fails on HTTPS-only camera | High | F1 blocked | Fix `GetDeviceUnsecureFactory` HTTPS path (TASK-2) |
| Player library does not support `rtsps://` | Medium | F3 limited | Detect + fail gracefully with user message |
| RTSP-over-HTTP tunnel port mismatch | Medium | F2 partial | FixUrl port-aware handling; log transport used |
| F# + C# session changes break existing HTTP cameras | Medium | Regression | Regression test suite at every VERIFY |
| 192.168.1.190 not yet configured when sprint starts | Medium | Integration tests skip | Offline unit tests cover logic independently |
