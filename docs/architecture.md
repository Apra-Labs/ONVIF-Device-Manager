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

## Test Architecture

### Unit/integration tests (`odm/odm.tests/`)

MSTest project targeting .NET 4.8. Tests are split by category:

- **Offline** (`[TestCategory("Offline")]` or untagged) — run in CI, no camera required
- **Integration** (`[TestCategory("Integration")]`) — skipped in CI via `Assert.Inconclusive` when `ODM_TEST_HOST` env var is absent; run manually against a real camera

The test project references `onvif.session.dll` and its dependencies from the Release output directory.

### E2E UI tests (planned — `odm/odm.e2e-tests/`)

FlaUI-based (UIA3) end-to-end test suite that drives the full ODM UI against real cameras and uses Claude vision API for screenshot analysis. See `docs/e2e-smoke-test-poc.md` for architecture details.
