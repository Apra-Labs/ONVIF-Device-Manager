# ODM End-to-End Test Suite — Local Run Guide

This document is the authoritative, step-by-step procedure for taking a fresh checkout of `ODM-Test` to a green run of the end-to-end (e2e) UI test suite on a Windows workstation. It is written for an AI agent or engineer who has never touched the suite before.

If a step here disagrees with reality, fix the doc — do not invent workarounds.

---

## 1. Overview

The e2e suite drives the real ODM (ONVIF Device Manager) WPF application via UI Automation (FlaUI / UIA3) and exercises real cameras on the local network using ONVIF WS-Discovery, HTTP and HTTPS.

- **Test project**: `odm/odm.e2e-tests/` — old-style csproj, `.NET Framework 4.8`, MSTest v1.4.0, FlaUI 4.0.0.
- **Helper unit-test project**: `odm/odm.tests/` — SDK-style csproj, runs in CI (`.github/workflows/odm.yml`). Not the e2e suite, but the build path it exercises is required.
- **Test runner**: `vstest.console.exe` from VS 2022 BuildTools.
- **Application under test**: `build\odm.exe` (produced by `package.bat` after building `odm.ui.app`).

The suite is **camera-type-aware**: it discovers cameras via ONVIF at runtime, classifies them by manufacturer/model, and dispatches the appropriate test suites per camera type. If a camera type is not present on the network, its tests are skipped (`Assert.Inconclusive`) — never failed.

### Why elevation matters

ODM uses WS-Discovery (UDP multicast on port 3702). On Windows, multicast subscription requires an elevated process or it returns zero devices. The test runner must therefore launch ODM in an elevated context.

The way this is achieved on the lab workstation is **Windows Task Scheduler**: a task `\RunE2ETest` runs `C:\odm-e2e-results\run-e2e.bat` with the "Run with highest privileges" flag set. Agents trigger it via `schtasks /run /tn \RunE2ETest`. Running `vstest.console.exe` directly from a non-elevated shell will produce `Discovery found 0 cameras` failures.

A companion task `\LaunchODM` exists to start ODM elevated for manual exploration; it is not required for the test run.

### Where artifacts land

| Artifact | Path |
|---|---|
| Test logs | `C:\odm-e2e-results\reports\run-YYYYMMDD-HHMMSS.log` |
| TRX result files | `C:\ak\ODM-Test\odm\odm.e2e-tests\bin\Release\TestResults\*.trx` |
| Screenshots | `C:\odm-e2e-results\screenshots\` |
| Smoke config | `C:\odm-e2e-results\smoke-config.json` |

---

## 2. Architecture (Brief)

### Camera-type-aware dispatch

On test-suite startup, before any UI tests run, the harness performs ONVIF WS-Discovery to find every camera on the local network. Each discovered device is queried for its `DeviceInfo` (manufacturer, model, firmware) and classified into a camera profile from `smoke-config.json`. Tests then dispatch per-camera-type.

Discovery flow:

```
1. Send WS-Discovery probe (multicast to 239.255.255.250:3702)
2. Collect all responding ONVIF endpoints
3. For each endpoint, call GetDeviceInformation()
4. Match manufacturer/model against cameraProfiles in smoke-config.json
5. Group cameras by classifyAs type
6. Dispatch test suites per camera group
```

Classification rules (evaluated in order, first match wins):

| Manufacturer/Model match | classifyAs | Transport | Suites |
|---|---|---|---|
| Manufacturer contains `Milesight` | `milesight` | HTTPS (443) | HttpsTests, LiveVideoTests, DiscoveryTests |
| Manufacturer contains `Apra` OR model contains `Virtual` | `virtual` | HTTP (80) | DiscoveryTests, LiveVideoTests |
| Manufacturer contains `AXIS` | `axis` | HTTP (80) | DiscoveryTests, LiveVideoTests |
| No match | `unknown` | HTTP (80) | DiscoveryTests only |

If a test class needs a camera type that was not discovered, it calls `Assert.Inconclusive(...)` so the run reports **Skipped**, not **Failed**.

### Project layout

```
odm/odm.e2e-tests/
  odm.e2e-tests.csproj     # .NET 4.8 class library, MSTest + FlaUI.UIA3
  Config/
    SmokeConfig.cs         # Loads smoke-config.json, resolves $env: vars
  Discovery/
    OnvifDiscovery.cs      # WS-Discovery probe + GetDeviceInformation
    CameraDispatcher.cs    # Match discovered cameras to config profiles
    DiscoveredCamera.cs    # Endpoint + device info + matched profile
  Helpers/
    OdmApp.cs              # App lifecycle: launch, attach, close
    WaitHelpers.cs         # Progress bar / spinner wait logic
    ScreenshotCapture.cs   # Capture + save screenshot
    ClaudeAnalyzer.cs      # Send screenshot to Claude API, parse response
  Tests/
    SmokeTestBase.cs       # Base class: config, camera list, shared helpers
    DiscoveryTests.cs      # Per camera type
    LiveVideoTests.cs      # Per camera type
    HttpsTests.cs          # Milesight (and any profile listing HttpsTests)
    ...
