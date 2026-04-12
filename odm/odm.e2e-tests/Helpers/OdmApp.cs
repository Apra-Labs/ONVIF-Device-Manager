using System;
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
            _app = Application.Launch(exePath);
            MainWindow = _app.GetMainWindow(_automation, timeout ?? TimeSpan.FromSeconds(30));
            MainWindow.Move(0, 0);
            // Resize via the UIA Transform pattern
            if (MainWindow.Patterns.Transform.IsSupported)
                MainWindow.Patterns.Transform.Pattern.Resize(1024, 768);
        }

        public void Dispose()
        {
            try { _app?.Close(); } catch { /* best effort */ }
            _automation?.Dispose();
        }
    }
}
