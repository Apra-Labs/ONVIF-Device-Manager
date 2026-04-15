# HTTPS/TLS Camera Support

Delivered in Sprint 4 (squash-merged as commit `2326947` and `b7ee223`).

---

## What Was Built

ODM can now connect to cameras where HTTP is disabled and only HTTPS is available. All ONVIF operations (discovery, capabilities, profiles, PTZ, imaging, events) and live video streaming work against such cameras.

Three feature areas were shipped:

| Feature | Description |
|---------|-------------|
| **F1 — HTTPS-only camera connectivity** | Scheme-upgrade fallback when WS-Discovery returns an HTTP XAddr on a camera that only accepts HTTPS |
| **F2 — RTSP streaming over HTTPS** | Automatic transport negotiation (plain RTSP → RTSP-over-HTTP) for HTTPS-only cameras |
| **F3 — RTSPS detection** | Graceful error message when the camera returns an `rtsps://` stream URI |

---

## Key Architecture Decisions

### SslStream single-write transport replaces `HttpsTransportBindingElement`

**Problem:** gSOAP 2.8 (used by most IP cameras, including Milesight) crashes or stalls when the HTTP request is split across multiple TLS application-data records. .NET's `HttpWebRequest` / `HttpsTransportBindingElement` always writes headers and body as separate TLS records.

**Solution:** A custom WCF `BindingElement` — `SslStreamTransportBindingElement` in `onvif/onvif.session/SslStreamTransport.fs` — opens a raw `TcpClient`, wraps it in `SslStream`, and sends the full HTTP request (headers + body) in a single `Write` call. This guarantees one TLS record per request and is compatible with all tested cameras.

**Alternative rejected:** Patching `HttpsTransportBindingElement` to use `AllowWriteStreamBuffering=true` — not available in .NET 4.0 WCF binding pipeline.

### WS-Addressing Action header stripping

gSOAP cameras return HTTP 500 `MustUnderstand` faults when they receive `ws:Action` headers. `SslStreamTransport.fs` strips the `Action` mustUnderstand header from outgoing requests by default.

**Exception:** Channels declared with `wsAddressing=true` (Events, Metadata subscriptions) require the Action header for server-side dispatch — stripping is skipped for those channels based on the message version (`Soap12WSAddressing10`).

### Scheme-upgrade fallback (`NvtSession.fs` — `CreateSession(uris:Uri[])`)

When WS-Discovery returns `http://` XAddrs and TCP-level connectivity or SOAP probing fails, `GenerateHttpsVariants` generates HTTPS alternatives (port 443, then 8443) and retries. The SOAP probe (`GetSystemDateAndTime` unauthenticated) is used rather than a bare TCP connect because some cameras accept TCP connections on port 80 but silently drop HTTP payloads.

Fallback is sequential: all HTTP URIs are tried to completion before HTTPS variants are attempted. This avoids racing HTTPS connections against HTTP on cameras that support both.

### `Expect100Continue = false`

Cameras do not understand `Expect: 100-Continue` and silently stall when it is present. This flag is set on both `ServicePointManager` (global, in `App.xaml.cs` and test init) and per-`ServicePoint` inside `CreateSession`.

### DOCTYPE stripping

The Milesight Analytics service includes an XML `DOCTYPE` declaration in its SOAP responses. WCF's `XmlDictionaryReader` rejects DOCTYPE declarations as a security measure. `SslStreamTransport.fs` strips any DOCTYPE before passing the response to the WCF message decoder.

### Certificate validation bypass

`App.xaml.cs` sets `ServicePointManager.ServerCertificateValidationCallback` to return `true` unconditionally. This is intentional — ONVIF cameras use self-signed certificates and there is no PKI to validate against. This must not be removed.

---

## Transport Selection Per Channel Type

| Channel type | `wsAddressing` | Transport used | Action header |
|---|---|---|---|
| Standard ONVIF services (Device, Media, PTZ, etc.) | false | `SslStreamTransportBindingElement` | Stripped |
| Events / Metadata subscriptions | true | `SslStreamTransportBindingElement` | Preserved |
| HTTP cameras (all channels) | false | `HttpTransportBindingElement` | Preserved (no mustUnderstand issue on HTTP) |

---

## RTSP Transport Negotiation (F2)

For HTTPS devices, `GetStreamUri` in `NvtSession.fs` attempts:
1. Standard RTSP (`Transport.Protocol = RTSP`)
2. If the camera's device session is HTTPS-only: retry with `RtspOverHttp` (`Transport.Protocol = HTTP`)

The retry is transparent — no modal dialogs. `FixUrl` was updated to handle `http://` stream URIs that are RTSP-over-HTTP tunnels on an HTTPS device, upgrading the stream URI scheme when appropriate.

---

## RTSPS Graceful Error (F3)

`VideoPlayerActivity.fs` exposes `IsRtspsUri` (a static member, case-insensitive scheme check) called after `GetStreamUri` returns. If the URI scheme is `rtsps://`, a `NotSupportedException` is raised with a descriptive message. The existing `with`-handler in the activity surfaces this via `ErrorView` — the video panel shows the error instead of going blank or crashing.

Live555 in this build was not compiled with OpenSSL, so native `rtsps://` playback is not supported. F3 is best-effort graceful degradation.

---

## Test Coverage

| Test file | Type | What it covers |
|---|---|---|
| `HttpsBindingTests.cs` | Offline unit | `SslStreamTransportBindingElement` present when `useTls=true` |
| `SchemeUpgradeTests.cs` | Offline unit | `GenerateHttpsVariants` logic — HTTP→HTTPS URI generation, HTTPS pass-through, deduplication |
| `HttpsIntegrationTests.cs` | Integration (skipped in CI) | Connect, GetCapabilities, GetProfiles, GetStreamUri against 192.168.1.190 |
| `FixUrlHttpsTests.cs` | Offline unit | `FixUrl` with HTTPS device URI + RTSP/RTSPS stream URI combinations |
| `StreamTransportNegotiationTests.cs` | Offline unit | RTSP → RtspOverHttp fallback sequence |
| `RtspsUriTests.cs` | Offline unit | `IsRtspsUri` detection — true/false for various schemes including uppercase |
| `SslStreamHelpersTests.cs` | Offline unit | `decodeChunked`, `parseResponse`, `stripDoctype` helpers |

Integration tests are gated by `[TestCategory("Integration")]` and skip when `ODM_TEST_HOST` is not set. CI runs only offline tests.
