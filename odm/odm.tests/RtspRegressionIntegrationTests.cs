using System;
using System.IO;
using System.Net;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;
using onvif.services;

namespace odm.tests
{
    /// <summary>
    /// Integration tests for RTSP URI regression (#30). Cameras that worked in v2.2.252.x
    /// should return an unmodified rtsp:// URI from GetStreamUri. Bug #30 hypothesis:
    /// UpgradeScheme was incorrectly rewriting RTSP URIs to https://. Phase 1 fix (#26)
    /// should resolve this.
    ///
    /// Set ODM_TEST_HOST to the camera IP (10.102.10.97 has RTSP).
    /// </summary>
    [TestClass]
    public class RtspRegressionIntegrationTests
    {
        private static string TestHost => Environment.GetEnvironmentVariable("ODM_TEST_HOST");
        private static string TestUser => Environment.GetEnvironmentVariable("ODM_TEST_USER") ?? "admin";
        private static string TestPass => Environment.GetEnvironmentVariable("ODM_TEST_PASS") ?? "";

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate))
                {
                    foreach (var line in File.ReadAllLines(candidate))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                        var parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length == 2 && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(parts[0].Trim())))
                            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
                    }
                    break;
                }
                dir = dir.Parent;
            }

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;
        }

        private static T Run<T>(FSharpAsync<T> async, int timeoutMs = 30000)
        {
            return FSharpAsync.RunSynchronously(async, FSharpOption<int>.Some(timeoutMs), null);
        }

        private static void SkipIfNoHost()
        {
            if (string.IsNullOrEmpty(TestHost))
                Assert.Inconclusive("ODM_TEST_HOST not set — skipping integration test");
        }

        private static INvtSession CreateSession()
        {
            var port = Environment.GetEnvironmentVariable("ODM_TEST_HTTP_PORT") ?? "80";
            var uri = new Uri(string.Format("http://{0}:{1}/onvif/device_service", TestHost, port));
            NetworkCredential creds = string.IsNullOrEmpty(TestUser)
                ? null
                : new NetworkCredential(TestUser, TestPass);
            var factory = new NvtSessionFactory(creds);
            return Run(factory.CreateSession(new[] { uri }));
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetStreamUri_Camera_ReturnsUnmodifiedRtspUrl()
        {
            SkipIfNoHost();

            var session = CreateSession();
            var profiles = Run(session.GetProfiles(), 20000);
            Assert.IsNotNull(profiles, "GetProfiles returned null");
            Assert.IsTrue(profiles.Length > 0,
                string.Format("Expected at least one profile from {0}", TestHost));

            var profileToken = profiles[0].token;
            var streamSetup = new StreamSetup
            {
                stream = StreamType.rtpUnicast,
                transport = new Transport { protocol = TransportProtocol.rtsp }
            };

            var mediaUri = Run(session.GetStreamUri(streamSetup, profileToken), 20000);
            Assert.IsNotNull(mediaUri, "GetStreamUri returned null MediaUri");
            Assert.IsFalse(string.IsNullOrEmpty(mediaUri.uri),
                "GetStreamUri returned null or empty URI string");

            var uri = new Uri(mediaUri.uri);

            // The URI must be rtsp://, not https:// or http:// — Phase 1 fix must not
            // have rewritten the scheme of a stream URI returned by the camera.
            Assert.AreEqual("rtsp", uri.Scheme,
                string.Format("GetStreamUri scheme must be rtsp, got: {0}", mediaUri.uri));

            // The host must match what the camera reported, not be rewritten to match
            // the device service host.
            Assert.AreEqual(TestHost, uri.Host,
                string.Format("Stream URI host must match ODM_TEST_HOST ({0}), got: {1}", TestHost, mediaUri.uri));
        }
    }
}
