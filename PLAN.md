# PLAN: Media2 Full Support — Complete Routing Layer

**Tracking issue:** https://github.com/Apra-Labs/ONVIF-Device-Manager/issues/21
**Branch:** feat/media2-support (from development)
**Goal:** When a camera supports Media2, ALL video operations route through Media2 transparently. Media1 becomes the fallback. No changes to INvtSession interface, activities, or GUI.

---

## Architecture Overview

**Detection:** `GetMedia2Client()` (NvtSession.fs:1035-1055) — memoized, checks for `ver20/media/wsdl` namespace in `GetServices()`. Returns non-null if Media2 is available. Already exists; no changes needed.

**Routing pattern:** Each video method in NvtSession.fs gains a Media2 path:
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

**WCF pattern:** All new IMedia2 operations use raw `System.ServiceModel.Channels.Message` return type (same as existing `GetVideoEncoderConfigurations`). XML parsed with LINQ-to-XML in NvtSession.fs. Namespaces: `tr2 = ver20/media/wsdl`, `tt = ver10/schema`.

**Key files:**
- `onvif\onvif.services\onvif.services.cs` — IMedia2 interface + request types (line 634)
- `onvif\onvif.session\NvtSession.fs` — routing layer (lines 1534-1893: IMediaAsync impl)
- `odm\odm.tests\` — unit + integration tests

---

## Phase 1 — IMedia2 WCF Interface Expansion

All 6 new operations added to IMedia2 with their request message contracts. This phase is pure interface — no implementation yet.

### Task 1.1 — Add all IMedia2 operations + request types

- **File:** `onvif\onvif.services\onvif.services.cs`
- **Change:**
  - Add 6 operation pairs to `IMedia2` interface (line 635), each following the existing `BeginGetVideoEncoderConfigurations`/`EndGetVideoEncoderConfigurations` pattern:
    1. `GetProfiles` — Action: `…/GetProfiles`, request: `Media2GetProfilesRequest` (optional `Token` field, optional `Type` field)
    2. `GetStreamUri` — Action: `…/GetStreamUri`, request: `Media2GetStreamUriRequest` (`ProfileToken`, `Protocol`)
    3. `GetVideoEncoderConfigurationOptions` — Action: `…/GetVideoEncoderConfigurationOptions`, request: `Media2GetVideoEncoderConfigurationOptionsRequest` (optional `ConfigurationToken`, optional `ProfileToken`)
    4. `SetVideoEncoderConfigurations` — Action: `…/SetVideoEncoderConfigurations`, request: `Media2SetVideoEncoderConfigurationsRequest` (raw XML body via `XElement Configuration`)
    5. `GetVideoSourceConfigurations` — Action: `…/GetVideoSourceConfigurations`, request: `Media2GetVideoSourceConfigurationsRequest` (optional `ConfigurationToken`, optional `ProfileToken`)
    6. `GetSnapshotUri` — Action: `…/GetSnapshotUri`, request: `Media2GetSnapshotUriRequest` (`ProfileToken`)
  - All End methods return `System.ServiceModel.Channels.Message`
  - All request classes use `[MessageContract(WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl")]`
  - Request fields use `[MessageBodyMember]` attribute
- **Done when:** `odm.sln` builds Release x64. IMedia2 has 7 operation pairs (1 existing + 6 new).
- **Could block:** WCF action URL typo — verify against ONVIF Media2 WSDL spec.
- **Tier:** standard

### Task 1.2 — Add Media2EncoderOptions parsed result type

- **File:** `onvif\onvif.services\onvif.services.cs`
- **Change:**
  - Add `Media2EncoderOptions` class: `Encoding` (string), `ResolutionsAvailable` (VideoResolution[]), `GovLengthRange` (IntRange, nullable), `FrameRateRange` (IntRange), `BitrateRange` (IntRange)
  - Plain C# class, no WCF attributes — populated by LINQ-to-XML parser
- **Done when:** Builds. Type usable from F#.
- **Could block:** Nothing — pure data class.
- **Tier:** cheap

### VERIFY 1
- [ ] `odm.sln` builds Release x64 with no errors
- [ ] IMedia2 has 7 operation pairs
- [ ] All 6 request types + Media2EncoderOptions exist and compile
- [ ] No changes to INvtSession interface or activity files

---

## Phase 2 — Foundation: GetProfiles + GetStreamUri (Read-Path Core)

These two operations are the most widely used and form the foundation — profiles are needed by every activity, and stream URI is needed for video playback. Implementing them first validates the routing pattern end-to-end.

### Task 2.1 — GetProfiles Media2 routing

- **File:** `onvif\onvif.session\NvtSession.fs`
- **Change:**
  - Add private helper `getProfilesViaMedia2(media2: IMedia2): Async<Profile[]>`:
    1. Create `Media2GetProfilesRequest` (no filters — get all profiles)
    2. `Async.FromBeginEnd` → raw Message
    3. Parse `<tr2:Profiles>` elements from response XML
    4. Map to `Profile` objects: extract `token` (attribute), `name`, `VideoSourceConfiguration` (nested `tt:VideoSourceConfiguration`), `VideoEncoderConfiguration` (nested `tt:VideoEncoderConfiguration` with `Encoding`, `Resolution`, `RateControl`)
    5. Media2 profiles embed configurations inline — parse them into the existing `Profile` type's fields
  - Modify `GetProfiles()` implementation (line 1546-1552):
    - Check `GetMedia2Client()` first
    - If non-null, call `getProfilesViaMedia2`; on failure, fall back to Media1
    - If null, use existing Media1 path
- **Done when:** Builds. Unit test for profile XML parsing passes.
- **Could block:** Media2 profile schema is richer than Media1 — must map all fields that activities actually read (token, name, videoSourceConfiguration, videoEncoderConfiguration). Fields not in Media1 are ignored.
- **Tier:** standard

### Task 2.2 — GetStreamUri Media2 routing

- **File:** `onvif\onvif.session\NvtSession.fs`
- **Change:**
  - Add private helper `getStreamUriViaMedia2(media2: IMedia2, profileToken: string): Async<MediaUri>`:
    1. Create `Media2GetStreamUriRequest` with `ProfileToken` and `Protocol = "RtspUnicast"`
    2. `Async.FromBeginEnd` → raw Message
    3. Parse `<tr2:Uri>` from response body — Media2 returns a plain URI string
    4. Wrap in `MediaUri` object (set `.uri` field), apply `FixUrl()` for relative URI handling
  - Modify `GetStreamUri()` implementation (line 1564-1571):
    - Check `GetMedia2Client()` first
    - If non-null, call `getStreamUriViaMedia2`; on failure, fall back to Media1
    - If null, use existing Media1 path (unchanged)
  - Note: Media1 takes `StreamSetup` + `profileToken`; Media2 takes `profileToken` + `Protocol`. The `StreamSetup.transport.protocol` maps to the Media2 `Protocol` field.
- **Done when:** Builds. Unit test for stream URI XML parsing passes.
- **Could block:** Some cameras may return relative URIs — `FixUrl()` already handles this (line 1568).
- **Tier:** standard

### Task 2.3 — Unit tests: GetProfiles + GetStreamUri XML parsing

- **File:** `odm\odm.tests\Media2XmlParserTests.cs` (new)
- **Change:**
  - Extract XML parsing helpers as static methods for testability
  - Test 1: GetProfiles response XML with 2 profiles (one H264, one H265) → parser returns correct Profile[] with tokens, names, and embedded encoder configurations
  - Test 2: GetProfiles response with no profiles → returns empty array
  - Test 3: GetStreamUri response XML → parser extracts URI string correctly
  - Test 4: GetStreamUri response with empty URI → returns MediaUri with null uri
- **Done when:** `dotnet test` passes all 4 tests.
- **Tier:** standard

### VERIFY 2
- [ ] `odm.sln` builds Release x64
- [ ] All unit tests pass (`dotnet test`)
- [ ] `GetProfiles` and `GetStreamUri` route through Media2 when available
- [ ] Fallback to Media1 verified (parser gracefully returns on error)
- [ ] No changes to INvtSession interface or activity files

---

## Phase 3 — Video Settings: GetVideoEncoderConfigurationOptions + SetVideoEncoderConfiguration

This is the root cause of issue #21. These two operations complete the video settings workflow: load correct ranges, then apply with correct parameters.

### Task 3.1 — GetVideoEncoderConfigurationOptions Media2 routing

- **File:** `onvif\onvif.session\NvtSession.fs`
- **Change:**
  - Add private helper `getVideoEncoderConfigurationOptionsViaMedia2(media2: IMedia2, configToken: string, profileToken: string): Async<VideoEncoderConfigurationOptions>`:
    1. Create `Media2GetVideoEncoderConfigurationOptionsRequest` with configToken/profileToken
    2. `Async.FromBeginEnd` → raw Message → XDocument parse
    3. Parse `<tr2:Options>` elements: each has `<tt:Encoding>`, `<tt:ResolutionsAvailable>` (Width/Height), `<tt:GovLengthRange>` (Min/Max), `<tt:FrameRateRange>` (Min/Max), `<tt:BitrateRange>` (Min/Max)
    4. Map to `VideoEncoderConfigurationOptions`: populate `.h264`, `.h265`, `.jpeg` sub-objects based on encoding type. H265 options populate `options.h265` with correct ranges — this is what fixes issue #21
    5. Return `VideoEncoderConfigurationOptions` (same type as Media1 returns)
  - Modify `GetVideoEncoderConfigurationOptions()` implementation (line 1855-1858):
    - Check `GetMedia2Client()` first
    - If non-null, call Media2 helper; on failure, fall back to Media1
    - If null, use existing Media1 path
- **Done when:** Builds. H265 camera returns correct ranges via Media2.
- **Could block:** Mapping Media2 per-encoding options into the single `VideoEncoderConfigurationOptions` type requires populating the right sub-object (h264/h265/jpeg). Verify field layout.
- **Tier:** standard

### Task 3.2 — SetVideoEncoderConfiguration Media2 routing

- **File:** `onvif\onvif.session\NvtSession.fs`
- **Change:**
  - Add private helper `setVideoEncoderConfigurationViaMedia2(media2: IMedia2, config: VideoEncoderConfiguration): Async<unit>`:
    1. Build XML request body for `tr2:SetVideoEncoderConfigurations` containing `<tt:Configuration>` with: token (attribute), name, encoding, resolution (Width/Height), rateControl (FrameRateLimit, BitrateLimit), govLength, quality, profile
    2. For H265 encoding: include H265-specific fields (govLength from `config.h265`)
    3. `ForcePersistence` is NOT sent — Media2 does not support it
    4. `Async.FromBeginEnd` → verify response (no body expected)
  - Modify `SetVideoEncoderConfiguration()` implementation (line 1825-1828):
    - Check `GetMedia2Client()` first
    - If non-null, call Media2 helper; on failure, fall back to Media1
    - If null, use existing Media1 path
- **Done when:** Builds. Applying settings on H265 camera succeeds via Media2.
- **Could block:** The request body must be constructed as raw XML (not WCF serialization). Follow the same manual XML construction approach used for other raw-Message patterns.
- **Tier:** standard

### Task 3.3 — Unit tests: Options parsing + Set request construction

- **File:** `odm\odm.tests\Media2XmlParserTests.cs` (append to existing)
- **Change:**
  - Test 5: GetVideoEncoderConfigurationOptions response with H265 + H264 options → parser populates `options.h265` and `options.h264` with correct ranges
  - Test 6: Options response with missing GovLengthRange → field is null, no exception
  - Test 7: Options response with empty body → returns default options object, no exception
  - Test 8: SetVideoEncoderConfiguration XML construction → verify output XML has correct structure, token, encoding, resolution, rateControl fields
- **Done when:** All tests pass.
- **Tier:** standard

### VERIFY 3
- [ ] `odm.sln` builds Release x64
- [ ] All unit tests pass
- [ ] H265 camera: Video Settings page shows correct slider ranges (issue #21 fix verified)
- [ ] H265 camera: Apply settings succeeds via Media2
- [ ] H264 camera: behavior unchanged (Media2 or Media1 — both work)
- [ ] No changes to INvtSession interface, VideoSettingsActivity.fs, or GUI

---

## Phase 4 — Profile Configuration: GetCompatibleVideoEncoderConfigurations + GetVideoSourceConfigurations

These operations support profile creation and configuration workflows.

### Task 4.1 — GetCompatibleVideoEncoderConfigurations Media2 routing

- **File:** `onvif\onvif.session\NvtSession.fs`
- **Change:**
  - Add private helper `getCompatibleVideoEncoderConfigurationsViaMedia2(media2: IMedia2, profileToken: string): Async<VideoEncoderConfiguration[]>`:
    1. Create `Media2GetVideoEncoderConfigurationsRequest` — reuse the existing request type but add optional `ProfileToken` and `ConfigurationToken` fields to it (or use the request from Task 1.1 that includes these fields)
    2. Note: Media2 has no separate "compatible" endpoint — `GetVideoEncoderConfigurations` with a profileToken filter returns the relevant configurations
    3. `Async.FromBeginEnd` → raw Message → XDocument parse
    4. Parse `<tr2:Configurations>` elements (same pattern as existing `GetVideoEncoderConfigurationsMedia2`, line 1187-1230, but now fully parse all fields: token, name, encoding, resolution, rateControl, quality, govLength, h264/h265 profile)
    5. Return `VideoEncoderConfiguration[]`
  - Modify `GetCompatibleVideoEncoderConfigurations()` implementation (line 1762-1772):
    - Check `GetMedia2Client()` first
    - If non-null, call Media2 helper; on failure, fall back to Media1
    - If null, use existing Media1 path (which already has try/catch fallback to `GetVideoEncoderConfigurations`)
  - Also update the existing `GetVideoEncoderConfigurations()` (line 1664-1677) to route through Media2 using the same helper — this ensures the enhanced `GetVideoEncoderConfigurationsMedia2` (line 1187-1230) pattern is replaced by the full-field parser
- **Done when:** Builds. ConfigureProfileActivity gets full encoder configs via Media2.
- **Could block:** The existing `GetVideoEncoderConfigurationsMedia2` (line 1187) only parses token+encoding. The new parser must parse all fields. Consider refactoring to share the full parser.
- **Tier:** standard

### Task 4.2 — GetVideoSourceConfigurations Media2 routing

- **File:** `onvif\onvif.session\NvtSession.fs`
- **Change:**
  - Add private helper `getVideoSourceConfigurationsViaMedia2(media2: IMedia2): Async<VideoSourceConfiguration[]>`:
    1. Create `Media2GetVideoSourceConfigurationsRequest` (no filters)
    2. `Async.FromBeginEnd` → raw Message → XDocument parse
    3. Parse `<tr2:Configurations>` elements: extract `token` (attribute), `name`, `sourceToken`, `bounds` (IntRectangle)
    4. Map to `VideoSourceConfiguration[]`
  - Modify `GetVideoSourceConfigurations()` implementation (line 1656-1662):
    - Check `GetMedia2Client()` first
    - If non-null, call Media2 helper; on failure, fall back to Media1
    - If null, use existing Media1 path
- **Done when:** Builds. CreateProfileActivity gets source configs via Media2.
- **Could block:** Minimal — VideoSourceConfiguration is a simple type.
- **Tier:** cheap

### Task 4.3 — Unit tests: Encoder configs + source configs parsing

- **File:** `odm\odm.tests\Media2XmlParserTests.cs` (append)
- **Change:**
  - Test 9: GetVideoEncoderConfigurations response with full fields → parser returns VideoEncoderConfiguration[] with all fields populated (encoding, resolution, rateControl, govLength, h265 profile)
  - Test 10: GetVideoSourceConfigurations response → parser returns VideoSourceConfiguration[] with token, name, sourceToken, bounds
  - Test 11: Empty responses → empty arrays, no exceptions
- **Done when:** All tests pass.
- **Tier:** standard

### VERIFY 4
- [ ] `odm.sln` builds Release x64
- [ ] All unit tests pass
- [ ] ConfigureProfileActivity: compatible encoder configs include H265 entries
- [ ] CreateProfileActivity: video source configs load correctly
- [ ] No changes to INvtSession interface or activity files

---

## Phase 5 — GetSnapshotUri + Cleanup + Integration Tests

### Task 5.1 — GetSnapshotUri Media2 routing

- **File:** `onvif\onvif.session\NvtSession.fs`
- **Change:**
  - Add private helper `getSnapshotUriViaMedia2(media2: IMedia2, profileToken: string): Async<MediaUri>`:
    1. Create `Media2GetSnapshotUriRequest` with `ProfileToken`
    2. `Async.FromBeginEnd` → raw Message → XDocument parse
    3. Parse `<tr2:Uri>` — Media2 returns plain URI string (same as GetStreamUri pattern)
    4. Wrap in `MediaUri`, apply `FixUrl()`
  - Modify `GetSnapshotUri()` implementation (line 1573-1584):
    - Check `GetMedia2Client()` first
    - If non-null, call Media2 helper; on failure, fall back to Media1
    - If null, use existing Media1 path
  - Usage confirmed: `OdmSession.fs` line 698 calls `session.GetSnapshotUri(profileToken)`
- **Done when:** Builds. Snapshot captures use Media2 URI when available.
- **Could block:** Some cameras may not support snapshot via Media2 — fallback handles this.
- **Tier:** cheap

### Task 5.2 — Retire GetVideoEncoderConfigurationsMedia2 INvtSession member

- **File:** `onvif\onvif.session\NvtSession.fs`
- **Change:**
  - The `abstract GetVideoEncoderConfigurationsMedia2: unit -> Async<VideoEncoderConfiguration[]>` member on INvtSession (line 88) was a temporary bridge. After Phase 4, `GetVideoEncoderConfigurations()` itself routes through Media2.
  - Audit all callers of `GetVideoEncoderConfigurationsMedia2`:
    - `VideoSettingsActivity.fs` — uses it to detect H265 encoding
    - Any other callers found in audit
  - Replace callers with the standard `GetVideoEncoderConfigurations()` which now returns H265 configs via Media2 routing
  - Remove the abstract member from INvtSession and its implementation
  - This simplifies the interface — no more separate Media2-specific methods on the public API
- **Done when:** Builds. `GetVideoEncoderConfigurationsMedia2` no longer exists. All callers use the standard routed method.
- **Could block:** Must verify all callers are migrated. Grep for `GetVideoEncoderConfigurationsMedia2` across entire codebase.
- **Tier:** standard

### Task 5.3 — Integration tests

- **File:** `odm\odm.tests\Media2IntegrationTests.cs` (new)
- **Change:**
  - Guard with `[TestCategory("Integration")]` — skipped when `ODM_TEST_HOST` not set
  - Test 1: `GetMedia2Client()` returns non-null for Media2-capable camera
  - Test 2: `GetProfiles()` returns valid profiles with H265 encoder configs
  - Test 3: `GetStreamUri()` returns valid RTSP URI
  - Test 4: `GetVideoEncoderConfigurationOptions()` returns H265 options with correct ranges
  - Test 5: `SetVideoEncoderConfiguration()` applies H265 config without error
  - Test 6: `GetVideoSourceConfigurations()` returns non-empty configs
  - Test 7: `GetSnapshotUri()` returns valid URI (if camera supports it)
- **Done when:** Tests pass against Media2-capable camera, skip cleanly otherwise.
- **Tier:** standard

### VERIFY 5
- [ ] `odm.sln` builds Release x64
- [ ] All unit tests pass
- [ ] All integration tests pass (with camera) or skip cleanly (without)
- [ ] `GetVideoEncoderConfigurationsMedia2` removed from INvtSession
- [ ] No INvtSession interface changes (net result: member removed, all operations now route internally)
- [ ] Full manual test: connect to H265 camera → profiles load → video plays → settings show correct ranges → apply succeeds → snapshot works

---

## Risk Register

| Risk | Impact | Mitigation | Phase |
|------|--------|------------|-------|
| XML namespace differences across camera vendors | Media2 parsing fails silently | Parse with namespace-aware XName (`nsTr2`, `nsTt`); test with multiple vendor XML samples; graceful fallback on parse error | 2, 3, 4 |
| Media2 profile schema mapping lossy | Activities see incomplete profile data | Map all fields that activities actually read (audited in research); log warnings for unmapped fields | 2 |
| SetVideoEncoderConfigurations raw XML body rejected | Camera rejects the apply request | Construct XML matching ONVIF spec exactly; test against real camera early; fallback to Media1 on failure | 3 |
| GetMedia2Client() returns non-null but individual operations fail | One operation fails, others succeed | Per-operation try/catch with fallback — each operation falls back independently, not all-or-nothing | All |
| Retiring GetVideoEncoderConfigurationsMedia2 breaks callers | Build failure or runtime regression | Grep entire codebase for callers before removal; phase it last (Phase 5) after routing layer is proven | 5 |
| WCF channel state corruption from mixed Media1/Media2 calls | Intermittent failures | Media1 and Media2 use separate channel factories (getMediaFactory vs getMedia2Factory, lines 339/421) — no shared state | All |
| Performance regression from Media2 detection on every call | Slow video operations | `GetMedia2Client()` is already memoized (Async.Memoize at line 1036) — detection cost is paid once per session | All |

---

## Out of Scope

- Issue #20: H265 sprop-vps/sps/pps (live555 MediaSubsession API additions) — separate sprint
- Media2 audio encoder operations
- Media2 PTZ operations
- Media2 analytics / metadata operations
- Changes to INvtSession interface signatures (routing is transparent)
- Changes to activity files or GUI/XAML
