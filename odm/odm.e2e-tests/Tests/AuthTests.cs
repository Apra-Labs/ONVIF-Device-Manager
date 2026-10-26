using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core.AutomationElements;
using odm.e2e_tests.Helpers;

namespace odm.e2e_tests.Tests
{
    /// <summary>
    /// Scenario 2: Authentication — enter credentials and log in.
    /// Uses credentials from the first matched camera profile (or fails if no cameras discovered).
    /// </summary>
    [TestClass]
    public class AuthTests : SmokeTestBase
    {
        private OdmApp _app;

        [TestInitialize]
        public void Init()
        {
            if (DiscoveredCameras == null || DiscoveredCameras.Count == 0)
                Assert.Inconclusive("No cameras discovered — skipping auth test.");

            _app = new OdmApp();
            _app.Launch(Config.Odm.ExePath, TimeSpan.FromSeconds(Config.Odm.LaunchTimeoutSeconds));
        }

        [TestCleanup]
        public void Cleanup()
        {
            _app?.Dispose();
        }

        [TestMethod]
        [TestCategory("Scenario2")]
        public void Auth_LoginControlsPresent()
        {
            var username = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("username"));
            var password = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("password"));
            var loginBtn = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("btLogin"));

            Assert.IsNotNull(username, "Username field (AutomationId='username') should be present.");
            Assert.IsNotNull(password, "Password field (AutomationId='password') should be present.");
            Assert.IsNotNull(loginBtn, "Login button (AutomationId='btLogin') should be present.");
        }

        [TestMethod]
        [TestCategory("Scenario2")]
        public void Auth_LoginSucceeds()
        {
            var profile = DiscoveredCameras[0].Profile;
            if (profile == null)
                Assert.Inconclusive("First discovered camera has no matched profile — cannot determine credentials.");

            var username = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("username"))?.AsTextBox();
            var password = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("password"))?.AsTextBox();
            var loginBtn = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("btLogin"))?.AsButton();

            Assert.IsNotNull(username, "Username field not found.");
            Assert.IsNotNull(password, "Password field not found.");
            Assert.IsNotNull(loginBtn, "Login button not found.");

            username.Text = profile.Username;
            password.Text = profile.Password;

            var screenshotBefore = ScreenshotCapture.Capture(
                _app.MainWindow, Config.Capture.ScreenshotOutputDir, "Auth", "BeforeLogin");

            loginBtn.Click();
            WaitHelpers.WaitForProgressToComplete(_app.MainWindow, TimeSpan.FromSeconds(Config.Timeouts.ConnectionSeconds));
            WaitHelpers.StabilityWait(Config.Timeouts.ProgressSettleSeconds);

            var screenshotAfter = ScreenshotCapture.Capture(
                _app.MainWindow, Config.Capture.ScreenshotOutputDir, "Auth", "AfterLogin");

            var analyzer = new ClaudeAnalyzer(Config.Claude.Model);
            var result = analyzer.AnalyzeScreenshot(
                screenshotAfter,
                $"User has entered credentials (username: {profile.Username}) and clicked Login. Expecting authenticated state.",
                new[] { "Authenticated/logged-in state visible", "Device list or main content accessible" },
                new[] { "Login error message", "Authentication failed dialog", "Invalid credentials message" });

            Assert.IsTrue(
                ClaudeAnalyzer.DeterminePassFail(result),
                $"Login did not succeed per Claude analysis: {result.Summary}");
        }
    }
}
