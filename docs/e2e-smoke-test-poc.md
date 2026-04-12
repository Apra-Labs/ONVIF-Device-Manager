# ODM — Comprehensive End-to-End UI Test Suite Plan

## Overview

Automated, comprehensive end-to-end UI tests that run after each ODM deployment on a dedicated Windows device. The test suite drives the UI against all discovered cameras, captures screenshots at key points, and sends them to Claude API for visual validation.

The suite is **camera-type-aware**: it discovers cameras via ONVIF at runtime, classifies them by manufacturer/model, and dispatches the appropriate test suites per camera type. If a camera type is not present on the network, its tests are skipped — never failed.

---

## 1. Technology Choice: FlaUI

**Recommendation: [FlaUI](https://github.com/FlaUI/FlaUI)** (FlaUI.UIA3 + FlaUI.Core)

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **FlaUI** | Native .NET, strong WPF support via UIA3, actively maintained, NuGet package, MIT licensed | Smaller community than Selenium ecosystem | **Selected** |
| WinAppDriver | WebDriver protocol, familiar API | Requires Appium, Microsoft archived the repo (Jan 2024), no active development | Rejected — unmaintained |
| pywinauto | Good UIA support, Python ecosystem | Different language from codebase, harder to integrate with existing .NET test infra | Rejected — language mismatch |
| AutoIt | Simple scripting, good for legacy apps | Coordinate-based clicking is brittle, poor programmatic element inspection | Rejected — fragile |
| Playwright | Excellent for web | Does not support native desktop apps | Not applicable |

**Why FlaUI:**
- ODM is WPF (.NET 4.8) — FlaUI's UIA3 backend has first-class support for WPF's automation peers
- XAML `x:Name` attributes (e.g. `username`, `password`, `btLogin`, `player`) are exposed as `AutomationId` properties, giving stable element identifiers
- Same language (.NET/C#) as the existing test project — can share the MSTest runner and CI infrastructure
- Supports window manipulation (resize, focus, wait patterns) out of the box
- `FlaUI.Core.Capturing.Capture` provides built-in screenshot support

**One concern to note:** FlaUI depends on the Windows UI Automation (UIA) tree being well-populated. If any ODM views use custom-drawn content (e.g. the video player surface rendered via DirectShow/GStreamer), those regions will appear as opaque rectangles to UIA. The test suite handles this via screenshot + Claude vision analysis rather than element inspection for such areas.

---

## 2. Camera-Type-Aware Dispatch Architecture

### Runtime discovery and classification

On test suite startup, before any UI tests run, the test harness performs ONVIF WS-Discovery to find all cameras on the local network. Each discovered device is queried for its `DeviceInfo` (manufacturer, model, firmware) and classified into a camera profile.

**Discovery flow:**

```
1. Send WS-Discovery probe (multicast to 239.255.255.250:3702)
2. Collect all responding ONVIF endpoints
3. For each endpoint, call GetDeviceInformation()
4. Match manufacturer/model against cameraProfiles in smoke-config.json
5. Group cameras by classifyAs type
6. Dispatch test suites per camera group
```

**Classification rules** (evaluated in order, first match wins):

| Manufacturer/Model match | classifyAs | Transport | Credentials (from config) | Test suites |
|---|---|---|---|---|
| Manufacturer contains `Milesight` | `milesight` | HTTPS (port 443) | admin / (from config) | HttpsTests, LiveVideoTests, DiscoveryTests |
| Manufacturer contains `Apra` OR model contains `Virtual` | `virtual` | HTTP (port 80) | admin / (from config) | DiscoveryTests, LiveVideoTests |
| Manufacturer contains `AXIS` | `axis` | HTTP (port 80) | (from config) | DiscoveryTests, LiveVideoTests |
| No match | `unknown` | HTTP (port 80) | — | DiscoveryTests only |

### Dispatch logic

```csharp
[TestClass]
public class CameraDispatcher
{
    private static List<DiscoveredCamera> _cameras;
    private static SmokeConfig _config;

    [AssemblyInitialize]
    public static void DiscoverAndClassify(TestContext ctx)
    {
        _config = SmokeConfig.Load();
        var discovered = OnvifDiscovery.Probe(TimeSpan.FromSeconds(10));

        _cameras = discovered.Select(endpoint =>
        {
            var info = endpoint.GetDeviceInformation();
            var profile = _config.CameraProfiles.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(p.ManufacturerContains)
                    && info.Manufacturer.Contains(p.ManufacturerContains, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(p.ModelContains)
                    && info.Model.Contains(p.ModelContains, StringComparison.OrdinalIgnoreCase)));

            return new DiscoveredCamera
            {
                Endpoint = endpoint,
                DeviceInfo = info,
                Profile = profile  // null if no match → skip most suites
            };
        }).ToList();

        ctx.Properties["DiscoveredCameras"] = _cameras;
    }
}
```

### Per-camera test execution

Each test class checks whether a camera of the required type was discovered. If not, the test is marked as **Inconclusive** (skipped), not Failed:

```csharp
[TestClass]
public class MilesightHttpsTests : SmokeTestBase
{
    private DiscoveredCamera _camera;

    [TestInitialize]
    public void FindMilesight()
    {
        _camera = DiscoveredCameras
            .FirstOrDefault(c => c.Profile?.ClassifyAs == "milesight");

        if (_camera == null)
            Assert.Inconclusive("No Milesight camera discovered — skipping HTTPS test suite.");
    }

    [TestMethod]
    public void HttpsConnection_Succeeds()
    {
        // Uses _camera.Profile.Username, _camera.Profile.Password,
        // _camera.Profile.HttpsPort from config
    }
}
```

---

## 3. Test Architecture

### Project structure

```
odm/odm.e2e-tests/
  odm.e2e-tests.csproj           # .NET 4.8 class library, MSTest + FlaUI.UIA3
  Config/
    smoke-config.json             # Parameterized test configuration (camera profiles, timeouts)
  Discovery/
    OnvifDiscovery.cs             # WS-Discovery probe + GetDeviceInformation
    CameraClassifier.cs           # Match discovered cameras to config profiles
    DiscoveredCamera.cs           # Data class: endpoint + device info + matched profile
  Helpers/
    OdmApp.cs                     # App lifecycle: launch, attach, close
    WaitHelpers.cs                # Progress bar / spinner wait logic
    ScreenshotCapture.cs          # Capture + save screenshot
    ClaudeAnalyzer.cs             # Send screenshot to Claude API, parse response
  Tests/
    SmokeTestBase.cs              # Base class: config loading, camera list, shared helpers
    LaunchTests.cs                # Scenario 1: app launches, correct version
    AuthTests.cs                  # Scenario 2: login flow
    DiscoveryTests.cs             # Scenario 3: device discovery (all camera types)
    LiveVideoTests.cs             # Scenario 4: live video stream (per camera type)
    HttpsTests.cs                 # Scenario 5: HTTPS connectivity (Milesight only)
    ErrorDetectionTests.cs        # Scenario 6: no error dialogs present
```

### App lifecycle helper (`OdmApp.cs`)

```csharp
public class OdmApp : IDisposable
{
    private Application _app;
    private AutomationBase _automation;

    public Window MainWindow { get; private set; }

    public void Launch(string exePath)
    {
        _automation = new UIA3Automation();
        _app = Application.Launch(exePath);
        MainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(30));
        // Resize to standard resolution for consistent screenshots
        MainWindow.Move(0, 0);
        MainWindow.SetSize(1024, 768);
    }

    public void Dispose()
    {
        _app?.Close();
        _automation?.Dispose();
    }
}
```

### Waiting for async operations (`WaitHelpers.cs`)

ODM uses progress bars and spinners during device discovery and connection. The wait strategy:

1. **Element-based wait** — poll for known progress indicators to disappear:
   ```csharp
   public static void WaitForProgressToComplete(Window window, TimeSpan timeout)
   {
       var deadline = DateTime.UtcNow + timeout;
       while (DateTime.UtcNow < deadline)
       {
           var progressBars = window.FindAllDescendants(cf =>
               cf.ByControlType(ControlType.ProgressBar));

           // Done when: no progress bars exist, or all are either disabled or offscreen (hidden)
           var activeBars = progressBars.Where(p => p.IsEnabled && !p.IsOffscreen);
           if (!activeBars.Any())
               return;

           Thread.Sleep(500);
       }
       // Timeout — capture screenshot anyway (test may still pass visually)
   }
   ```

2. **Stability wait** — after progress completes, wait an additional 2 seconds for render settle before capturing screenshots.

3. **Hard timeout** — each scenario has a max timeout (default 60s). If exceeded, capture a screenshot of whatever state the UI is in and flag it for analysis.

### Parameterization

All test parameters come from `smoke-config.json` loaded at test init:

```csharp
[TestClass]
public class SmokeTestBase
{
    protected static SmokeConfig Config;
    protected static List<DiscoveredCamera> DiscoveredCameras;

    [AssemblyInitialize]
    public static void Init(TestContext ctx)
    {
        var path = Environment.GetEnvironmentVariable("ODM_SMOKE_CONFIG")
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-config.json");
        Config = JsonConvert.DeserializeObject<SmokeConfig>(File.ReadAllText(path));
    }
}
```

---

## 4. Screenshot + Claude Analysis Flow

### Capture

Use FlaUI's built-in capture after each test step:

```csharp
public static string CaptureScreenshot(Window window, string scenarioName, string stepName)
{
    var image = Capture.Element(window);
    var filename = $"{scenarioName}_{stepName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
    var path = Path.Combine(Config.ScreenshotOutputDir, filename);
    image.ToFile(path);
    return path;
}
```

### Claude API analysis

Send each screenshot to Claude with a structured prompt. Use the Anthropic .NET SDK or direct HTTP calls:

```csharp
public class ClaudeAnalyzer
{
    private readonly string _apiKey;
    private readonly HttpClient _http;

    public async Task<AnalysisResult> AnalyzeScreenshot(
        string screenshotPath,
        string scenarioContext,
        string[] expectedElements,
        string[] errorIndicators)
    {
        var imageBytes = File.ReadAllBytes(screenshotPath);
        var base64 = Convert.ToBase64String(imageBytes);

        var prompt = BuildPrompt(scenarioContext, expectedElements, errorIndicators);

        // Call Claude API with vision
        var response = await CallClaude(base64, prompt);

        return ParseResponse(response);
    }
}
```

### Analysis prompt template

```
You are a QA analyst reviewing a screenshot of the ONVIF Device Manager (ODM) desktop application.

**Scenario:** {scenarioContext}
(e.g. "User has logged in and navigated to live video view for camera at 192.168.1.190")

**Expected elements on screen:**
{expectedElements as bulleted list}
(e.g. "- Video feed displaying live stream", "- Camera name visible in device list")

**Error indicators to check for:**
{errorIndicators as bulleted list}
(e.g. "- Error dialog boxes", "- Red warning text", "- 'Connection failed' messages", "- Blank/black areas where content should be")

Analyze the screenshot and respond with EXACTLY this JSON structure:
{
  "pass": true/false,
  "confidence": 0.0-1.0,
  "summary": "one-line summary of what you see",
  "expected_found": ["list of expected elements that ARE visible"],
  "expected_missing": ["list of expected elements that are NOT visible"],
  "errors_detected": ["list of any error indicators found"],
  "notes": "any additional observations"
}
```

### Pass/fail determination

```csharp
public bool DeterminePassFail(AnalysisResult result)
{
    // Fail if Claude says fail
    if (!result.Pass) return false;

    // Fail if confidence is too low (Claude uncertain)
    if (result.Confidence < 0.7) return false;

    // Fail if any expected elements are missing
    if (result.ExpectedMissing.Any()) return false;

    // Fail if errors detected
    if (result.ErrorsDetected.Any()) return false;

    return true;
}
```

---

## 5. Test Scenarios

### Scenario 1: Launch and Version Check

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Launch `build\odm.exe` | After main window appears | Window title contains expected version string |
| 2 | Verify window renders | Full window capture | Main layout visible: toolbar, device list panel, main content area. No error dialogs. |

### Scenario 2: Authentication

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Locate auth controls | Before login | `username` and `password` fields visible, `btLogin` button present |
| 2 | Enter credentials (from matched camera profile) | After filling fields | Fields populated, no validation errors |
| 3 | Click Login | After login completes | Login panel shows authenticated state (logout button visible), no error dialogs |

### Scenario 3: Device Discovery (per camera type)

Runs for each camera type discovered on the network.

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Wait for device list to populate | After discovery settles (progress bar gone) | Device list panel shows at least one device |
| 2 | Verify expected device | After list populated | Discovered camera's name/IP visible in device list |

### Scenario 4: Connect and View Live Video (per camera type)

Runs for each discovered camera whose profile includes `LiveVideoTests`.

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Click on discovered camera | After navigation | Device detail view loads |
| 2 | Navigate to live video | After video view loads and progress completes | Video player area (`player` element) is visible, video feed is not a black rectangle, no connection error messages |

### Scenario 5: HTTPS Connectivity Verification (Milesight cameras only)

Only runs when a Milesight camera (or other profile with `HttpsTests` in its suites) is discovered.

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Connect via HTTPS port | After HTTPS connection established | Connection succeeds without certificate error dialogs |
| 2 | Verify secure connection indicator | After connection | Device accessible over HTTPS, no plaintext fallback warnings |

### Scenario 6: Error State Detection (Negative Check)

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Full window scan | At end of all scenarios | No unexpected error dialogs, no unhandled exception windows, no "not responding" state |

---

## 6. Configuration Schema

`smoke-config.json`:

```json
{
  "odm": {
    "exePath": "C:\\akhil\\git\\ONVIF-Device-Manager\\build\\odm.exe",
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
    },
    {
      "classifyAs": "axis",
      "manufacturerContains": "AXIS",
      "modelContains": null,
      "username": "root",
      "password": "...",
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
    "resolution": {
      "width": 1024,
      "height": 768
    },
    "screenshotOutputDir": "C:\\odm-e2e-results\\screenshots",
    "reportOutputDir": "C:\\odm-e2e-results\\reports"
  },
  "claude": {
    "model": "claude-sonnet-4-6",
    "maxTokens": 1024
  }
}
```

**Notes:**
- `claude.apiKey` is read from environment variable `ANTHROPIC_API_KEY` (never stored in config)
- Credentials in `cameraProfiles` are stored in the config file which should be excluded from version control (add to `.gitignore`). For CI, source from environment variables or a secrets manager.
- `manufacturerContains` and `modelContains` are case-insensitive substring matches. Either or both can be set; if both are set, either matching classifies the camera.
- A camera matching no profile is classified as `unknown` and only gets `DiscoveryTests`.
- All timeouts have sensible defaults; config values override.

---

## 7. Integration with Deploy Flow

Add as **Step 7** in `docs/deploy.md`:

```markdown
### Step 7 — End-to-end test suite (automated)

After launch (Step 5), run the e2e test suite:

    powershell -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe' 'C:\akhil\git\ONVIF-Device-Manager\odm\odm.e2e-tests\bin\Release\net48\odm.e2e-tests.dll'"

Results are written to `C:\odm-e2e-results\`:
- `screenshots\` — captured PNGs from each scenario
- `reports\` — JSON report with pass/fail per scenario and Claude analysis

If any scenario fails, review the screenshot + Claude analysis before proceeding.
```

**Future automation:** The deploy one-liner can be extended to chain the e2e test suite:

```powershell
schtasks /Run /TN 'ODM-kill'; Start-Sleep 2; & deploy.ps1; schtasks /Run /TN 'ODM-dev'; Start-Sleep 10; & run-e2e-tests.ps1
```

The 10-second sleep gives ODM time to launch and render before tests attach. The actual tests use FlaUI's `GetMainWindow` with a 30-second timeout, so this is a soft lower bound.

---

## 8. Success Criteria

The test suite PoC is "working" when all of the following are true:

| # | Criterion | How to verify |
|---|---|---|
| 1 | **FlaUI can launch and attach to ODM** | `OdmApp.Launch()` succeeds, `MainWindow` is non-null, window title contains version string |
| 2 | **Window resize works** | Screenshot dimensions are 1024x768 |
| 3 | **Element discovery works** | Tests can find `username`, `password`, `btLogin` by AutomationId |
| 4 | **ONVIF camera discovery works** | At least one camera discovered and classified against config profiles |
| 5 | **Camera-type dispatch works** | Tests for discovered camera types run; tests for absent camera types are skipped (Inconclusive), not failed |
| 6 | **Wait-for-progress works** | Discovery test waits for progress bar to complete before capturing, does not timeout on a normal run |
| 7 | **Screenshots capture correctly** | PNG files saved to output dir, non-zero size, visually show the ODM window (not a blank/black image) |
| 8 | **Claude API analysis returns structured results** | API call succeeds, response parses into the expected JSON schema, `pass`/`confidence` fields present |
| 9 | **At least 4 of 6 scenarios pass** | Scenarios 1-4 (launch, auth, discovery, live video) pass on a clean run with at least one reachable camera |
| 10 | **End-to-end run completes in under 5 minutes** | Total wall time from test start to report generation < 300 seconds |
| 11 | **Report generated** | JSON report written with per-scenario pass/fail, screenshot paths, and Claude analysis summaries |

---

## 9. Dependencies and Prerequisites

- **NuGet packages:** `FlaUI.UIA3`, `FlaUI.Core`, `Newtonsoft.Json`, `MSTest.TestFramework`, `MSTest.TestAdapter`
- **ONVIF library:** The existing ODM ONVIF stack (or a lightweight WS-Discovery client) for runtime camera discovery
- **Environment:** `ANTHROPIC_API_KEY` set on the test machine
- **Network:** Test machine must be on the same subnet as cameras (for WS-Discovery multicast) and must reach Claude API (api.anthropic.com)
- **Configuration:** `smoke-config.json` populated with camera profiles matching the test network's cameras
- **Scheduled tasks:** `ODM-dev` and `ODM-kill` registered (per deploy.md one-time setup)
- **ODM built and deployed:** `build\odm.exe` must be current before running the test suite
- **.gitignore:** `smoke-config.json` should be gitignored since it contains credentials (provide a `smoke-config.example.json` template in the repo)

---

## Review Notes

**FlaUI choice: Confirmed.** FlaUI remains the right choice. The only real alternative (WinAppDriver) is archived. The UIA3 backend handles WPF well. The main risk — opaque video player surfaces — is mitigated by the Claude vision analysis approach.

**WaitHelpers bug (fixed above).** The original condition `progressBars.All(p => !p.IsEnabled || !p.IsOffscreen == false)` had a confusing double negation. Due to C# operator precedence, `!p.IsOffscreen == false` evaluates as `(!p.IsOffscreen) == false`, which is `p.IsOffscreen` — the code happened to work by accident, but was misleading and fragile. The revised version uses an explicit `activeBars` filter (`p.IsEnabled && !p.IsOffscreen`) which is clear and correct: return when no active (enabled + visible) progress bars remain.

**Claude API analysis flow: Sound.** The structured prompt with JSON response schema is a good approach. One recommendation for implementation: add retry logic (1-2 retries with backoff) for transient API failures, and consider caching screenshots with their analysis results so re-runs don't re-analyze unchanged states.

**Test scenario coverage: Adequate for PoC.** The 6 scenarios cover the critical user journey (launch → auth → discover → view video → HTTPS → error check). For post-PoC expansion, consider adding: credential manager tests (add/edit/delete stored credentials), multi-camera switching, and window resize/layout persistence.
