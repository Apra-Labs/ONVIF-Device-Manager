# ODM Architecture

## Solution Structure

ODM is a C#/.NET 4.8 WPF application backed by a multi-language codebase:

| Layer | Language | Projects |
|-------|----------|----------|
| ONVIF session | F# | `onvif/onvif.session` |
| ONVIF types/services | C# (hand-maintained + WCF-generated) | `onvif/onvif.services` |
| UI activities (view models) | F# | `odm/odm.ui.activities` |
| UI views / controls | C# + XAML | `odm/odm.ui.views` |
| WPF app entry point | C# | `odm/odm.ui.app` |
| Native video pipeline | C++ | `odm/odm.player/odm.player.lib` |
| Player host (C++/CLI bridge) | C++/CLI | `odm/odm.player/odm.player.host` |
| Player .NET API | C++/CLI | `odm/odm.player/odm.player.net` |
| Unit/integration tests | C# (MSTest) | `odm/odm.tests` |

---

## Video Pipeline

Video playback is a multi-process pipeline:

```
NvtSession.fs (F#)
  └── GetStreamUri() → RTSP URI
       ↓
VideoPlayerActivity.fs (F#)
  └── passes URI to player view
       ↓
LiveVideoView.xaml.cs (C#)
  └── calls odm.player.net.dll (C++/CLI)
       ↓
odm.player.host.exe (C++/CLI, separate process)
  ├── Live555 → RTSP/RTP receive + SDP codec dispatch
  ├── H264VirtualSink / H265VirtualSink → Annex-B NAL framing
  ├── FFmpeg avcodec → decode (H.264, H.265, MJPEG, MPEG4)
  └── VideoRenderer → YUV→RGB → shared VideoBuffer
       ↓
VideoPlayer.xaml.cs (C#)
  └── WriteableBitmap rendering from VideoBuffer
```

### Codec dispatch in `Live555.cpp` — `InitSubsession()`

The SDP codec name maps to an FFmpeg `AV_CODEC_ID`:

| SDP codec name | FFmpeg codec ID |
|---|---|
| `H264` | `AV_CODEC_ID_H264` |
| `H265` | `AV_CODEC_ID_HEVC` |
| `JPEG`, `MJPEG` | `AV_CODEC_ID_MJPEG` |
| `MP4V-ES`, `MPEG4` | `AV_CODEC_ID_MPEG4` |
| `MPV` | `AV_CODEC_ID_MPEG2VIDEO` |

Unknown codec names return `nullptr` from `InitSubsession()` — the subsession is silently skipped.

### NAL framing sinks

H.264 and H.265 RTP payloads arrive as raw NAL units. Both require Annex-B start code prepending (`0x00 0x00 0x00 0x01`) before the FFmpeg decoder. `H264VirtualSink` and `H265VirtualSink` perform this framing identically.

---

## Transport Layer (`onvif/onvif.session/`)

### `NvtSession.fs` — session routing

`NvtSessionFactory` is the entry point for all ONVIF communication. It accepts a `NetworkCredential` and a device `Uri` and creates WCF channel factories.

**HTTPS detection:** `CreateSession(deviceUri)` checks `Uri.UriSchemeHttps` and sets `useTls=true`, which selects `SslStreamTransportBindingElement` instead of `HttpTransportBindingElement` when building channel factories.

**Scheme-upgrade fallback:** `CreateSession(uris:Uri[])` (the multi-URI overload used after WS-Discovery) probes each URI with a SOAP `GetSystemDateAndTime` call. If all original HTTP URIs fail, `GenerateHttpsVariants` produces HTTPS alternatives (port 443, then 8443) and retries. See `docs/features/https-tls.md` for details.

**`factory_wrapper`:** Memoizes separate HTTP and HTTPS factory instances. The same `NvtSessionFactory` instance can serve channels over both schemes without re-allocating factories.

### `SslStreamTransport.fs` — custom TLS transport

Replaces `HttpsTransportBindingElement` for all HTTPS connections. Sends the full HTTP request (headers + body) in a single `SslStream.Write` call, working around gSOAP 2.8's multi-record TLS fragmentation crash.

Key helpers in `SslStreamHelpers` module:
- `decodeChunked` — handles `Transfer-Encoding: chunked` responses
- `parseResponse` — splits HTTP response into status, headers, body
- `stripDoctype` — removes XML DOCTYPE declarations before WCF message parsing

### `FixUrl` — host/port fixup

`FixUrl` (NvtSession.fs) corrects RTSP stream URIs where the camera's self-reported host/port does not match the network address used to connect. It handles:
- `rtsp://` — standard fixup
- `rtsps://` — scheme preserved, host/port fixed
- `http://` on HTTPS device — upgraded to `https://` with port correction

---

## ONVIF Type System (`onvif/onvif.services/`)

### Type maintenance model

`onvif.types.cs` is **hand-maintained** (not auto-generated from WSDL). It defines the C# data contracts used for ONVIF SOAP deserialization. Adding support for a new ONVIF feature requires:

