# ODM — Media2 Full Support
## Requirements

**Tracking issue:** https://github.com/Apra-Labs/ONVIF-Device-Manager/issues/21
**Base branch:** development
**Sprint branch:** feat/media2-support

---

## Vision

ODM detects whether a connected camera supports the ONVIF Media2 service (`ver20/media/wsdl`). If it does, **all video-related operations route through Media2**. If it does not, ODM falls back to Media1 (unchanged existing behaviour). The GUI, activities, and `INvtSession` interface remain the same — the routing is entirely transparent inside `NvtSession.fs`.

---

## Problem Statement

ODM's video operations are hardwired to the legacy Media1 API (`ver10/media/wsdl`). Modern cameras — especially H265/HEVC models — advertise capabilities only via Media2 and report incorrect or missing data via Media1. The symptoms range from wrong slider ranges on the Video Settings page (confirmed, issue #21) to silent failures when saving encoder config on H265 cameras.

The current codebase has one partial Media2 call (`GetVideoEncoderConfigurationsMedia2`) used only to detect H265 encoding. All other operations use Media1 exclusively.

---

## Architecture

### Detection (once per session, memoized)

`NvtSession.fs` already has `GetMedia2Client` which discovers the Media2 service endpoint via `GetServices()` checking for namespace `http://www.onvif.org/ver20/media/wsdl`. This is the canonical detection mechanism — if `GetMedia2Client()` returns non-null, Media2 is available.

No new detection logic is needed. The existing `GetMedia2Client` is the gate.

### Routing (transparent, per operation)

Each video operation in `NvtSession.fs` becomes:
```fsharp
member this.GetStreamUri(streamSetup, profileToken) = async {
    let! media2 = GetMedia2Client()
    if media2 |> NotNull then
        return! getStreamUriViaMedia2(media2, profileToken)
    else
        return! getStreamUriViaMedia1(streamSetup, profileToken)
}
```

The `INvtSession` interface signatures do not change. Activities call the same methods as today.

### Fallback guarantee

If the Media2 call fails at runtime (network error, camera returns a fault), fall back to the Media1 path. Never surface a Media2-specific error to the caller — graceful degradation always.

---

## Operations to Route Through Media2

All operations below are currently Media1 only. Each needs a Media2 implementation added and the existing Media1 call converted to a fallback.

### 1. GetProfiles

**Currently:** `session.GetProfiles()` → `IMediaAsync.GetProfiles`  
**Used in:** ProfileManagementActivity.fs, and indirectly via GetProfile calls  
**Media2 op:** `tr2:GetProfiles` → returns `tr2:MediaProfile[]`  
**Note:** Media2 profile schema differs from Media1 — includes `VideoEncoderConfiguration` inline. Map to existing `Profile` type or adapt callers minimally.

### 2. GetStreamUri

**Currently:** `session.GetStreamUri(streamSetup, profileToken)` → `IMediaAsync.GetStreamUri`  
**Used in:** VideoPlayerActivity.fs  
**Media2 op:** `tr2:GetStreamUri` with `tr2:GetStreamUriRequest` (profileToken + protocol + stream type)  
**Note:** Media2 returns a plain URI string; Media1 returns `MediaUri` wrapper. The result must be adapted to the existing return type.

### 3. GetVideoEncoderConfigurationOptions

**Currently:** `session.GetVideoEncoderConfigurationOptions(vec.token, profile.token)` → Media1  
**Used in:** VideoSettingsActivity.fs  
**Media2 op:** `tr2:GetVideoEncoderConfigurationOptions` → returns per-encoding options with H265-specific ranges  
**Note:** This is the root cause of issue #21. When Media2 is available, use Media2 options; they contain correct H265 govLength, frameRate, bitrate, resolution ranges that Media1 does not expose.

### 4. SetVideoEncoderConfiguration

**Currently:** `session.SetVideoEncoderConfiguration(vec, true)` → Media1  
**Used in:** VideoSettingsActivity.fs  
**Media2 op:** `tr2:SetVideoEncoderConfigurations` (note: plural) with `tr2:VideoEncoder2Configuration`  
**Note:** Media2 encoder config type differs from Media1. Key fields (resolution, frameRate, bitrate, govLength, encoding) map directly. The `ForcePersistence` flag does not exist in Media2 — ignore it on the Media2 path.

### 5. GetCompatibleVideoEncoderConfigurations

