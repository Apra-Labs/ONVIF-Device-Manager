# Media2 Testing

## Test files

| File | Category | Description |
|------|----------|-------------|
| `odm/odm.tests/Media2IntegrationTests.cs` | Integration | Behavioral tests against a real camera using `INvtSession.GetVideoEncoderConfigurationsMedia2` |

Integration tests require the `ODM_TEST_HOST` environment variable. They are skipped in CI via `Assert.Inconclusive` when that variable is absent.

## Running offline tests

```
dotnet build odm\odm.tests\odm.tests.csproj -v quiet
vstest.console.exe odm\odm.tests\bin\Debug\net48\odm.tests.dll --TestCaseFilter:"TestCategory!=Integration"
```

No camera required.

## Running integration tests

Set `ODM_TEST_HOST` to the camera's IP address (and optionally `ODM_TEST_USER` / `ODM_TEST_PASS`), then run all tests without the filter:

```
set ODM_TEST_HOST=192.168.1.100
set ODM_TEST_USER=admin
set ODM_TEST_PASS=password
vstest.console.exe odm\odm.tests\bin\Debug\net48\odm.tests.dll
```

## What to test

- `GetVideoEncoderConfigurationsMedia2` returns a non-empty array on a Media2-capable camera.
- Encoding strings (`H264`, `H265`, `MPEG4`, `JPEG`) round-trip correctly to `VideoEncoding` enum values.
- A Media1-only camera (no `Media2XAddr` in `GetServices()`) still returns results via the Media1 fallback.

## Adding tests for new Media2 operations

Add integration test methods to `Media2IntegrationTests.cs` using the `[TestCategory("Integration")]` attribute. Test the `INvtSession` surface — not the internal proxy types — so tests stay valid regardless of the underlying WCF implementation.
