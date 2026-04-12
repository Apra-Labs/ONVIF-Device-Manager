using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.e2e_tests.Helpers;

namespace odm.e2e_tests.Tests
{
    /// <summary>
    /// Scenario 3: Device discovery — ODM shows discovered cameras in device list.
    /// Runs for each camera type present on the network.
    /// </summary>
    [TestClass]
    public class DiscoveryTests : SmokeTestBase
    {
        private OdmApp _app;

        [TestInitialize]
        public void Init()
        {
            if (DiscoveredCameras == null || DiscoveredCameras.Count == 0)
                Assert.Inconclusive("No cameras discovered on the network — skipping discovery tests.");

            _app = new OdmApp();
            _app.Launch(Config.Odm.ExePath, TimeSpan.FromSeconds(Config.Odm.LaunchTimeoutSeconds));
        }

        [TestCleanup]
        public void Cleanup()
        {
            _app?.Dispose();
        }

        [TestMethod]
        [TestCategory("Scenario3")]
        public void Discovery_AtLeastOneCameraFound()
        {
            Assert.IsTrue(
                DiscoveredCameras.Count > 0,
                "WS-Discovery should find at least one camera on the network.");
        }

        [TestMethod]
        [TestCategory("Scenario3")]
        public void Discovery_OdmShowsDeviceInList()
        {
            // Wait for ODM to complete its own discovery and show devices in the UI
            WaitHelpers.WaitForProgressToComplete(
                _app.MainWindow,
                TimeSpan.FromSeconds(Config.Timeouts.DiscoverySeconds));
            WaitHelpers.StabilityWait(Config.Timeouts.ProgressSettleSeconds);

            var screenshotPath = ScreenshotCapture.Capture(
                _app.MainWindow, Config.Capture.ScreenshotOutputDir, "Discovery", "DeviceList");

            // Build a list of known IPs to verify at least one is visible
            var expectedIps = DiscoveredCameras.Select(c => c.IpAddress).Where(ip => !string.IsNullOrEmpty(ip)).ToArray();

            var analyzer = new ClaudeAnalyzer(Config.Claude.Model);
            var result = analyzer.AnalyzeScreenshot(
                screenshotPath,
                "ODM has finished device discovery. The device list panel should show at least one discovered camera.",
                new[] { "Device list panel populated with at least one device", "No 'loading' or spinner visible" },
                new[] { "Empty device list with no cameras", "Error message in device list", "Application crash" });

            Assert.IsTrue(
                ClaudeAnalyzer.DeterminePassFail(result),
                $"Device discovery did not succeed per Claude analysis: {result.Summary}");
        }

        [TestMethod]
        [TestCategory("Scenario3")]
        public void Discovery_KnownCameraTypesClassifiedCorrectly()
        {
            var knownCameras = DiscoveredCameras.Where(c => c.Profile != null).ToList();

            // If no cameras matched a profile, that's Inconclusive rather than a failure
            if (!knownCameras.Any())
                Assert.Inconclusive("No discovered cameras matched a configured profile — cannot verify classification.");

            foreach (var cam in knownCameras)
            {
                Assert.IsFalse(
                    string.IsNullOrEmpty(cam.CameraType),
                    $"Camera at {cam.IpAddress} should have a non-empty CameraType.");

                Assert.AreNotEqual(
                    "unknown",
                    cam.CameraType,
                    $"Camera {cam.Manufacturer} {cam.Model} at {cam.IpAddress} matched a profile but returned 'unknown' type.");
            }
        }

        [TestMethod]
        [TestCategory("Scenario3")]
        public void Discovery_UnknownCamerasSkipMostSuites()
        {
            var unknownCameras = DiscoveredCameras.Where(c => c.Profile == null).ToList();

            // Unknown cameras should only get DiscoveryTests — this test verifies we don't crash on them
            foreach (var cam in unknownCameras)
                Assert.AreEqual("unknown", cam.CameraType,
                    $"Camera {cam.Manufacturer} {cam.Model} with no profile should be classified as 'unknown'.");

            // If no unknown cameras exist, this test passes trivially
        }
    }
}