**Currently:** `session.GetCompatibleVideoEncoderConfigurations(profile.token)` → Media1  
**Used in:** ConfigureProfileActivity.fs  
**Media2 op:** `tr2:GetVideoEncoderConfigurations` filtered by profile token (Media2 does not have a separate "compatible" call — all configurations are returned and filtered client-side if needed)

### 6. GetVideoSourceConfigurations

**Currently:** `session.GetVideoSourceConfigurations()` → Media1  
**Used in:** CreateProfileActivity.fs  
**Media2 op:** `tr2:GetVideoSourceConfigurations`

### 7. GetSnapshotUri

**Currently:** wherever snapshot URLs are fetched → Media1 `GetSnapshotUri`  
**Media2 op:** `tr2:GetSnapshotUri` (profile token only, returns URI string)  
**Note:** Audit codebase for snapshot usage and wire up if present.

---

## What Does NOT Change

- `INvtSession` interface signatures — no change
- All activity files — no change (they call the same interface methods)
- GUI/XAML — no change
- Non-video operations (device info, network settings, PTZ, imaging, metadata, events) — Media2 does not cover these; leave on Media1

---

## IMedia2 WCF Interface Additions

**File:** `onvif\onvif.services\onvif.services.cs`

All new operations follow the same pattern as the existing `GetVideoEncoderConfigurations` implementation: use raw `System.ServiceModel.Channels.Message` as the return type so WCF never deserializes the body — parse XML manually in NvtSession.fs.

Operations to add:
- `GetProfiles` (request: optional token filter)
- `GetStreamUri` (request: profileToken, protocol, streamType)
- `GetVideoEncoderConfigurationOptions` (request: optional configToken, profileToken)
- `SetVideoEncoderConfigurations` (request: VideoEncoder2Configuration)
- `GetVideoSourceConfigurations` (request: optional token filter)
- `GetSnapshotUri` (request: profileToken)

---

## XML Parsing / Type Mapping

All Media2 responses are parsed via LINQ to XML (same pattern as existing `GetVideoEncoderConfigurationsMedia2`). Use `XNamespace`:
- tr2: `http://www.onvif.org/ver20/media/wsdl`
- tt: `http://www.onvif.org/ver10/schema`

Results are mapped to the existing ODM domain types (`Profile`, `VideoEncoderConfiguration`, `VideoEncoderConfigurationOptions`, `MediaUri`, etc.) so no activity changes are needed. Where the Media2 schema is richer (e.g. H265 options), populate the existing type's extension fields (`vec.h265`, `options.h265`).

---

## Testing Requirements

### Unit Tests (`odm.tests`, no camera)

1. XML parsing tests for each new Media2 response type — given a sample XML string, parser returns correct domain object
2. Fallback tests — when `GetMedia2Client()` returns null, each operation returns the same result as the Media1 path (mock or stub)
3. `GetVideoEncoderConfigurationOptions` — Media2 path returns correct H265 ranges; H264 path falls back gracefully
4. `SetVideoEncoderConfiguration` — Media2 path maps fields correctly (no ForcePersistence)

### Integration Tests (camera-dependent, skip when `ODM_TEST_HOST` not set)

- `GetMedia2Client()` returns non-null for a known Media2-capable camera
- `GetProfiles` via Media2 returns valid profile list
- `GetStreamUri` via Media2 returns a valid RTSP URI
- `GetVideoEncoderConfigurationOptions` via Media2 returns non-empty options with correct H265 ranges
- `SetVideoEncoderConfiguration` via Media2 applies changes without error

---

## Out of Scope

- Issue #20: H265 sprop-vps/sps/pps (live555 MediaSubsession API additions) — separate sprint
- Media2 audio encoder operations
- Media2 PTZ operations
- Media2 analytics / metadata operations

---

## Build Commands

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
onvif\onvif.services\onvif.services.cs             — IMedia2 WCF interface + request types
onvif\onvif.session\NvtSession.fs                  — INvtSession routing layer (main work)
odm\odm.ui.activities\VideoSettingsActivity.fs     — verify options/apply now use Media2
odm\odm.ui.activities\VideoPlayerActivity.fs       — verify GetStreamUri routes to Media2
odm\odm.ui.activities\ConfigureProfileActivity.fs  — verify GetCompatibleVideoEncoderConfigurations
odm\odm.tests\                                     — unit + integration tests
```