```

The codebase is the source of truth for the current set of test classes; consult `odm/odm.e2e-tests/Tests/` for what actually exists.

---

## 3. Prerequisites

Install once per machine.

### 3.1 Visual Studio 2022 BuildTools

Required workloads:

- **Desktop development with C++** (`Microsoft.VisualStudio.Workload.VCTools`) — provides `cl.exe`, the Windows SDK, and the `v143` platform toolset needed by `odm.player.lib` and `live555`.
- **.NET desktop build tools** (`Microsoft.VisualStudio.Workload.ManagedDesktop` equivalent for BuildTools) — provides MSBuild for managed projects and `vstest.console.exe`.

Verify `vstest.console.exe` exists at:

```
C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe
```

That exact path is what `run-e2e.bat` uses.

### 3.2 .NET 4.0 and 4.5 targeting packs

VS 2022 BuildTools no longer ships these. They must be installed from NuGet and copied into the system reference-assembly tree. The CI workflow `.github/workflows/odm.yml` is the canonical recipe; the equivalent local commands are:

```powershell
$nuget = "nuget"
$outDir = "$env:TEMP\netfx-ref"

& $nuget install Microsoft.NETFramework.ReferenceAssemblies.net40 -OutputDirectory $outDir -ExcludeVersion -NonInteractive
& $nuget install Microsoft.NETFramework.ReferenceAssemblies.net45 -OutputDirectory $outDir -ExcludeVersion -NonInteractive

$refRoot = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework"

# v4.0
$src40 = "$outDir\Microsoft.NETFramework.ReferenceAssemblies.net40\build\.NETFramework\v4.0"
$dst40 = "$refRoot\v4.0"
if (-not (Test-Path $dst40)) { New-Item -ItemType Directory -Path $dst40 -Force | Out-Null }
Copy-Item "$src40\*" $dst40 -Recurse -Force

# v4.5
$src45 = "$outDir\Microsoft.NETFramework.ReferenceAssemblies.net45\build\.NETFramework\v4.5"
$dst45 = "$refRoot\v4.5"
if (-not (Test-Path $dst45)) { New-Item -ItemType Directory -Path $dst45 -Force | Out-Null }
Copy-Item "$src45\*" $dst45 -Recurse -Force
```

The `Copy-Item` step needs **administrator** privileges. Run the PowerShell session elevated.

### 3.3 NuGet CLI

`nuget.exe` 5.x or newer must be on `PATH`. Used to restore `packages.config` (the e2e project is old-style and `dotnet restore` will not handle it).

### 3.4 Scheduled tasks

Two Windows Task Scheduler tasks must exist on the workstation:

- `\LaunchODM` — optional, launches ODM elevated for manual smoke checks.
- `\RunE2ETest` — required, action = `C:\odm-e2e-results\run-e2e.bat`, "Run with highest privileges" enabled, "Run only when user is logged on" (so the WPF UI is real).

Verify with:

```bash
schtasks /query /tn \RunE2ETest /v /fo LIST
```

### 3.5 Environment

- Test machine must be on the same subnet as the cameras (WS-Discovery multicast does not cross subnets).
- `ANTHROPIC_API_KEY` must be set machine-wide if the optional Claude vision analysis is enabled.

---

## 4. One-Time Setup (Fresh Checkout)

From an **elevated** developer command prompt.

```bash
# 1. Clone (if not already done)
git clone <repo-url> C:\ak\ODM-Test
cd C:\ak\ODM-Test

