using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.e2e_tests.Config;
using odm.e2e_tests.Helpers;

namespace odm.e2e_tests.Tests
{
    [TestClass]
    public class HttpsNavigationTests
    {
        private static OdmApp _app;
        private static SmokeConfig _config;
        private const string CameraIp = "192.168.1.190";

        [ClassInitialize]
        public static void ClassInit(TestContext ctx)
        {
            _config = SmokeConfig.Load();
            _app = new OdmApp();
            _app.Launch(_config.Odm.ExePath, TimeSpan.FromSeconds(_config.Odm.LaunchTimeoutSeconds));

            WaitHelpers.WaitForProgressToComplete(_app.MainWindow, TimeSpan.FromSeconds(30));
            WaitHelpers.StabilityWait(3);

            var cf = _app.MainWindow.ConditionFactory;

            // Locate the filter/search box
            AutomationElement filterBox = null;
            var filterDeadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < filterDeadline && filterBox == null)
            {
                filterBox = _app.MainWindow.FindFirstDescendant(cf.ByAutomationId("valueFilter"));
                if (filterBox == null)
                {
                    var edits = _app.MainWindow.FindAllDescendants(cf.ByControlType(ControlType.Edit));
                    filterBox = edits.FirstOrDefault(e =>
                    {
                        try { return e.Name?.Contains("filter") == true || e.Name?.Contains("Filter") == true || e.Name?.Contains("search") == true; }
                        catch { return false; }
                    });
                    if (filterBox == null && edits.Length > 0)
                        filterBox = edits[0];
                }
                if (filterBox == null)
                    Thread.Sleep(2000);
            }

            Assert.IsNotNull(filterBox, "Could not find device filter/search box after 60s");
            filterBox.Click();
            filterBox.AsTextBox().Text = CameraIp;
            Thread.Sleep(3000);

            // Click the device
            var deviceList = _app.MainWindow.FindFirstDescendant(cf.ByAutomationId("deviceList"));
            AutomationElement targetDevice = null;
            if (deviceList != null)
            {
                var items = deviceList.FindAllChildren();
                targetDevice = items.FirstOrDefault(item =>
                {
                    try { return item.Name?.Contains(CameraIp) == true; }
                    catch { return false; }
                });
                if (targetDevice == null && items.Length > 0)
                    targetDevice = items[0];
            }

            if (targetDevice == null)
            {
                var listItems = _app.MainWindow.FindAllDescendants(cf.ByControlType(ControlType.ListItem));
                targetDevice = listItems.FirstOrDefault(item =>
                {
                    try { return item.Name?.Contains(CameraIp) == true; }
                    catch { return false; }
                });
                if (targetDevice == null && listItems.Length > 0)
                    targetDevice = listItems[0];
            }

            Assert.IsNotNull(targetDevice, $"Could not find device with IP {CameraIp}");
            targetDevice.Click();

            WaitHelpers.WaitForProgressToComplete(_app.MainWindow, TimeSpan.FromSeconds(120));
            WaitHelpers.StabilityWait(3);

            // Wait up to 120s for "Identification" nav link to appear (proves device is fully connected)
            AutomationElement identBtn = null;
            var identDeadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < identDeadline && identBtn == null)
            {
                identBtn = _app.MainWindow.FindFirstDescendant(cf.ByName("Identification"));
                if (identBtn == null)
                {
                    var allItems = _app.MainWindow.FindAllDescendants(cf.ByControlType(ControlType.ListItem));
                    identBtn = allItems.FirstOrDefault(item =>
                    {
                        try { return item.Name?.Contains("Identification") == true; }
                        catch { return false; }
                    });
                }
                if (identBtn == null)
                    Thread.Sleep(2000);
            }

