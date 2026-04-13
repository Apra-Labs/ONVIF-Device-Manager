using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SWA = System.Windows.Automation;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace odm.e2e_tests.Helpers
{
    public class OdmApp : IDisposable
    {
        private Application _app;
        private AutomationBase _automation;

        public Window MainWindow { get; private set; }

        public void Launch(string exePath, TimeSpan? timeout = null)
        {
            _automation = new UIA3Automation();

            // Close any running ODM instances before launching fresh.
            // Use System.Windows.Automation (UIA) to close elevated processes that can't be killed.
            foreach (var proc in Process.GetProcessesByName("odm"))
            {
                try
                {
                    // Try direct kill first (works when test runner matches process integrity)
                    proc.Kill();
                }
                catch
                {
                    // For elevated ODM, use the WindowPattern.Close() via UIA which crosses
                    // integrity levels (UIA is exempt from UIPI).
                    try
                    {
                        var hwnd = proc.MainWindowHandle;
                        if (hwnd != IntPtr.Zero)
                        {
                            var element = SWA.AutomationElement.FromHandle(hwnd);
                            var wp = element?.GetCurrentPattern(SWA.WindowPattern.Pattern) as SWA.WindowPattern;
                            wp?.Close();
                        }
                    }
                    catch { }
                }
            }

            // Wait for ODM to fully exit before launching a fresh instance
            var exitDeadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < exitDeadline && Process.GetProcessesByName("odm").Any())
                Thread.Sleep(500);

            _app = Application.Launch(exePath);
            MainWindow = _app.GetMainWindow(_automation, timeout ?? TimeSpan.FromSeconds(30));

            // Wait for device list UI to be ready (filter box must be visible).
            // This is more reliable than waiting for progress bars to disappear.
            WaitForDeviceListReady(MainWindow, TimeSpan.FromSeconds(60));

            try
            {
                MainWindow.Move(0, 0);
                if (MainWindow.Patterns.Transform.IsSupported)
                    MainWindow.Patterns.Transform.Pattern.Resize(1024, 768);
            }
            catch (InvalidOperationException) { }
        }

        /// <summary>
        /// Polls until the device-list filter box (AutomationId "valueFilter") appears in the
        /// main window, or until the timeout expires.
        /// </summary>
        private static void WaitForDeviceListReady(Window window, TimeSpan timeout)
        {
            if (window == null) return;
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var cf = window.ConditionFactory;
                    var el = window.FindFirstDescendant(cf.ByAutomationId("valueFilter"));
                    if (el != null)
                        return;
                }
                catch { }
                Thread.Sleep(500);
            }
        }

        public void Dispose()
        {
            try { _app?.Close(); } catch { /* best effort */ }
            _automation?.Dispose();
        }
    }
}
