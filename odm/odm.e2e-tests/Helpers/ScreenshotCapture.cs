using System;
using System.IO;
using FlaUI.Core.AutomationElements;

namespace odm.e2e_tests.Helpers
{
    public static class ScreenshotCapture
    {
        public static string Capture(Window window, string outputDir, string scenarioName, string stepName)
        {
            Directory.CreateDirectory(outputDir);
            var filename = $"{scenarioName}_{stepName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
            var path = Path.Combine(outputDir, filename);
            var image = FlaUI.Core.Capturing.Capture.Element(window);
            image.ToFile(path);
            return path;
        }
    }
}