# 2. Install targeting packs (see section 3.2)

# 3. Restore NuGet packages for the whole solution
nuget restore odm.sln

# 4. Materialise the unversioned Interop.UIAutomationClient folder
#    (the e2e csproj references packages\Interop.UIAutomationClient\lib\net48 with no version,
#     but nuget restores into a versioned directory)
xcopy /E /I /Y packages\Interop.UIAutomationClient.10.19041.0\* packages\Interop.UIAutomationClient\
#    Adjust the version suffix to match what nuget actually produced.

# 5. Create the results tree
mkdir C:\odm-e2e-results\reports
mkdir C:\odm-e2e-results\screenshots

# 6. Copy the example smoke config and edit it (see section 6)
copy odm\odm.e2e-tests\smoke-config.example.json C:\odm-e2e-results\smoke-config.json
```

---

## 5. Build

Three artifacts must exist before the e2e suite can run: the ODM application binaries, the packaged `build\odm.exe` tree, and the e2e test DLL.

### 5.1 Build the solution (Release | x64, v143 toolset)

C++ projects (`odm.player.lib`, `live555`) default to PlatformToolset `v142`, which BuildTools 2022 does not include. Override on the command line.

```bash
msbuild odm.sln /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143 /v:minimal
```

Equivalent CI step (see `.github/workflows/odm.yml`):

```bash
devenv odm.sln /Build "Release|x64" /Project odm.ui.app
```

### 5.2 Package `build\odm.exe`

```bash
package.bat
```

This collects `odm.ui.app\bin\Release\x64\*`, ffmpeg DLLs, images, locales, meta, and logs into the `build\` folder. The smoke config's `odm.exePath` must point at `build\odm.exe` (or wherever you copied it).

### 5.3 Build the e2e test DLL (Release)

```bash
nuget restore odm.sln
msbuild odm\odm.e2e-tests\odm.e2e-tests.csproj /p:Configuration=Release /v:minimal
```

The output is `odm\odm.e2e-tests\bin\Release\odm.e2e-tests.dll` — exactly what `run-e2e.bat` invokes.

---

## 6. Configure

The smoke config drives camera selection, credentials, timeouts, and capture paths. The example file is `odm/odm.e2e-tests/smoke-config.example.json`.

### 6.1 Location

`SmokeConfig.Load()` (see `odm/odm.e2e-tests/Config/SmokeConfig.cs`) reads:

1. The path in the `ODM_SMOKE_CONFIG` environment variable, if set.
2. Otherwise `smoke-config.json` next to the test DLL.

`run-e2e.bat` sets `ODM_SMOKE_CONFIG=C:\odm-e2e-results\smoke-config.json`, so that is the canonical location.

### 6.2 Shape

```json
{
  "odm": {
    "exePath": "C:\\ak\\ODM-Test\\build\\odm.exe",
    "expectedVersion": "2.2.252.17",
    "launchTimeoutSeconds": 30
  },
  "cameraProfiles": [
    {
      "classifyAs": "milesight",
      "manufacturerContains": "Milesight",
      "modelContains": null,
      "username": "admin",
      "password": "$env:ODM_MILESIGHT_PASS",
      "httpPort": 80,
      "httpsPort": 443,
      "suites": ["HttpsTests", "LiveVideoTests", "DiscoveryTests"]
    },
    {
      "classifyAs": "virtual",
      "manufacturerContains": "Apra",
      "modelContains": "Virtual",
      "username": "admin",
      "password": "$env:ODM_VIRTUAL_PASS",
      "httpPort": 80,
      "httpsPort": null,
      "suites": ["DiscoveryTests", "LiveVideoTests"]
    }
  ],
  "timeouts": {
    "discoverySeconds": 30,
    "connectionSeconds": 20,
    "videoLoadSeconds": 15,
    "progressSettleSeconds": 2
  },
  "capture": {
    "resolution": { "width": 1024, "height": 768 },
    "screenshotOutputDir": "C:\\odm-e2e-results\\screenshots",
    "reportOutputDir": "C:\\odm-e2e-results\\reports"
  },
  "claude": {
    "model": "claude-sonnet-4-6",
    "maxTokens": 1024
  }
}
```

### 6.3 Credentials

`username` and `password` accept either a plain string or the `$env:VAR_NAME` form. Env-var form is resolved at config load time by `SmokeConfig.ResolveEnvVars()`. Set the variables in the **same context that runs the test** — for the scheduled task, that means setting them as machine-wide environment variables (System Properties → Environment Variables → System) so the task picks them up.

`claude.apiKey` is never stored in config; it is read from `ANTHROPIC_API_KEY` at runtime. The active `smoke-config.json` lives outside the repo (`C:\odm-e2e-results\`) and must not be checked in — only the `smoke-config.example.json` template is versioned.

### 6.4 Camera profile fields

| Field | Meaning |
|---|---|
| `classifyAs` | Logical name used by `CameraDispatcher` to route tests. |
| `manufacturerContains` / `modelContains` | Case-insensitive substring match against ONVIF device info to bind a discovered camera to this profile. `null` = wildcard. If both are set, either matching classifies the camera. |
| `httpPort` / `httpsPort` | Ports to use; `httpsPort: null` means "skip HTTPS suites for this camera". |
| `suites` | Whitelist of test class names this camera participates in. |

---

## 7. Run

### 7.1 Trigger the elevated run

```bash
schtasks /run /tn \RunE2ETest
```

The task fires `C:\odm-e2e-results\run-e2e.bat`, which:

1. Kills any existing `odm.exe`.
2. Sets `ODM_SMOKE_CONFIG`.
3. Invokes `vstest.console.exe` against `odm\odm.e2e-tests\bin\Release\odm.e2e-tests.dll` with `/TestAdapterPath:packages\MSTest.TestAdapter.1.4.0\build\_common` and `/logger:trx`.
4. Redirects all output to `C:\odm-e2e-results\reports\run-<timestamp>.log`.

### 7.2 Wait for completion

`schtasks /run` returns immediately. Poll the task state:

```bash
schtasks /query /tn \RunE2ETest /fo LIST | findstr /C:"Status"
```

While the task is executing, `Status` reads `Running`. When it returns to `Ready`, the run is done.

Alternatively, watch the newest log:

```powershell
Get-ChildItem C:\odm-e2e-results\reports\run-*.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Wait
```

### 7.3 Result artifacts

- **Console + summary log**: `C:\odm-e2e-results\reports\run-<timestamp>.log` (final line: `Exit code: N`).
- **TRX file**: `odm\odm.e2e-tests\bin\Release\TestResults\<user>_<machine>_<timestamp>.trx`.
- **Screenshots**: `C:\odm-e2e-results\screenshots\` (one PNG per failed assertion / step that called `ScreenshotCapture`).

---

## 8. Interpret Results

The vstest console logger writes one line per test in the form:

```
Passed   DiscoveryTests.Milesight_DiscoversCamera
Failed   HttpsTests.Milesight_LiveVideo_OverHttps
Skipped  LiveVideoTests.Axis_LiveVideo
```

Followed by a summary block:

```
Total tests: 14
     Passed: 12
     Failed: 1
    Skipped: 1
