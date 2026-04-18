using System;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace odm.e2e_tests.Helpers
{
    public static class WaitHelpers
    {
        /// <summary>
        /// Polls for all progress bar controls to become inactive (disabled or offscreen).
        /// Returns when no active (enabled + visible) progress bars remain, or on timeout.
        /// </summary>
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
            // Timeout — caller captures screenshot and decides whether to fail
        }

        /// <summary>
        /// Waits after progress completes for the UI to settle before screenshot capture.
        /// </summary>
        public static void StabilityWait(int seconds = 2)
        {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }
    }
}
