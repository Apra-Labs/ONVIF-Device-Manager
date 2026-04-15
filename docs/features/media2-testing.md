# Media2 Testing

## Test Files

| File | Type | Count |
|------|------|-------|
| `odm/odm.tests/Media2XmlParserTests.cs` | Offline unit tests | 11 |
| `odm/odm.tests/Media2IntegrationTests.cs` | Integration tests (require camera) | 7 |

---

## Running Unit Tests (Offline)

Unit tests have no external dependencies. They exercise `Media2XmlParser` directly
with inline XML strings.

```bat
dotnet build odm\odm.tests\odm.tests.csproj -v quiet

"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" ^
  odm\odm.tests\bin\Debug\net48\odm.tests.dll ^
  --TestCaseFilter:"TestCategory!=Integration"
```

All 80 tests (including Media2 unit tests) should pass.

---

## Running Integration Tests (Live Camera)

Integration tests require a real ONVIF camera that supports Media2 (`ver20/media/wsdl`).

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `ODM_TEST_HOST` | Yes | — | Camera IP or hostname (e.g., `192.168.1.100`) |
| `ODM_TEST_USER` | No | `""` | Username for authenticated access |
| `ODM_TEST_PASS` | No | `""` | Password for authenticated access |

If `ODM_TEST_HOST` is not set, all integration tests call `Assert.Inconclusive` and
skip gracefully. The offline CI run uses `TestCaseFilter:"TestCategory!=Integration"`
to exclude them entirely.

### Running Against a Camera

```bat
set ODM_TEST_HOST=192.168.1.100
set ODM_TEST_USER=admin
set ODM_TEST_PASS=password

"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" ^
  odm\odm.tests\bin\Debug\net48\odm.tests.dll ^
  --TestCaseFilter:"TestCategory=Integration"
```

---

## What Each Test Covers

### Unit Tests (Media2XmlParserTests.cs)

| Test | What it verifies |
|------|-----------------|
| 1. `ParseGetProfilesResponse_TwoProfiles_*` | Two-profile response (H264 + H265): tokens, names, embedded VSC and VEC fields, h264/h265 sub-blocks |
| 2. `ParseGetProfilesResponse_NoProfiles_*` | Empty response body → empty array, no exception |
| 3. `ParseGetStreamUriResponse_ValidUri_*` | Valid `tr2:Uri` element → URI string extracted correctly |
| 4. `ParseGetStreamUriResponse_EmptyUri_*` | Empty `tr2:Uri` element → returns null |
| 5. `ParseGetVideoEncoderConfigurationOptionsResponse_H265AndH264_*` | Per-encoding options blocks → `options.h265` and `options.h264` populated with correct resolution/range values; `options.jpeg` remains null |
| 6. `ParseGetVideoEncoderConfigurationOptionsResponse_MissingGovLengthRange_*` | Absent `GovLengthRange` element → field is null (not default-initialised), no exception |
| 7. `ParseGetVideoEncoderConfigurationOptionsResponse_EmptyBody_*` | No `tr2:Options` elements → all sub-objects null, no exception |
| 8. `BuildSetVideoEncoderConfigurationElement_H265Config_*` | H265 VEC → XML has correct token, Name, Encoding, Resolution, RateControl, H265 block; H264 block absent |
| 9. `ParseGetVideoEncoderConfigurationsResponse_FullFields_*` | Two configs (H265 + H264): all fields (token, name, encoding, resolution, rateControl, govLength, h265/h264 block); exclusive h265/h264 presence |
| 10. `ParseGetVideoSourceConfigurationsResponse_ValidResponse_*` | Two VSCs: token, name, sourceToken, bounds (x/y/width/height) |
| 11. `ParseGetVideoEncoder/SourceConfigurationsResponse_EmptyBody_*` | Both parsers return empty arrays for empty responses, no exception |

### Integration Tests (Media2IntegrationTests.cs)

| Test | What it verifies |
|------|-----------------|
| 1. `GetProfiles_Media2Camera_ReturnsNonEmptyProfiles` | Session returns at least one profile; all profiles have non-empty tokens |
| 2. `GetStreamUri_FirstProfile_ReturnsRtspUri` | Stream URI is non-empty and starts with `rtsp://` |
| 3. `GetVideoEncoderConfigurationOptions_FirstProfile_ReturnsOptions` | Options object is non-null for the first profile's encoder configuration |
| 4. `GetVideoEncoderConfigurations_Media2Camera_ReturnsConfigurations` | At least one configuration returned; all have non-empty tokens |
| 5. `GetVideoSourceConfigurations_Media2Camera_ReturnsConfigurations` | At least one source configuration; all have non-empty token and sourceToken |
| 6. `GetSnapshotUri_FirstProfile_ReturnsUriOrNull` | URI is HTTP(S) if returned; null or exception → marked Inconclusive (not a failure) |
| 7. `GetCompatibleVideoEncoderConfigurations_FirstProfile_ReturnsConfigurations` | No exception thrown; empty result is acceptable for some profiles |

---

## Adding New Integration Tests

1. Add a `[TestMethod]` inside `Media2IntegrationTests.cs`.
2. Tag it `[TestCategory("Integration")]`.
3. Call `SkipIfNoHost()` as the first statement.
4. Use `CreateSession()` to get an `INvtSession` against the live camera.
5. Use `Run(async)` to execute F# async workflows synchronously.

Example skeleton:

```csharp
[TestMethod]
[TestCategory("Integration")]
public void GetFoo_FirstProfile_ReturnsBar()
{
    SkipIfNoHost();
    var session = CreateSession();
    var profiles = Run(session.GetProfiles());
    if (profiles == null || profiles.Length == 0)
        Assert.Inconclusive("No profiles — cannot test GetFoo");

    var result = Run(session.GetFoo(profiles[0].token));
    Assert.IsNotNull(result);
    // ... specific assertions
}
```

---

## Adding New Unit Tests

All XML parser tests live in `Media2XmlParserTests.cs`. Each test:

1. Defines a literal XML string matching the WCF body format
   (`response.GetReaderAtBodyContents().ReadOuterXml()`).
2. Calls the relevant static method on `Media2XmlParser`.
3. Asserts on the returned ODM type.

The XML must include the `tr2` and `tt` namespace declarations on the root element,
as the parser uses namespace-qualified `XName` lookups.

Tests should be tagged `[TestCategory("Unit")]` and must not depend on any external
resources or environment variables.
