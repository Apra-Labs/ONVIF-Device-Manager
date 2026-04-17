using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;
using odm.ui.core;
using onvif.services;

namespace odm.tests
{
    /// <summary>
    /// Camera compatibility sweep — mirrors the real ODM UI workflow:
    ///   1. Read credentials.dat via CredentialStore (DPAPI-decrypted) and report count.
    ///   2. Run ONVIF WS-Discovery for 5 s; report discovered cameras.
    ///   3. For each camera try every stored credential (+ anonymous); report first that works.
    ///   4. For each authenticated camera get the first profile's RTSP stream URI (no credential logging).
    ///   5. For each stream URI spawn HostedPlayer headlessly for 5 s and report whether it played.
    ///
    /// Nothing is asserted — every step is allowed to fail.  The test is Inconclusive when
    /// no cameras are discovered or no camera can be authenticated; otherwise it is always green.
    /// The Markdown report captures per-camera outcomes.
    ///
    /// Step 5 uses a TCP socket probe to the RTSP port (default 554) instead of live playback.
    ///
    /// Optional: set ODM_SWEEP_REPORT_DIR to override where the report is written.
    /// </summary>
    [TestClass]
    public class CameraCompatibilitySweepTests
    {
        // ----------------------------------------------------------------
        // Per-step result
        // ----------------------------------------------------------------

        private class StepResult
        {
            public bool   Ok    { get; set; }
            public string Value { get; set; }
            public string Error { get; set; }

            public static StepResult Pass(string value = null) =>
                new StepResult { Ok = true, Value = value };
            public static StepResult Fail(string error) =>
                new StepResult { Ok = false, Error = Truncate(error, 120) };
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ----------------------------------------------------------------
        // Per-camera result
        // ----------------------------------------------------------------

        private class CameraResult
        {
            public string     EndpointRef  { get; set; }
            public Uri[]      Uris         { get; set; }
            public StepResult Login        { get; set; }
            public string     AuthUser     { get; set; }  // username that worked — no password logged
            public StepResult Profiles     { get; set; }
            public StepResult StreamUri    { get; set; }
            public StepResult StreamPlay   { get; set; }
        }

        // ----------------------------------------------------------------
        // WS-Discovery node collector
        // ----------------------------------------------------------------

        private class NodeCollector : IObserver<INvtNode>
        {
            public readonly List<INvtNode> Nodes = new List<INvtNode>();
            public void OnNext(INvtNode value)  { lock (Nodes) Nodes.Add(value); }
            public void OnError(Exception error) { }
            public void OnCompleted()            { }
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

        private static List<INvtNode> DiscoverCameras(TimeSpan duration)
        {
            var manager = new NvtManager();
            var nvtMgr  = (INvtManager)manager;
            var collector = new NodeCollector();

            using (nvtMgr.Observe().Subscribe(collector))
            using (nvtMgr.Discover(duration))
            {
                // Wait for the probe window to expire, plus a small buffer.
                Thread.Sleep(duration + TimeSpan.FromSeconds(1));
            }

            return collector.Nodes;
        }

        // ----------------------------------------------------------------
        // Per-camera smoke test
        // ----------------------------------------------------------------