1. Adding the new type/enum to `onvif.types.cs`
2. Adding the corresponding XSD to `schemas/onvif.xsd`
3. Mirroring the XSD change to `Service References/services/onvif.xsd` (both copies must stay in sync)

The WCF service proxies (`Reference.cs`, `Reference1.cs`) are auto-generated from WSDL via svcutil and are not manually edited.

### VideoEncoding enum

```csharp
public enum VideoEncoding {
    jpeg, mpeg4, h264, h265
}
```

Deserialization fails at runtime if a camera returns an encoding value not present in this enum. Every new codec added to the ONVIF spec requires an enum member here.

---

## Credential Flow

Credentials are managed outside the F# session layer. `DeviceListViewModel.cs` (C#) owns credential iteration:

1. Checks the per-device credential cache (keyed by host IP)
2. On cache miss or failure, iterates all stored credentials via `AccountManager`
3. Falls back to anonymous session as a last resort

The F# `NvtSessionFactory` accepts one `NetworkCredential` at construction time and is stateless with respect to credential rotation. See `docs/credentials.md` for full details.

---

## WS-Discovery (`onvif/onvif.discovery/`)

**File:** `onvif/onvif.discovery/NvtDiscovery.fs`

ONVIF WS-Discovery is implemented in F# using `System.ServiceModel.Discovery` (WCF). The
key types are:

| Type | Description |
|------|-------------|
| `NvtManager` | Concrete class; manages the discovery lifecycle |
| `INvtManager` | Interface — `Discover(TimeSpan)`, `Observe()` |
| `INvtNode` | Represents one discovered camera; has `identity: NvtIdentity` |
| `NvtIdentity` | `endpointReference`, `uris: Uri[]`, `scopes: Uri[]` |
| `WsDiscoveryObservable` | Low-level WCF `DiscoveryClient` wrapped as `IObservable<WsDiscoveredEndpoint>` |

### Usage pattern

```csharp
var manager = new NvtManager();
var nvtMgr  = (INvtManager)manager;
using (nvtMgr.Observe().Subscribe(myObserver))  // subscribe first
using (nvtMgr.Discover(TimeSpan.FromSeconds(5))) // then probe
{
    Thread.Sleep(6000);  // wait for probe window + buffer
}
// myObserver.Nodes now contains all discovered INvtNode instances
```

### Probe types

`Discover()` fires two WCF probes in parallel — one for each contract type cameras
advertise:

- `NetworkVideoTransmitter` — `http://www.onvif.org/ver10/network/wsdl`
- `Device` — `http://www.onvif.org/ver10/device/wsdl`

Each probe uses `FindCriteria.Duration = TimeSpan.MaxValue` + a `Timeout()` operator to
honour the caller-specified duration. Responses are deduplicated by endpoint reference.

### Hello/Bye announcements

`NvtManager` also implements an announcement service host (`UdpAnnouncementEndpoint`) that
passively receives Hello and Bye multicast announcements from cameras as they come online
or go offline. This is the mechanism that drives the live device list updates in the UI.

---

## Test Architecture

### Unit/integration tests (`odm/odm.tests/`)

MSTest project targeting .NET 4.8. Tests are split by category:

- **Offline** (`[TestCategory("Offline")]` or untagged) — run in CI, no camera required
- **Integration** (`[TestCategory("Integration")]`) — skipped in CI via `Assert.Inconclusive` when `ODM_TEST_HOST` env var is absent; run manually against a real camera

The test project references `onvif.session.dll` and its dependencies from the Release output directory.

### Camera Compatibility Sweep (`CameraCompatibilitySweepTests`)

**File:** `odm/odm.tests/CameraCompatibilitySweepTests.cs`

Mirrors the real ODM UI workflow in a test:

1. Reads `CredentialStore.Instance.GetAll()` — DPAPI-decrypted credentials, same file ODM uses.
2. Runs `NvtManager` WS-Discovery for 5 seconds — finds cameras on the local network.
3. For each discovered camera, tries every stored credential (then anonymous) via
   `NvtSessionFactory.CreateSession(identity.uris)` — exact same multi-URI probe ODM uses.
4. For authenticated cameras: `GetProfiles()` → `GetStreamUri()` to get the RTSP URL.
5. For each RTSP URL: TCP socket probe to the RTSP port (default 554) to verify reachability.

The test never asserts pass/fail — it is always green as long as at least one camera could
be authenticated. It writes a Markdown report to the test output directory (or
`ODM_SWEEP_REPORT_DIR`). The test is `Inconclusive` (orange in CI) when no cameras are
found or none authenticate.

To run: place the test DLL in an environment with ODM already configured (credentials.dat
present) and on the same network as the cameras.

### E2E UI tests (planned — `odm/odm.e2e-tests/`)

FlaUI-based (UIA3) end-to-end test suite that drives the full ODM UI against real cameras and uses Claude vision API for screenshot analysis. See `docs/e2e-smoke-test-poc.md` for architecture details.
