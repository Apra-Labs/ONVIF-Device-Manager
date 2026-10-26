using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace odm.e2e_tests.Config
{
    public class SmokeConfig
    {
        [JsonProperty("odm")]
        public OdmSettings Odm { get; set; }

        [JsonProperty("cameraProfiles")]
        public List<CameraProfile> CameraProfiles { get; set; } = new List<CameraProfile>();

        [JsonProperty("timeouts")]
        public TimeoutSettings Timeouts { get; set; } = new TimeoutSettings();

        [JsonProperty("capture")]
        public CaptureSettings Capture { get; set; }

        [JsonProperty("claude")]
        public ClaudeSettings Claude { get; set; }

        public static SmokeConfig Load()
        {
            var path = Environment.GetEnvironmentVariable("ODM_SMOKE_CONFIG")
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-config.json");
            var json = File.ReadAllText(path);
            var config = JsonConvert.DeserializeObject<SmokeConfig>(json);
            config.ResolveEnvVars();
            return config;
        }

        private void ResolveEnvVars()
        {
            foreach (var profile in CameraProfiles)
            {
                profile.Username = ResolveEnvVar(profile.Username);
                profile.Password = ResolveEnvVar(profile.Password);
            }
        }

        private static string ResolveEnvVar(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("$env:"))
                return value;
            var varName = value.Substring(5);
            return Environment.GetEnvironmentVariable(varName) ?? string.Empty;
        }
    }

    public class OdmSettings
    {
        [JsonProperty("exePath")]
        public string ExePath { get; set; }

        [JsonProperty("expectedVersion")]
        public string ExpectedVersion { get; set; }

        [JsonProperty("launchTimeoutSeconds")]
        public int LaunchTimeoutSeconds { get; set; } = 30;
    }

    public class CameraProfile
    {
        [JsonProperty("classifyAs")]
        public string ClassifyAs { get; set; }

        [JsonProperty("manufacturerContains")]
        public string ManufacturerContains { get; set; }

        [JsonProperty("modelContains")]
        public string ModelContains { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }

        [JsonProperty("httpPort")]
        public int HttpPort { get; set; } = 80;

        [JsonProperty("httpsPort")]
        public int? HttpsPort { get; set; }

        [JsonProperty("suites")]
        public List<string> Suites { get; set; } = new List<string>();

        public bool HasSuite(string suiteName)
        {
            return Suites != null && Suites.Contains(suiteName);
        }
    }

    public class TimeoutSettings
    {
        [JsonProperty("discoverySeconds")]
        public int DiscoverySeconds { get; set; } = 30;

        [JsonProperty("connectionSeconds")]
        public int ConnectionSeconds { get; set; } = 20;

        [JsonProperty("videoLoadSeconds")]
        public int VideoLoadSeconds { get; set; } = 15;

        [JsonProperty("progressSettleSeconds")]
        public int ProgressSettleSeconds { get; set; } = 2;
    }

    public class CaptureSettings
    {
        [JsonProperty("resolution")]
        public Resolution Resolution { get; set; } = new Resolution();

        [JsonProperty("screenshotOutputDir")]
        public string ScreenshotOutputDir { get; set; } = @"C:\odm-e2e-results\screenshots";

        [JsonProperty("reportOutputDir")]
        public string ReportOutputDir { get; set; } = @"C:\odm-e2e-results\reports";
    }

    public class Resolution
    {
        [JsonProperty("width")]
        public int Width { get; set; } = 1024;

        [JsonProperty("height")]
        public int Height { get; set; } = 768;
    }

    public class ClaudeSettings
    {
        [JsonProperty("model")]
        public string Model { get; set; } = "claude-sonnet-4-6";

        [JsonProperty("maxTokens")]
        public int MaxTokens { get; set; } = 1024;
    }
}