        private static CameraResult TestCamera(INvtNode node, IList<Account> creds)
        {
            var result = new CameraResult
            {
                EndpointRef = node.identity.endpointReference,
                Uris        = node.identity.uris
            };

            // Global TLS settings (idempotent — safe to call per-camera)
            ServicePointManager.SecurityProtocol  = SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            // Step 3 — try each stored credential, then anonymous
            INvtSession session  = null;
            string      authUser = null;
            Account?    authCred = null;

            foreach (var cred in creds)
            {
                try
                {
                    var nc      = new NetworkCredential(cred.Name, cred.Password);
                    var factory = new NvtSessionFactory(nc);
                    session  = Run(factory.CreateSession(node.identity.uris), 15000);
                    authUser = string.IsNullOrEmpty(cred.Name) ? "<blank>" : cred.Name;
                    authCred = cred;
                    break;
                }
                catch { /* try next */ }
            }

            if (session == null)
            {
                // Last resort: anonymous
                try
                {
                    var nc      = new NetworkCredential(string.Empty, string.Empty);
                    var factory = new NvtSessionFactory(nc);
                    session  = Run(factory.CreateSession(node.identity.uris), 15000);
                    authUser = "<anonymous>";
                    authCred = Account.Anonymous;
                }
                catch { /* fall through */ }
            }

            if (session == null)
            {
                result.Login      = StepResult.Fail("no credential worked");
                result.Profiles   = StepResult.Fail("skipped — login failed");
                result.StreamUri  = StepResult.Fail("skipped — login failed");
                result.StreamPlay = StepResult.Fail("skipped — login failed");
                return result;
            }

            result.Login    = StepResult.Pass();
            result.AuthUser = authUser;

            // Step 4a — GetProfiles
            Profile[] profiles = null;
            try
            {
                profiles         = Run(session.GetProfiles(), 15000);
                result.Profiles  = StepResult.Pass(
                    string.Format("{0} profile(s)", profiles == null ? 0 : profiles.Length));
            }
            catch (Exception ex)
            {
                result.Profiles = StepResult.Fail(ex.Message);
            }

            // Step 4b — GetStreamUri for first profile
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
                    var mediaUri    = Run(session.GetStreamUri(setup, profiles[0].token), 15000);
                    streamUriStr    = mediaUri != null ? mediaUri.uri : null;
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

            // Step 5 — TCP reachability probe to the RTSP port
            if (!string.IsNullOrEmpty(streamUriStr) && result.StreamUri.Ok)
            {
                result.StreamPlay = TestRtspReachable(streamUriStr);
            }
            else
            {
                result.StreamPlay = StepResult.Fail("skipped — no stream URI");
            }

            return result;
        }