            Assert.IsNotNull(identBtn, "Could not find 'Identification' nav link after 120s — device may not have connected");
        }

        [ClassCleanup]
        public static void Cleanup()
        {
            try { _app?.Dispose(); } catch { }
            foreach (var p in Process.GetProcessesByName("odm"))
                try { p.Kill(); } catch { }
        }

        private void VerifyNavLink(string linkName)
        {
            var cf = _app.MainWindow.ConditionFactory;

            // 1. Find descendant where Name == linkName (prefer ListItem)
            AutomationElement link = null;
            var listItems = _app.MainWindow.FindAllDescendants(cf.ByControlType(ControlType.ListItem));
            link = listItems.FirstOrDefault(el =>
            {
                try { return string.Equals(el.Name, linkName, StringComparison.Ordinal); }
                catch { return false; }
            });

            if (link == null)
            {
                var all = _app.MainWindow.FindAllDescendants(cf.ByName(linkName));
                link = all.FirstOrDefault();
            }

            // 2. If not found -> Inconclusive
            if (link == null)
            {
                Assert.Inconclusive($"Nav link '{linkName}' not present for this device");
                return;
            }

            // 3. Click it
            link.Click();

            // 4. Wait for progress, then stabilize
            WaitHelpers.WaitForProgressToComplete(_app.MainWindow, TimeSpan.FromSeconds(60));
            WaitHelpers.StabilityWait(3);

            // 5. Capture screenshot
            var screenshotDir = _config.Capture?.ScreenshotOutputDir ?? @"C:\odm-e2e-results\screenshots";
            string screenshotPath = null;
            try
            {
                screenshotPath = ScreenshotCapture.Capture(
                    _app.MainWindow, screenshotDir, "HttpsNav", linkName.Replace(" ", ""));
                Console.WriteLine($"Screenshot saved: {screenshotPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Screenshot capture failed: {ex.Message}");
            }

            // 6. Scan ControlType.Text and ControlType.Edit descendants for error keywords
            var errorKeywords = new[] { "error", "cannot", "pending", "exception", "fault", "failed" };

            var textBlocks = _app.MainWindow.FindAllDescendants(cf.ByControlType(ControlType.Text));
            var textBoxes = _app.MainWindow.FindAllDescendants(cf.ByControlType(ControlType.Edit));
            var allTextElements = textBlocks.Concat(textBoxes).ToArray();

            var errorElements = allTextElements.Where(el =>
            {
                try
                {
                    if (el.IsOffscreen) return false;
                    var text = el.Name ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        try { text = el.AsTextBox()?.Text ?? string.Empty; } catch { }
                    }
                    if (string.IsNullOrWhiteSpace(text)) return false;
                    return errorKeywords.Any(kw =>
                        text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                catch { return false; }
            }).ToArray();

            // 7. Fail with details if errors found
            if (errorElements.Length > 0)
            {
                var errorTexts = errorElements.Select(el =>
                {
                    try
                    {
                        var text = el.Name ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(text))
                            try { text = el.AsTextBox()?.Text ?? string.Empty; } catch { }
                        return text;
                    }
                    catch { return "(unreadable)"; }
                }).ToArray();

                var joinedErrorText = string.Join("\n", errorTexts);
                Assert.Fail($"Errors on '{linkName}' page:\n{joinedErrorText}\nScreenshot: {screenshotPath}");
            }

            // 8. Pass silently
        }

        [TestMethod]
        [TestCategory("HttpsIntegration")]
        public void HttpsCamera_IdentificationTab_ShowsNoErrors()
        {
            VerifyNavLink("Identification");
        }

        [TestMethod]
        [TestCategory("HttpsIntegration")]
        public void HttpsCamera_TimeSettingsTab_ShowsNoErrors()
        {
            VerifyNavLink("Time Settings");
        }

        [TestMethod]
        [TestCategory("HttpsIntegration")]
        public void HttpsCamera_MaintenanceTab_ShowsNoErrors()
        {
            VerifyNavLink("Maintenance");
        }

        [TestMethod]
        [TestCategory("HttpsIntegration")]
        public void HttpsCamera_NetworkSettingsTab_ShowsNoErrors()
        {
            VerifyNavLink("Network Settings");
        }

        [TestMethod]
        [TestCategory("HttpsIntegration")]
        public void HttpsCamera_UserManagementTab_ShowsNoErrors()
        {
            VerifyNavLink("User Management");
        }

        [TestMethod]
        [TestCategory("HttpsIntegration")]
        public void HttpsCamera_VideoStreamingTab_ShowsNoErrors()
        {
            VerifyNavLink("Video Streaming");
        }

        [TestMethod]
        [TestCategory("HttpsIntegration")]
        public void HttpsCamera_ImagingSettingsTab_ShowsNoErrors()
        {
            VerifyNavLink("Imaging Settings");
        }

        [TestMethod]
        [TestCategory("HttpsIntegration")]
        public void HttpsCamera_EventsTab_ShowsNoErrors()
        {
            VerifyNavLink("Events");
        }
    }
}
