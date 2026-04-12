# ODM — End-to-End UI Smoke Test PoC Plan

## Overview

Automated UI smoke tests that run after each ODM deployment on a dedicated Windows device. The tests drive the UI, capture screenshots at key points, and send them to Claude API for visual validation.

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

---

## 2. Test Architecture

### Project structure

```
odm/odm.smoke-tests/
  odm.smoke-tests.csproj      # .NET 4.8 class library, MSTest + FlaUI.UIA3
  Config/
    smoke-config.json          # Parameterized test configuration
  Helpers/
    OdmApp.cs                  # App lifecycle: launch, attach, close
    WaitHelpers.cs             # Progress bar / spinner wait logic
    ScreenshotCapture.cs       # Capture + save screenshot
    ClaudeAnalyzer.cs          # Send screenshot to Claude API, parse response
  Tests/
    LaunchTests.cs             # Scenario 1: app launches, correct version
    AuthTests.cs               # Scenario 2: login flow
    DiscoveryTests.cs          # Scenario 3: device discovery
    LiveVideoTests.cs          # Scenario 4: live video stream
    HttpsTests.cs              # Scenario 5: HTTPS connectivity
    ErrorDetectionTests.cs     # Scenario 6: no error dialogs present
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
        // Resize to standard resolution
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
           
           // No progress bars, or all are at 100% / hidden
           if (progressBars.All(p => !p.IsEnabled || !p.IsOffscreen == false))
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

## 3. Screenshot + Claude Analysis Flow

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

## 4. Test Scenarios for PoC

### Scenario 1: Launch and Version Check

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Launch `build\odm.exe` | After main window appears | Window title contains expected version string |
| 2 | Verify window renders | Full window capture | Main layout visible: toolbar, device list panel, main content area. No error dialogs. |

### Scenario 2: Authentication

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Locate auth controls | Before login | `username` and `password` fields visible, `btLogin` button present |
| 2 | Enter credentials | After filling fields | Fields populated, no validation errors |
| 3 | Click Login | After login completes | Login panel shows authenticated state (logout button visible), no error dialogs |

### Scenario 3: Device Discovery

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Wait for device list to populate | After discovery settles (progress bar gone) | Device list panel shows at least one device |
| 2 | Verify expected device | After list populated | Expected device name/IP visible in device list |

### Scenario 4: Connect and View Live Video

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Click on discovered camera | After navigation | Device detail view loads |
| 2 | Navigate to live video | After video view loads and progress completes | Video player area (`player` element) is visible, video feed is not a black rectangle, no connection error messages |

### Scenario 5: HTTPS Connectivity Verification

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Connect via HTTPS port | After HTTPS connection established | Connection succeeds without certificate error dialogs |
| 2 | Verify secure connection indicator | After connection | Device accessible over HTTPS, no plaintext fallback warnings |

### Scenario 6: Error State Detection (Negative Check)

| Step | Action | Screenshot | Expected |
|---|---|---|---|
| 1 | Full window scan | At end of all scenarios | No unexpected error dialogs, no unhandled exception windows, no "not responding" state |

---

## 5. Parameterization Schema

`smoke-config.json`:

```json
{
  "odm": {
    "exePath": "C:\\akhil\\git\\ONVIF-Device-Manager\\build\\odm.exe",
    "expectedVersion": "2.2.252.17",
    "launchTimeoutSeconds": 30
  },
  "camera": {
    "ip": "192.168.1.190",
    "httpPort": 80,
    "httpsPort": 443,
    "username": "admin",
    "password": "...",
    "expectedDeviceName": "AXIS"
  },
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
    "screenshotOutputDir": "C:\\odm-smoke-results\\screenshots",
    "reportOutputDir": "C:\\odm-smoke-results\\reports"
  },
  "claude": {
    "model": "claude-sonnet-4-6",
    "maxTokens": 1024
  }
}
```

**Notes:**
- `claude.apiKey` is read from environment variable `ANTHROPIC_API_KEY` (never stored in config)
- `camera.password` can also be sourced from `ODM_TEST_PASS` env var as a fallback
- All timeouts have sensible defaults; config values override

---

## 6. Integration with Deploy Flow

Add as **Step 7** in `docs/deploy.md`:

```markdown
### Step 7 — Smoke test (automated)

After launch (Step 5), run the smoke test suite:

    powershell -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe' 'C:\akhil\git\ONVIF-Device-Manager\odm\odm.smoke-tests\bin\Release\net48\odm.smoke-tests.dll'"

Results are written to `C:\odm-smoke-results\`:
- `screenshots\` — captured PNGs from each scenario
- `reports\` — JSON report with pass/fail per scenario and Claude analysis

If any scenario fails, review the screenshot + Claude analysis before proceeding.
```

**Future automation:** The deploy one-liner can be extended to chain smoke tests:

```powershell
schtasks /Run /TN 'ODM-kill'; Start-Sleep 2; & deploy.ps1; schtasks /Run /TN 'ODM-dev'; Start-Sleep 10; & run-smoke-tests.ps1
```

The 10-second sleep gives ODM time to launch and render before tests attach. The actual tests use FlaUI's `GetMainWindow` with a 30-second timeout, so this is a soft lower bound.

---

## 7. Success Criteria for PoC

The PoC is "working" when all of the following are true:

| # | Criterion | How to verify |
|---|---|---|
| 1 | **FlaUI can launch and attach to ODM** | `OdmApp.Launch()` succeeds, `MainWindow` is non-null, window title contains version string |
| 2 | **Window resize works** | Screenshot dimensions are 1024x768 |
| 3 | **Element discovery works** | Tests can find `username`, `password`, `btLogin` by AutomationId |
| 4 | **Wait-for-progress works** | Discovery test waits for progress bar to complete before capturing, does not timeout on a normal run |
| 5 | **Screenshots capture correctly** | PNG files saved to output dir, non-zero size, visually show the ODM window (not a blank/black image) |
| 6 | **Claude API analysis returns structured results** | API call succeeds, response parses into the expected JSON schema, `pass`/`confidence` fields present |
| 7 | **At least 4 of 6 scenarios pass** | Scenarios 1-4 (launch, auth, discovery, live video) pass on a clean run with a reachable camera |
| 8 | **End-to-end run completes in under 5 minutes** | Total wall time from test start to report generation < 300 seconds |
| 9 | **Report generated** | JSON report written with per-scenario pass/fail, screenshot paths, and Claude analysis summaries |

---

## Dependencies and Prerequisites

- **NuGet packages:** `FlaUI.UIA3`, `FlaUI.Core`, `Newtonsoft.Json`, `MSTest.TestFramework`, `MSTest.TestAdapter`
- **Environment:** `ANTHROPIC_API_KEY` set on the test machine
- **Network:** Test machine must reach the camera (192.168.1.190) and Claude API (api.anthropic.com)
- **Scheduled tasks:** `ODM-dev` and `ODM-kill` registered (per deploy.md one-time setup)
- **ODM built and deployed:** `build\odm.exe` must be current before running smoke tests