        private static StepResult TestRtspReachable(string streamUri)
        {
            try
            {
                var uri  = new Uri(streamUri);
                int port = uri.IsDefaultPort ? 554 : uri.Port;
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(uri.Host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(5000))
                        return StepResult.Fail(string.Format("{0}:{1} TCP timeout", uri.Host, port));
                    client.EndConnect(ar);
                    return StepResult.Pass(string.Format("{0}:{1} reachable", uri.Host, port));
                }
            }
            catch (Exception ex)
            {
                return StepResult.Fail("TCP probe: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------
        // Report generation
        // ----------------------------------------------------------------

        private static string BuildReport(List<CameraResult> results, int credCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Camera Compatibility Sweep Report");
            sb.AppendLine();
            sb.AppendLine(string.Format("**Date:** {0:yyyy-MM-dd HH:mm:ss} UTC", System.DateTime.UtcNow));
            sb.AppendLine(string.Format("**Stored credentials:** {0}", credCount));
            sb.AppendLine(string.Format("**Cameras discovered:** {0}", results.Count));
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Camera endpoint | Auth user | Login | Profiles | StreamURI | RTSP TCP |");
            sb.AppendLine("|----------------|:---------:|:-----:|:--------:|:---------:|:--------:|");

            foreach (var r in results)
            {
                sb.AppendLine(string.Format("| {0} | {1} | {2} | {3} | {4} | {5} |",
                    Truncate(r.EndpointRef, 50),
                    r.AuthUser ?? "—",
                    Flag(r.Login),
                    Flag(r.Profiles),
                    Flag(r.StreamUri),
                    Flag(r.StreamPlay)));
            }

            sb.AppendLine();
            sb.AppendLine("## Detail");
            sb.AppendLine();

            foreach (var r in results)
            {
                sb.AppendLine(string.Format("### {0}", r.EndpointRef));
                sb.AppendLine();
                if (r.Uris != null)
                    foreach (var u in r.Uris)
                        sb.AppendLine(string.Format("- endpoint URI: `{0}`", u));
                sb.AppendLine();
                AppendStep(sb, "Login",     r.Login,      r.AuthUser != null ? "user=" + r.AuthUser : null);
                AppendStep(sb, "Profiles",  r.Profiles,   null);
                AppendStep(sb, "StreamURI", r.StreamUri,  null);
                AppendStep(sb, "RTSP TCP",  r.StreamPlay, null);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Flag(StepResult r)
        {
            if (r == null) return "—";
            return r.Ok ? "✓" : "✗";
        }

        private static void AppendStep(StringBuilder sb, string name, StepResult r, string extra)
        {
            if (r == null) { sb.AppendLine(string.Format("- **{0}:** —", name)); return; }
            if (r.Ok)
            {
                var detail = new List<string>();
                if (!string.IsNullOrEmpty(r.Value)) detail.Add(r.Value);
                if (!string.IsNullOrEmpty(extra))   detail.Add(extra);
                sb.AppendLine(string.Format("- **{0}:** PASS{1}", name,
                    detail.Count > 0 ? " — " + string.Join(", ", detail) : ""));
            }
            else
            {
                sb.AppendLine(string.Format("- **{0}:** FAIL — {1}", name, r.Error));
            }
        }

        // ----------------------------------------------------------------
        // The single test method
        // ----------------------------------------------------------------

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Sweep")]
        public void Sweep_AllCameras_ProduceCompatibilityReport()
        {
            // Step 1 — load credentials from CredentialStore (DPAPI)
            var creds = CredentialStore.Instance.GetAll();
            Console.WriteLine("CredentialStore: {0} credential(s) loaded.", creds.Count);
            if (creds.Count == 0)
                Console.WriteLine("No stored credentials — anonymous login will be attempted per camera.");

            // Step 2 — WS-Discovery
            Console.WriteLine("Running WS-Discovery (5 s)…");
            List<INvtNode> nodes;
            try
            {
                nodes = DiscoverCameras(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Assert.Inconclusive("WS-Discovery failed: " + ex.Message);
                return;
            }
            Console.WriteLine("Discovered {0} camera(s).", nodes.Count);

            if (nodes.Count == 0)
            {
                Assert.Inconclusive(
                    "No cameras found via WS-Discovery. Check that the test host is on the camera network.");
                return;
            }

            // Steps 3–5 — per-camera sweep
            var results = new List<CameraResult>();
            foreach (var node in nodes)
            {
                Console.WriteLine("Testing: {0}", node.identity.endpointReference);
                var r = TestCamera(node, creds);
                results.Add(r);

                Console.WriteLine("  Login={0} ({1})  Profiles={2}  StreamURI={3}  RTSP-TCP={4}",
                    r.Login  != null && r.Login.Ok  ? "OK"  : "FAIL",
                    r.AuthUser ?? "—",
                    r.Profiles != null && r.Profiles.Ok  ? r.Profiles.Value : "FAIL",
                    r.StreamUri != null && r.StreamUri.Ok ? r.StreamUri.Value : "FAIL",
                    r.StreamPlay != null && r.StreamPlay.Ok ? r.StreamPlay.Value : "FAIL");
            }

            // Generate report
            var report    = BuildReport(results, creds.Count);
            var reportDir = Environment.GetEnvironmentVariable("ODM_SWEEP_REPORT_DIR")
                         ?? AppDomain.CurrentDomain.BaseDirectory;
            var reportPath = Path.Combine(reportDir,
                string.Format("camera-sweep-{0:yyyyMMdd-HHmmss}.md", System.DateTime.UtcNow));
            try   { File.WriteAllText(reportPath, report, Encoding.UTF8); }
            catch { /* non-fatal */ }

            Console.WriteLine();
            Console.WriteLine("======== CAMERA SWEEP REPORT ========");
            Console.WriteLine(report);
            Console.WriteLine("Report written to: " + reportPath);

            int loginCount = 0;
            foreach (var r in results)
                if (r.Login != null && r.Login.Ok) loginCount++;

            Console.WriteLine("{0}/{1} cameras authenticated.", loginCount, results.Count);

            // The test is always green — the report captures outcomes.
            // Mark Inconclusive only when zero cameras could be authenticated, so CI
            // can distinguish "all cameras healthy" from "nothing worked".
            if (loginCount == 0)
                Assert.Inconclusive(string.Format(
                    "0/{0} cameras authenticated. Check stored credentials or network. Report: {1}",
                    results.Count, reportPath));
        }
    }
}
