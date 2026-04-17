using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;
using onvif.services;

namespace odm.tests
{
    /// <summary>
    /// Multi-camera smoke sweep.  Reads a cameras.json config, runs
    /// Discover → Login → GetProfiles → GetStreamUri → TCP-reachability
    /// against every listed camera, then writes a Markdown report.
    ///
    /// Nothing is asserted — every step is allowed to fail.  The test
    /// is Inconclusive when no cameras.json is found; it is always green
    /// when the file is present (report captures what succeeded/failed).
    ///
    /// cameras.json format (place next to the test binary or any ancestor dir):
    /// {
    ///   "cameras": [
    ///     { "ip": "10.102.10.7",  "user": "admin", "pass": ""      },
    ///     { "ip": "10.102.10.97", "user": "admin", "pass": "admin" },
    ///     { "ip": "192.168.1.190","user": "admin", "pass": ""      }
    ///   ]
    /// }
    ///
    /// Alternatively, point ODM_SWEEP_CONFIG to the full path of the JSON file.
    ///
    /// Report is written to the same directory as cameras.json, or to
    /// ODM_SWEEP_REPORT_DIR if that env var is set.
    /// </summary>
    [TestClass]
    public class CameraCompatibilitySweepTests
    {
        // ----------------------------------------------------------------
        // Data-contract types for cameras.json
        // ----------------------------------------------------------------

        [DataContract]
        private class CameraEntry
        {
            [DataMember(Name = "ip")]   public string Ip   { get; set; }
            [DataMember(Name = "user")] public string User { get; set; }
            [DataMember(Name = "pass")] public string Pass { get; set; }
        }

        [DataContract]
        private class SweepConfig
        {
            [DataMember(Name = "cameras")]
            public List<CameraEntry> Cameras { get; set; }
        }

        // ----------------------------------------------------------------
        // Per-camera result
        // ----------------------------------------------------------------

        private class StepResult
        {
            public bool   Ok    { get; set; }
            public string Value { get; set; }   // extra info (URI, count, …)
            public string Error { get; set; }

            public static StepResult Pass(string value = null) =>
                new StepResult { Ok = true, Value = value };
            public static StepResult Fail(string error) =>
                new StepResult { Ok = false, Error = Truncate(error, 120) };
        }

        private class CameraResult
        {
            public CameraEntry    Camera       { get; set; }
            public StepResult     Connect      { get; set; }
            public StepResult     DeviceInfo   { get; set; }
            public StepResult     Profiles     { get; set; }
            public StepResult     StreamUri    { get; set; }
            public StepResult     StreamTcp    { get; set; }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static T Run<T>(FSharpAsync<T> async, int timeoutMs = 30000)
        {
            return FSharpAsync.RunSynchronously(
                async,
                FSharpOption<int>.Some(timeoutMs),
                FSharpOption<CancellationToken>.None);
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static bool TcpReachable(string host, int port, int timeoutMs = 5000)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    client.EndConnect(ar);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------------------------------------------
        // Config loading
        // ----------------------------------------------------------------

        private static string FindConfigFile()
        {
            // 1. Explicit env var
            var explicit_ = Environment.GetEnvironmentVariable("ODM_SWEEP_CONFIG");
            if (!string.IsNullOrEmpty(explicit_) && File.Exists(explicit_))
                return explicit_;

            // 2. Walk up from test binary directory
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "cameras.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        private static SweepConfig LoadConfig(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            {
                var ser = new DataContractJsonSerializer(typeof(SweepConfig));
                return (SweepConfig)ser.ReadObject(ms);
            }
        }

        // ----------------------------------------------------------------
        // Per-camera smoke test
        // ----------------------------------------------------------------

