using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.e2e_tests.Helpers;

namespace odm.e2e_tests.Tests
{
    /// <summary>
    /// Scenario 1: Launch ODM and verify it opens correctly with expected version.
    /// </summary>
    [TestClass]
    public class LaunchTests : SmokeTestBase
    {
        private OdmApp _app;

        [TestInitialize]
        public void Init()
        {
            _app = new OdmApp();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _app?.Dispose();
        }

        [TestMethod]
        [TestCategory("Scenario1")]
        public void Launch_MainWindowAppears()
        {
            _app.Launch(Config.Odm.ExePath, TimeSpan.FromSeconds(Config.Odm.LaunchTimeoutSeconds));

            Assert.IsNotNull(_app.MainWindow, "MainWindow should not be null after launch.");
            Assert.IsTrue(_app.MainWindow.IsAvailable, "MainWindow should be available.");
        }

        [TestMethod]
        [TestCategory("Scenario1")]
        public void Launch_WindowTitleContainsVersion()
        {
            _app.Launch(Config.Odm.ExePath, TimeSpan.FromSeconds(Config.Odm.LaunchTimeoutSeconds));

            var title = _app.MainWindow.Title ?? string.Empty;
            Assert.IsTrue(
                title.Contains(Config.Odm.ExpectedVersion),
                $"Window title '{title}' should contain expected version '{Config.Odm.ExpectedVersion}'.");
        }

        [Ignore("Claude CLI integration broken — '--image' flag no longer supported. Re-enable once ClaudeAnalyzer is updated.")]
        [TestMethod]
        [TestCategory("Scenario1")]
        public void Launch_ScreenshotCapturedAndAnalyzed()
        {
            _app.Launch(Config.Odm.ExePath, TimeSpan.FromSeconds(Config.Odm.LaunchTimeoutSeconds));
            WaitHelpers.StabilityWait(2);

            var screenshotPath = ScreenshotCapture.Capture(
                _app.MainWindow,
                Config.Capture.ScreenshotOutputDir,
                "Launch",
                "MainWindow");

            var analyzer = new ClaudeAnalyzer(Config.Claude.Model);
            var result = analyzer.AnalyzeScreenshot(
                screenshotPath,
                "ODM has just launched. We expect to see the main application window.",
                new[] { "Toolbar at the top", "Device list panel on the left", "Main content area" },
                new[] { "Error dialog boxes", "Application crash message", "Blank/white window" });

            Assert.IsTrue(
                ClaudeAnalyzer.DeterminePassFail(result),
                $"Claude analysis failed: {result.Summary}. Missing: [{string.Join(", ", result.ExpectedMissing ?? new System.Collections.Generic.List<string>())}]");
        }
    }
}