```

Meaning of each outcome in this suite:

- **Passed** — the assertion succeeded against a real camera.
- **Failed** — assertion failed; check the screenshot named after the test in `C:\odm-e2e-results\screenshots\` and the stack trace in the log.
- **Skipped** — `Assert.Inconclusive` was called, typically because no camera profile in `smoke-config.json` declared the suite (e.g. an Axis camera without `HttpsTests` in its `suites` list). This is a configuration outcome, not a defect.

Exit code: `0` if all tests pass, non-zero on any failure.

---

## 9. Adding a New Test

The e2e project is **old-style csproj** (`odm/odm.e2e-tests/odm.e2e-tests.csproj`). New `.cs` files are not auto-discovered — they must be listed explicitly.

### 9.1 Pattern

1. Create the file under `odm/odm.e2e-tests/Tests/`, e.g. `Tests/MyNewTests.cs`.
2. Inherit from `SmokeTestBase` (which handles ODM lifecycle and config loading).
3. Use `[TestClass]` / `[TestMethod]` from `Microsoft.VisualStudio.TestTools.UnitTesting`.
4. For camera-specific tests, query `CameraDispatcher` to filter by `classifyAs` and skip with `Assert.Inconclusive` if no matching profile exists:

   ```csharp
   [TestInitialize]
   public void FindMilesight()
   {
       _camera = DiscoveredCameras
           .FirstOrDefault(c => c.Profile?.ClassifyAs == "milesight");

       if (_camera == null)
           Assert.Inconclusive("No Milesight camera discovered — skipping HTTPS test suite.");
   }
   ```

### 9.2 Register the file in the csproj

Add a `<Compile Include>` entry alongside the others:

```xml
<ItemGroup>
  <Compile Include="Tests\MyNewTests.cs" />
  <!-- existing entries -->