        private static CameraResult TestCamera(CameraEntry cam)
        {
            var result = new CameraResult { Camera = cam };

            // Global TLS settings (idempotent — safe to call per-camera)
            ServicePointManager.SecurityProtocol  = SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            var cred    = new NetworkCredential(cam.User ?? "admin", cam.Pass ?? "");
            var factory = new NvtSessionFactory(cred);

            // Step 1 — Connect (HTTP probe; factory will upgrade to HTTPS if needed)
            INvtSession session = null;
            try
            {
                var uri = new Uri(string.Format("http://{0}/onvif/device_service", cam.Ip));
                session         = Run(factory.CreateSession(new[] { uri }), 30000);
                result.Connect  = StepResult.Pass();
            }
            catch (Exception ex)
            {
                result.Connect    = StepResult.Fail(ex.Message);
                result.DeviceInfo = StepResult.Fail("skipped — connect failed");
                result.Profiles   = StepResult.Fail("skipped — connect failed");
                result.StreamUri  = StepResult.Fail("skipped — connect failed");
                result.StreamTcp  = StepResult.Fail("skipped — connect failed");
                return result;
            }

            // Step 2 — GetDeviceInformation
            try
            {
                var info          = Run(session.GetDeviceInformation(), 15000);
                var label         = string.Format("{0} {1}", info.Manufacturer ?? "", info.Model ?? "").Trim();
                result.DeviceInfo = StepResult.Pass(string.IsNullOrEmpty(label) ? "ok" : label);
            }
            catch (Exception ex)
            {
                result.DeviceInfo = StepResult.Fail(ex.Message);
            }

            // Step 3 — GetProfiles
            Profile[] profiles = null;
            try
            {
                profiles         = Run(session.GetProfiles(), 15000);
                result.Profiles  = StepResult.Pass(string.Format("{0} profile(s)", profiles == null ? 0 : profiles.Length));
            }
            catch (Exception ex)
            {
                result.Profiles  = StepResult.Fail(ex.Message);
            }

            // Step 4 — GetStreamUri (first profile)
            string streamUriStr = null;
            if (profiles != null && profiles.Length > 0)
            {
                try
                {
                    var setup = new StreamSetup
                    {
                        stream    = StreamType.rtpUnicast,
                        transport = new Transport { protocol = TransportProtocol.rtsp }
                    };
                    var mediaUri   = Run(session.GetStreamUri(setup, profiles[0].token), 15000);
                    streamUriStr   = mediaUri != null ? mediaUri.uri : null;
                    result.StreamUri = streamUriStr != null
                        ? StepResult.Pass(streamUriStr)
                        : StepResult.Fail("null URI returned");
                }
                catch (Exception ex)
                {
                    result.StreamUri = StepResult.Fail(ex.Message);
                }
            }
            else
            {
                result.StreamUri = StepResult.Fail("skipped — no profiles");
            }

            // Step 5 — TCP reachability of stream host:port
            if (!string.IsNullOrEmpty(streamUriStr) && result.StreamUri.Ok)
            {
                try
                {
                    var uri    = new Uri(streamUriStr);
                    int port   = uri.IsDefaultPort ? 554 : uri.Port;
                    bool reach = TcpReachable(uri.Host, port);
                    result.StreamTcp = reach
                        ? StepResult.Pass(string.Format("{0}:{1}", uri.Host, port))
                        : StepResult.Fail(string.Format("{0}:{1} not reachable (TCP)", uri.Host, port));
                }
                catch (Exception ex)
                {
                    result.StreamTcp = StepResult.Fail("URI parse error: " + ex.Message);
                }
            }
            else
            {
                result.StreamTcp = StepResult.Fail("skipped — no stream URI");
            }

            return result;
        }

        // ----------------------------------------------------------------
        // Report generation
        // ----------------------------------------------------------------

