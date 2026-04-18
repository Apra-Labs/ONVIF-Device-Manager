using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.e2e_tests.Config;
using odm.e2e_tests.Helpers;

namespace odm.e2e_tests.Tests
{
    [TestClass]
    public class HttpsIdentificationTests
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

            ctx.WriteLine($"MainWindow null: {_app.MainWindow == null}");
            if (_app.MainWindow != null)
            {
                string title = "?"; try { title = _app.MainWindow.Title; } catch { }
                ctx.WriteLine($"MainWindow title: '{title}'");
                // Log all Edit controls
                try
                {
                    var allEdits = _app.MainWindow.FindAllDescendants(cf2 => cf2.ByControlType(ControlType.Edit));
                    ctx.WriteLine($"Edit controls found: {allEdits.Length}");
                }
                catch (Exception ex2) { ctx.WriteLine($"FindAllDescendants(Edit) threw: {ex2.Message}"); }
                // Log all descendants count
                try
                {
                    var allDesc = _app.MainWindow.FindAllDescendants();
                    ctx.WriteLine($"Total descendants: {allDesc.Length}");
                }
                catch (Exception ex3) { ctx.WriteLine($"FindAllDescendants() threw: {ex3.Message}"); }
            }

            WaitHelpers.WaitForProgressToComplete(_app.MainWindow, TimeSpan.FromSeconds(30));
            WaitHelpers.StabilityWait(3);
        }

        [ClassCleanup]
        public static void Cleanup()
        {
            try { _app?.Dispose(); } catch { }
            foreach (var p in Process.GetProcessesByName("odm"))
                try { p.Kill(); } catch { }
        }

        [TestMethod]
        [TestCategory("HttpsIntegration")]
        public void HttpsCamera_IdentificationTab_ShowsNoErrors()
        {
            var cf = _app.MainWindow.ConditionFactory;

            // Step b: poll for the filter/search box — ODM may still be loading the device list view
            AutomationElement filterBox = null;
            var filterDeadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < filterDeadline && filterBox == null)
            {
                filterBox = _app.MainWindow.FindFirstDescendant(cf.ByAutomationId("valueFilter"));
                if (filterBox == null)
                {
                    // Fallback: find by control type Edit in the filter area
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

            Assert.IsNotNull(filterBox, "Could not find device filter/search box after 60s — ODM device list may not have loaded");
            filterBox.Click();
            filterBox.AsTextBox().Text = CameraIp;
            Thread.Sleep(3000);

            // Step c: find and click the device in the list
            var sw = Stopwatch.StartNew();

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

            Assert.IsNotNull(targetDevice, $"Could not find device with IP {CameraIp} in device list");
            targetDevice.Click();

            WaitHelpers.WaitForProgressToComplete(_app.MainWindow, TimeSpan.FromSeconds(120));
            WaitHelpers.StabilityWait(3);

            sw.Stop();
            Console.WriteLine($"Device load completed in {sw.Elapsed.TotalSeconds:F1}s");

            // Step e: wait for Identification nav link to appear (device must finish connecting first)
            // HTTPS cameras can take >60s to connect — poll until the tab appears or we time out.
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

            Console.WriteLine($"Identification button found: {identBtn != null} (after {(identDeadline - DateTime.UtcNow.AddSeconds(120) + TimeSpan.FromSeconds(120)).TotalSeconds:F0}s wait)");
            Assert.IsNotNull(identBtn, "Could not find 'Identification' navigation button after 120s — device may not have connected");
            identBtn.Click();

            WaitHelpers.WaitForProgressToComplete(_app.MainWindow, TimeSpan.FromSeconds(60));
            WaitHelpers.StabilityWait(3);

            // Step f: search for error indicators in visible text elements
            var screenshotDir = _config.Capture?.ScreenshotOutputDir ?? @"C:\odm-e2e-results\screenshots";
            string screenshotPath = null;
            try
            {
                screenshotPath = ScreenshotCapture.Capture(
                    _app.MainWindow, screenshotDir, "HttpsIdentification", "AfterIdent");
                Console.WriteLine($"Screenshot saved: {screenshotPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Screenshot capture failed: {ex.Message}");
            }

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
                    return errorKeywords.Any(kw =>
                        text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                catch { return false; }
            }).ToArray();

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

                var errorMsg = string.Join("\n", errorTexts);
                Assert.Fail($"Found error text in Identification view:\n{errorMsg}\nScreenshot: {screenshotPath}");
            }
        }
    }
}