</ItemGroup>
```

### 9.3 Helpers

Reusable code goes in `odm/odm.e2e-tests/Helpers/` and must also be added with `<Compile Include>`. Existing helpers worth knowing:

- `OdmApp` — launches/closes ODM, waits for the device-list filter to appear (`AutomationId="valueFilter"`).
- `CameraDispatcher` — maps discovered cameras to profiles in the config.
- `WaitHelpers` — polling helpers around UIA queries (e.g. waiting for progress bars to disappear).
- `ScreenshotCapture` — PNG dump on failure.
- `ClaudeAnalyzer` — optional AI-assisted screenshot triage (uses `claude.model` from config).

---

## 10. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Discovery found 0 cameras` / discovery tests all fail | ODM not running elevated; WS-Discovery multicast subscription denied | Run via `schtasks /run /tn \RunE2ETest`, never directly from a non-elevated shell. Confirm `\RunE2ETest` has "Run with highest privileges" set. |
| `No test matches the given testcase filter` (or `0 tests discovered`) | `vstest.console.exe` cannot find the MSTest v1 adapter | Pass `/TestAdapterPath:"packages\MSTest.TestAdapter.1.4.0\build\_common"` (already done by `run-e2e.bat`). |
| `MSB8020: The build tools for v142 (Platform Toolset = 'v142') cannot be found` | C++ projects pinned to v142, BuildTools 2022 only has v143 | Pass `/p:PlatformToolset=v143` to `msbuild`. |
| `CS0246: The type or namespace name 'TestMethod' could not be found` | `packages.config` not restored, MSTest assemblies missing | `nuget restore odm.sln` (not `dotnet restore` — old-style csproj). |
| `Could not find file ...\packages\Interop.UIAutomationClient\lib\net48\Interop.UIAutomationClient.dll` | Versioned NuGet folder exists, unversioned alias does not | Copy `packages\Interop.UIAutomationClient.<version>\*` into `packages\Interop.UIAutomationClient\` (see step 4). |
| `The reference assemblies for .NETFramework,Version=v4.0 were not found` | Targeting packs missing from BuildTools 2022 | Run section 3.2 (elevated). |
| `FileNotFoundException: smoke-config.json` | `ODM_SMOKE_CONFIG` not set, and no config beside the test DLL | Set `ODM_SMOKE_CONFIG=C:\odm-e2e-results\smoke-config.json` or copy the file into `bin\Release\`. |
| All HTTPS tests skipped for a camera | Profile has `httpsPort: null` or suite not listed in `suites` | Edit the camera profile in `smoke-config.json`. |
| Test launches ODM but UI never appears | ODM crashed at startup, or `expectedVersion` mismatch | Check `build\logs\` after launch; rebuild with `package.bat`; update `odm.expectedVersion` to match the built version. |
| `run-e2e.bat` produces an empty log file | Path with spaces in `vstest.console.exe` not quoted in a customised batch file | Quote the executable path; the shipped `run-e2e.bat` already does this. |

---

## 11. Reference: Files and Paths

| Purpose | Path |
|---|---|
| CI workflow (canonical build recipe) | `.github/workflows/odm.yml` |
| Solution | `odm.sln` |
| Application project | `odm/odm.ui.app/` |
| E2E test project | `odm/odm.e2e-tests/` |
| Helper unit tests (CI) | `odm/odm.tests/` |
| Smoke config example | `odm/odm.e2e-tests/smoke-config.example.json` |
| Smoke config loader | `odm/odm.e2e-tests/Config/SmokeConfig.cs` |
| ODM launcher helper | `odm/odm.e2e-tests/Helpers/OdmApp.cs` |
| Packaging script | `package.bat` |
| Test runner wrapper | `C:\odm-e2e-results\run-e2e.bat` |
| Active smoke config | `C:\odm-e2e-results\smoke-config.json` |
| Logs | `C:\odm-e2e-results\reports\` |
| Screenshots | `C:\odm-e2e-results\screenshots\` |

---

## Appendix A — Design Rationale

This section captures the *why* behind key technology and architecture choices. It is background reading; nothing here is required to operate the suite.

### A.1 Why FlaUI

[FlaUI](https://github.com/FlaUI/FlaUI) (`FlaUI.UIA3` + `FlaUI.Core`) was selected over the alternatives:

| Option | Verdict | Reason |
|---|---|---|
| **FlaUI** | **Selected** | Native .NET, strong WPF support via UIA3, actively maintained, MIT licensed. |
| WinAppDriver | Rejected | Microsoft archived the repo in Jan 2024; no active development. |
| pywinauto | Rejected | Python — language mismatch with the existing .NET test infra. |
| AutoIt | Rejected | Coordinate-based, brittle, poor element inspection. |
| Playwright | N/A | Web only; no native desktop support. |

Why FlaUI fits ODM specifically:

- ODM is WPF (.NET 4.8); the UIA3 backend has first-class support for WPF automation peers.
- XAML `x:Name` attributes (e.g. `username`, `password`, `btLogin`, `player`) surface as `AutomationId` properties — stable element identifiers.
- Same language (.NET/C#) as the existing test project — shares the MSTest runner and CI infrastructure.
- `FlaUI.Core.Capturing.Capture` provides built-in screenshot support.

**Known limitation:** UIA cannot see inside custom-drawn surfaces. ODM's video player area is rendered via a native pipeline and appears to UIA as an opaque rectangle. The suite handles this by capturing a screenshot of the area and (optionally) sending it to the Claude vision API for content validation, rather than relying on UIA element inspection.

### A.2 Why screenshot + Claude vision analysis

For UI regions where UIA gives no useful structural information (the video surface, custom-drawn overlays, error toast graphics), the suite falls back to:

1. Capture a PNG of the relevant element via `FlaUI.Core.Capturing.Capture`.
2. POST the image plus a structured prompt to the Claude API.
3. Parse a JSON response with fields `pass`, `confidence`, `expected_found`, `expected_missing`, `errors_detected`, `notes`.
4. Apply a deterministic pass/fail rule: fail if `pass=false`, `confidence<0.7`, any expected element missing, or any error indicator detected.

This decouples the test from pixel-exact comparisons (which break on every cosmetic change) while still catching real regressions like a black video pane, a dropped error dialog, or a missing toolbar. The model name and token budget come from the `claude` block in `smoke-config.json`; the API key comes from `ANTHROPIC_API_KEY`.

### A.3 WaitHelpers — progress bar settle

ODM uses progress bars and spinners during device discovery and connection. The wait strategy is:

1. Poll `FindAllDescendants(ControlType.ProgressBar)` and consider a bar "active" if `IsEnabled && !IsOffscreen`.
2. Return once no active bars remain.
3. Sleep an additional `progressSettleSeconds` (default 2s) for render settle before capturing.
4. Each scenario has a hard timeout; on expiry, capture a screenshot of whatever state the UI is in for triage.

(An earlier draft used `progressBars.All(p => !p.IsEnabled || !p.IsOffscreen == false)`, which due to operator precedence accidentally evaluated to the right thing but was unreadable. The current `activeBars` filter is the explicit, correct form.)
