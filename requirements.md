# ODM — Media2 Full Support
## Requirements

**Tracking issue:** https://github.com/Apra-Labs/ONVIF-Device-Manager/issues/21
**Base branch:** development
**Sprint branch:** feat/media2-support

---

## Problem Statement

ODM's Video Settings page is broken for H265 cameras that advertise H265 only via the ONVIF Media2 API (`ver20/media/wsdl`). These cameras report `Encoding=H264` via the legacy Media1 `GetVideoEncoderConfiguration` endpoint, and only expose their true H265 configuration via Media2's `GetVideoEncoderConfigurations` response.

The current code has a partial hack:
- `VideoSettingsActivity.fs` calls `GetVideoEncoderConfigurationsMedia2()` to detect H265 encoding.
- When H265 is confirmed via Media2 but Media1 reports H264 (`isMedia2OnlyH265` flag), it falls back to using `options.h264` (Media1 H264 options) for rate/quality validation, and skips govLength for H265 entirely.
- There is **no** `GetVideoEncoderConfigurationOptionsMedia2` method anywhere in the codebase — neither in the `IMedia2` WCF interface, nor in `INvtSession`, nor in any implementation.

### Impact
- Frame rate, resolution, bitrate, govLength sliders for H265 cameras may show wrong ranges or be silently ignored on apply.
- Applying settings on a Media2-only H265 camera sends a SetVideoEncoderConfiguration request with possibly invalid/clipped values.
- The bug is confirmed reproducible (see screenshot in issue #21).

---

## Root Cause

### 1. Missing `GetVideoEncoderConfigurationOptions` in IMedia2

`onvif\onvif.services\onvif.services.cs` — The `IMedia2` WCF interface currently only exposes:
```csharp
IAsyncResult BeginGetVideoEncoderConfigurations(...)
Message EndGetVideoEncoderConfigurations(IAsyncResult)
```

It is missing the `GetVideoEncoderConfigurationOptions` operation. Per the ONVIF Media2 WSDL, this operation takes an optional `ProfileToken` and `ConfigurationToken` and returns a response with `VideoEncoder2ConfigurationOptions` elements containing H265-specific fields (`GovLengthRange`, `FrameRateRange`, `ResolutionsAvailable`, `BitrateRange`) that do not exist in the Media1 options schema.

### 2. Missing `GetVideoEncoderConfigurationOptionsMedia2` in INvtSession / NvtSession

`onvif\onvif.session\NvtSession.fs` — `INvtSession` interface has:
```fsharp
abstract GetVideoEncoderConfigurationsMedia2: unit -> Async<VideoEncoderConfiguration[]>
```
but no `GetVideoEncoderConfigurationOptionsMedia2` method. The implementation class also lacks it.

### 3. VideoSettingsActivity.fs — load() uses only Media1 options

`odm\odm.ui.activities\VideoSettingsActivity.fs` — `load()` calls:
```fsharp
let! options = session.GetVideoEncoderConfigurationOptions(vec.token, profile.token)
```
This is a Media1 call. For Media2-only H265 cameras, `options.h265` is typically null in the Media1 response. There is no fallback to a Media2 options call.

### 4. VideoSettingsActivity.fs — apply_changes() has a leaky hack

`odm\odm.ui.activities\VideoSettingsActivity.fs` (~line 311):
```fsharp
|VideoEncoding.h265 ->
    if isMedia2OnlyH265 then
        validateConfig(options.h264)   // wrong: clips against H264 ranges
    else
        validateConfig(options.h265)
```
Using `options.h264` to validate H265 config is wrong — it clips frame rate, resolution, bitrate against H264 ranges. The actual H265 options are never fetched.

---

## Required Changes

### TASK AREA 1 — Media2 Options in WCF Interface

**File:** `onvif\onvif.services\onvif.services.cs`

Add `GetVideoEncoderConfigurationOptions` operation to `IMedia2`:
```csharp
[OperationContract(AsyncPattern = true,
    Action = "http://www.onvif.org/ver20/media/wsdl/GetVideoEncoderConfigurationOptions",
    ReplyAction = "*")]
IAsyncResult BeginGetVideoEncoderConfigurationOptions(
    Media2GetVideoEncoderConfigurationOptionsRequest request,
    AsyncCallback callback, object asyncState);
System.ServiceModel.Channels.Message EndGetVideoEncoderConfigurationOptions(
    IAsyncResult result);
```

Add request type:
```csharp
[MessageContract(WrapperName = "GetVideoEncoderConfigurationOptions",
    WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl", IsWrapped = true)]
public partial class Media2GetVideoEncoderConfigurationOptionsRequest {
    [MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 0)]
    public string ProfileToken;
    [MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 1)]
    public string ConfigurationToken;
}
```

The response is returned as a raw `Message` (same pattern as `GetVideoEncoderConfigurations`) so WCF never tries to deserialize it — parse XML manually.

### TASK AREA 2 — Media2 Options Response Model

The Media2 options response XML (`GetVideoEncoderConfigurationOptionsResponse`) contains `Options` elements per encoder type. Key schema (tr2 namespace `http://www.onvif.org/ver20/media/wsdl`, tt namespace `http://www.onvif.org/ver10/schema`):

```xml
<tr2:Options token="..." >
  <tt:Encoding>H265</tt:Encoding>
  <tt:ResolutionsAvailable><tt:Width>1920</tt:Width><tt:Height>1080</tt:Height></tt:ResolutionsAvailable>
  <tt:GovLengthRange><tt:Min>1</tt:Min><tt:Max>120</tt:Max></tt:GovLengthRange>
  <tt:FrameRateRange><tt:Min>1</tt:Min><tt:Max>30</tt:Max></tt:FrameRateRange>
  <tt:BitrateRange><tt:Min>64</tt:Min><tt:Max>8000</tt:Max></tt:BitrateRange>
</tr2:Options>
```

Add a parsed result type (in `onvif.services.cs` or a companion file):
```csharp
public class Media2EncoderOptions {
    public string Encoding;                        // "H264", "H265", "JPEG", "MPEG4"
    public VideoResolution[] ResolutionsAvailable;
    public IntRange GovLengthRange;                // null if not present
    public IntRange FrameRateRange;
    public IntRange BitrateRange;
}
```

### TASK AREA 3 — NvtSession.fs: GetVideoEncoderConfigurationOptionsMedia2

**File:** `onvif\onvif.session\NvtSession.fs`

Add to `INvtSession` interface:
```fsharp
abstract GetVideoEncoderConfigurationOptionsMedia2: configToken:string -> profileToken:string -> Async<Media2EncoderOptions[]>
```

Implement similarly to `GetVideoEncoderConfigurationsMedia2`: call `BeginGetVideoEncoderConfigurationOptions`, receive raw `Message`, parse XML body manually with LINQ to XML.

Return empty array on any error or if Media2 client unavailable (graceful fallback — never throw).

### TASK AREA 4 — VideoSettingsActivity.fs: Use Media2 Options

**File:** `odm\odm.ui.activities\VideoSettingsActivity.fs`

In `load()`:
- After obtaining `effectiveEncoding`, if it is H265 (via Media2 detection), call `GetVideoEncoderConfigurationOptionsMedia2` and use the H265 entries to populate frame rate, bitrate, govLength, and resolution ranges.
- Fall back to Media1 options when Media2 options are unavailable or return no H265 entries.

In `apply_changes()`:
- Remove the `isMedia2OnlyH265 → validateConfig(options.h264)` hack.
- For Media2-detected H265: apply rate/quality fields using ranges from Media2 options (already fetched in load). Preserve `vec.encoding` as Media1 sees it (do not send H265 encoding to a Media1 endpoint).
- govLength: use from Media2 options if available; skip if not.

---

## Testing Requirements

### Unit Tests (odm.tests, no camera required)

1. **XML parsing test:** Given a sample `GetVideoEncoderConfigurationOptionsResponse` XML string, the parser returns correct `Media2EncoderOptions[]` — right encoding enum, resolutions, ranges.
2. **Fallback test:** When Media2 client returns null/empty, `GetVideoEncoderConfigurationOptionsMedia2` returns `[||]` without throwing.
3. **Range coercion test:** `apply_changes` with H265 + Media2 options coerces frame rate to the Media2 H265 range, not the H264 range.

### Integration Tests (camera-dependent, skip when `ODM_TEST_HOST` not set)

- Verify `GetVideoEncoderConfigurationOptionsMedia2` returns non-empty results for a Media2-capable H265 camera.
- Verify returned ranges are reasonable (min < max, positive values).

---

## Out of Scope

- Issue #20: H265 sprop-vps/sps/pps (live555 MediaSubsession API additions) — separate sprint.
- Media2 audio encoder options — not needed.
- Switching `SetVideoEncoderConfiguration` to the Media2 `SetVideoEncoderConfigurations` operation — keep Media1 apply; goal is correct options/ranges.

---

## Build Commands (local, on odm-dev)

```powershell
# Full Release x64 build
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'C:\akhil\git\ONVIF-Device-Manager\odm.sln' /p:Configuration=Release /p:Platform=x64 /v:minimal

# Build test project
dotnet build 'C:\akhil\git\ONVIF-Device-Manager\odm\odm.tests\odm.tests.csproj' -v quiet

# Run offline tests only
& "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\akhil\git\ONVIF-Device-Manager\odm\odm.tests\bin\Debug\net48\odm.tests.dll" --TestCaseFilter:"TestCategory!=Integration"
```

## Key Files

```
onvif\onvif.services\onvif.services.cs             — IMedia2 WCF interface + request/response types
onvif\onvif.session\NvtSession.fs                  — INvtSession interface + implementation
odm\odm.ui.activities\VideoSettingsActivity.fs     — load() + apply_changes()
odm\odm.tests\                                     — unit + integration tests
```
