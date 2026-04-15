# Media2 Transparent Routing

## Overview

ODM supports ONVIF Media2 (ver20/media/wsdl) cameras transparently. When a camera
advertises Media2, all video operations route through it automatically. Media1 remains
the fallback for cameras that do not support Media2 and as a per-operation safety net.
No changes were required to `INvtSession`, activity files, or the GUI.

Tracking issue: https://github.com/Apra-Labs/ONVIF-Device-Manager/issues/21

---

## Detection

`GetMedia2Client()` in `NvtSession.fs` is the single detection gate for the entire
routing layer. It inspects the `GetServices()` response for the `ver20/media/wsdl`
namespace and returns a non-null `IMedia2` WCF proxy if found.

The result is **memoized** (`Async.Memoize`) so detection happens exactly once per
session. Subsequent calls return the cached result immediately.

---

## Routing Pattern

Every video operation in `NvtSession.fs` follows the same pattern:

```fsharp
member this.SomeVideoOp(args) = async {
    let! media2 = GetMedia2Client()
    if media2 |> NotNull then
        try return! someVideoOpViaMedia2(media2, args)
        with _ -> return! someVideoOpViaMedia1(args)
    else
        return! someVideoOpViaMedia1(args)
}
```

Key properties of this design:

- **Media2-first** — if the camera supports Media2, the Media2 path is tried first.
- **Per-operation fallback** — each operation falls back to Media1 independently.
  A failure in one operation does not affect others.
- **Transparent** — callers use the standard `INvtSession` interface; the routing
  is invisible to activities and the GUI.
- **No shared channel state** — Media1 and Media2 use separate WCF channel factories
  (`getMediaFactory` vs `getMedia2Factory`). Mixed calls cannot corrupt each other.

---

## Routed Operations

| Operation | Media2 request type | Notes |
|-----------|---------------------|-------|
| `GetProfiles` | `Media2GetProfilesRequest` | Returns all profiles; embedded encoder configs parsed inline |
| `GetStreamUri` | `Media2GetStreamUriRequest` | `Protocol = "RtspUnicast"` |
| `GetSnapshotUri` | `Media2GetSnapshotUriRequest` | Returns plain URI string; same parse path as GetStreamUri |
| `GetVideoEncoderConfigurationOptions` | `Media2GetVideoEncoderConfigurationOptionsRequest` | Per-encoding options block; fixes H265 slider ranges (issue #21) |
| `SetVideoEncoderConfiguration` | `Media2SetVideoEncoderConfigurationsRequest` | Raw XML body; `ForcePersistence` omitted (not supported by Media2) |
| `GetVideoEncoderConfigurations` | `Media2GetVideoEncoderConfigurationsRequest` | Full-field parser: encoding, resolution, rateControl, govLength, h264/h265 block |
| `GetCompatibleVideoEncoderConfigurations` | `Media2GetVideoEncoderConfigurationsRequest` | Reuses the same helper as GetVideoEncoderConfigurations with a `ProfileToken` filter |
| `GetVideoSourceConfigurations` | `Media2GetVideoSourceConfigurationsRequest` | Parses token, name, sourceToken, bounds |

---

## WCF Interface Design

All `IMedia2` operations use `System.ServiceModel.Channels.Message` as the return type
(Begin/End async pair). XML is parsed in `NvtSession.fs` using LINQ-to-XML with
namespace-qualified element names.

Two XML namespaces are used throughout:

| Prefix | Namespace URI | Usage |
|--------|---------------|-------|
| `tr2` | `http://www.onvif.org/ver20/media/wsdl` | Response wrapper elements (e.g., `tr2:Profiles`, `tr2:Options`) |
| `tt` | `http://www.onvif.org/ver10/schema` | Field elements inside each block (e.g., `tt:Encoding`, `tt:Resolution`) |

Request classes use `[MessageContract(WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl")]`
and `[MessageBodyMember]` on each field. This pattern was chosen because the stock WCF
proxy generator does not handle Media2 responses correctly — the raw Message approach
gives full control over XML parsing and avoids serializer mismatches.

### XML parsing helper

`Media2XmlParser` (in `onvif.services`) is a static C# class that encapsulates all
LINQ-to-XML parsing logic. It is deliberately separate from the F# session code so it
can be unit-tested without a WCF channel. Parsers are defensive: missing optional
elements (e.g., `GovLengthRange`) are left null rather than default-initialised, to
preserve the distinction between "not provided" and "zero".

---

## Why INvtSession Was Not Changed

Activities and the GUI call `INvtSession` methods by name. Keeping the interface
unchanged means no activity, view model, or test mock required modification. The
routing is an internal implementation detail of `NvtSession.fs`.

The one temporary exception — `GetVideoEncoderConfigurationsMedia2` — was added as a
bridge during development and **retired in Phase 5**. Callers in
`VideoSettingsActivity.fs` and `ProfileManagementActivity.fs` were migrated to the
standard `GetVideoEncoderConfigurations()`, which now returns full H265 configurations
via Media2 routing.

---

## Why GetVideoEncoderConfigurationsMedia2 Was Retired

The original `GetVideoEncoderConfigurationsMedia2` member on `INvtSession` was a
temporary bridge that only parsed `token` and `encoding` from Media2 responses.
After Phase 4, `GetVideoEncoderConfigurations()` itself routes through Media2 and
returns fully-parsed configurations (all fields). Keeping the bridge method would have
left two ways to retrieve the same data with different field completeness. Retiring it
simplified the interface and removed the partial-parse path.

---

## Video Encoder Configuration Options — Issue #21 Root Cause

ONVIF Media1 returns `VideoEncoderConfigurationOptions` with a single
`h264`/`jpeg`/`mpeg4` sub-object. Cameras that encode H265 report options inside a
Media2-only `tr2:Options` block with `<tt:Encoding>H265</tt:Encoding>`. Media1 has no
`h265` sub-object, so H265 slider ranges were always empty.

Media2 returns one `tr2:Options` block per supported encoding. The parser maps each
block into the corresponding sub-object (`options.h265`, `options.h264`, `options.jpeg`)
of the existing `VideoEncoderConfigurationOptions` type. No type changes were needed.