        private static string BuildReport(List<CameraResult> results, string configPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Camera Compatibility Sweep Report");
            sb.AppendLine();
            sb.AppendLine(string.Format("**Date:** {0:yyyy-MM-dd HH:mm:ss} UTC", System.DateTime.UtcNow));
            sb.AppendLine(string.Format("**Config:** {0}", configPath));
            sb.AppendLine(string.Format("**Cameras tested:** {0}", results.Count));
            sb.AppendLine();

            // Summary table
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Camera | Connect | DeviceInfo | Profiles | StreamURI | StreamTCP |");
            sb.AppendLine("|--------|:-------:|:----------:|:--------:|:---------:|:---------:|");

            foreach (var r in results)
            {
                var cred = string.Format("{0}/{1}", r.Camera.User, string.IsNullOrEmpty(r.Camera.Pass) ? "<blank>" : "****");
                sb.AppendLine(string.Format("| {0} ({1}) | {2} | {3} | {4} | {5} | {6} |",
                    r.Camera.Ip,
                    cred,
                    Flag(r.Connect),
                    Flag(r.DeviceInfo),
                    Flag(r.Profiles),
                    Flag(r.StreamUri),
                    Flag(r.StreamTcp)));
            }

            sb.AppendLine();

            // Detail section per camera
            sb.AppendLine("## Detail");
            sb.AppendLine();
            foreach (var r in results)
            {
                sb.AppendLine(string.Format("### {0}", r.Camera.Ip));
                sb.AppendLine();
                AppendStep(sb, "Connect",    r.Connect);
                AppendStep(sb, "DeviceInfo", r.DeviceInfo);
                AppendStep(sb, "Profiles",   r.Profiles);
                AppendStep(sb, "StreamURI",  r.StreamUri);
                AppendStep(sb, "StreamTCP",  r.StreamTcp);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Flag(StepResult r)
        {
            if (r == null) return "—";
            return r.Ok ? "✓" : "✗";
        }

        private static void AppendStep(StringBuilder sb, string name, StepResult r)
        {
            if (r == null) { sb.AppendLine(string.Format("- **{0}:** —", name)); return; }
            if (r.Ok)
                sb.AppendLine(string.Format("- **{0}:** PASS{1}", name,
                    string.IsNullOrEmpty(r.Value) ? "" : " — " + r.Value));
            else
                sb.AppendLine(string.Format("- **{0}:** FAIL — {1}", name, r.Error));
        }

        // ----------------------------------------------------------------
        // The single test method
        // ----------------------------------------------------------------

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Sweep")]
        public void Sweep_AllCameras_ProduceCompatibilityReport()
        {
            // Locate config
            var configPath = FindConfigFile();
            if (configPath == null)
                Assert.Inconclusive(
                    "cameras.json not found (searched test bin dir ancestors) and ODM_SWEEP_CONFIG not set. " +
                    "Create cameras.json next to the test DLL with a list of camera IPs and credentials.");

            SweepConfig config;
            try   { config = LoadConfig(configPath); }
            catch (Exception ex)
            { Assert.Fail("Failed to parse cameras.json: " + ex.Message); return; }

            if (config.Cameras == null || config.Cameras.Count == 0)
                Assert.Inconclusive("cameras.json contains no camera entries.");

            // Run sweep
            var results = new List<CameraResult>();
            foreach (var cam in config.Cameras)
            {
                Console.WriteLine("Testing {0} ({1}/{2}) …",
                    cam.Ip, cam.User, string.IsNullOrEmpty(cam.Pass) ? "<blank>" : "****");
                var r = TestCamera(cam);
                results.Add(r);

                Console.WriteLine("  Connect={0}  Profiles={1}  StreamURI={2}  StreamTCP={3}",
                    r.Connect   != null && r.Connect.Ok   ? "OK" : "FAIL",
                    r.Profiles  != null && r.Profiles.Ok  ? r.Profiles.Value : "FAIL",
                    r.StreamUri != null && r.StreamUri.Ok ? r.StreamUri.Value : "FAIL",
                    r.StreamTcp != null && r.StreamTcp.Ok ? "OK" : "FAIL");
            }

            // Generate report
            var report = BuildReport(results, configPath);

            // Write to file
            var reportDir = Environment.GetEnvironmentVariable("ODM_SWEEP_REPORT_DIR")
                ?? Path.GetDirectoryName(configPath);
            var reportPath = Path.Combine(reportDir,
                string.Format("camera-sweep-{0:yyyyMMdd-HHmmss}.md", System.DateTime.UtcNow));
            try   { File.WriteAllText(reportPath, report, Encoding.UTF8); }
            catch { /* non-fatal — report still printed to console */ }

            // Print to console (captured by vstest output)
            Console.WriteLine();
            Console.WriteLine("======== CAMERA SWEEP REPORT ========");
            Console.WriteLine(report);
            Console.WriteLine("Report written to: " + reportPath);

            // Count passes for informational summary (never fail)
            int passCount = 0;
            foreach (var r in results)
                if (r.Connect != null && r.Connect.Ok) passCount++;

            Console.WriteLine(string.Format("{0}/{1} cameras connected successfully.", passCount, results.Count));

            // The test is always green — the report captures the outcome.
            // Assert Inconclusive only if zero cameras could connect, so the
            // CI result is clearly distinguishable from "all cameras healthy".
            if (passCount == 0)
                Assert.Inconclusive(
                    string.Format("0/{0} cameras connected. Check network/credentials. Report: {1}",
                        results.Count, reportPath));
        }
    }
}
