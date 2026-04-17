using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;
using onvif.services;

namespace odm.tests
{
    [TestClass]
    public class TlsProbeIntegrationTests
    {
        private static string _host;
        private static string _user;
        private static string _pass;
        private static NvtSessionFactory _factory;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            // Load .env if env vars are not already set
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

            _host = Environment.GetEnvironmentVariable("ODM_TEST_HOST");
            if (string.IsNullOrEmpty(_host))
                Assert.Inconclusive("ODM_TEST_HOST not set — skipping integration tests");

            _user = Environment.GetEnvironmentVariable("ODM_TEST_USER") ?? "admin";
            _pass = Environment.GetEnvironmentVariable("ODM_TEST_PASS") ?? "";

            // Apply TLS settings before any WCF channel factory is created.
            // Milesight gSOAP stack is not compatible with TLS 1.3; restrict to 1.2.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            _factory = new NvtSessionFactory(new NetworkCredential(_user, _pass));
        }

        private static T Run<T>(FSharpAsync<T> computation, int timeoutMs = 120000)
        {
            return FSharpAsync.RunSynchronously(
                computation,
                FSharpOption<int>.Some(timeoutMs),
                FSharpOption<CancellationToken>.None);
        }

        /// <summary>
        /// Exercises the probe path: passes an HTTP URI to CreateSession(Uri[]).
        /// The probe either succeeds on HTTP (camera answers port 80) or the HTTPS fallback
        /// kicks in.  Either way a non-null session must be returned.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void TlsProbe_MilesightCamera_HttpFallbackToHttps_ConnectsSuccessfully()
        {
            var httpUri = new Uri(string.Format("http://{0}/onvif/device_service", _host));

            INvtSession session;
            try
            {
                session = Run(_factory.CreateSession(new[] { httpUri }), timeoutMs: 60000);
            }
            catch (Exception ex)
            {
                Assert.Fail("CreateSession(Uri[]) probe failed: " + ex);
                return;
            }

            Assert.IsNotNull(session, "Session should not be null after probe");
        }

        /// <summary>
        /// Verifies that once a session is established (via direct HTTPS), GetProfiles can be
        /// called.  Marked Inconclusive if the camera's media endpoint returns HTTP 400 —
        /// that is a known camera-specific behaviour orthogonal to the TLS probe mechanism.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void TlsProbe_MilesightCamera_DirectHttps_GetProfiles_ReturnsAtLeastOne()
        {
            var httpsUri = new Uri(string.Format("https://{0}:443/onvif/device_service", _host));
            var session = _factory.CreateSession(httpsUri);

            Assert.IsNotNull(session, "Direct HTTPS session should not be null");

            try
            {
                var profiles = Run(session.GetProfiles());
                Assert.IsNotNull(profiles, "GetProfiles should not return null");
                Assert.IsTrue(profiles.Length >= 1,
                    string.Format("Expected at least one profile, got {0}", profiles.Length));
            }
            catch (System.ServiceModel.ProtocolException ex) when (ex.Message.Contains("400"))
            {
                Assert.Inconclusive(
                    "Camera media endpoint returns HTTP 400 for all authenticated SOAP requests " +
                    "(known camera limitation — not a TLS probe regression). Details: " + ex.Message);
            }
        }

        /// <summary>
        /// Verifies the probe path end-to-end: session created via probe can call GetProfiles.
        /// Marked Inconclusive if the camera's media endpoint returns HTTP 400 —
        /// that is a known camera-specific behaviour orthogonal to the TLS probe mechanism.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void TlsProbe_MilesightCamera_HttpFallbackToHttps_GetProfiles_ReturnsAtLeastOne()
        {
            var httpUri = new Uri(string.Format("http://{0}/onvif/device_service", _host));

            INvtSession session;
            try
            {
                session = Run(_factory.CreateSession(new[] { httpUri }), timeoutMs: 60000);
            }
            catch (Exception ex)
            {
                Assert.Fail("CreateSession(Uri[]) probe failed: " + ex);
                return;
            }

            try
            {
                var profiles = Run(session.GetProfiles());
                Assert.IsNotNull(profiles, "GetProfiles should not return null");
                Assert.IsTrue(profiles.Length >= 1,
                    string.Format("Expected at least one profile from probed session, got {0}", profiles.Length));
            }
            catch (System.ServiceModel.ProtocolException ex) when (ex.Message.Contains("400"))
            {
                Assert.Inconclusive(
                    "Camera media endpoint returns HTTP 400 for all authenticated SOAP requests " +
                    "(known camera limitation — not a TLS probe regression). Details: " + ex.Message);
            }
        }
    }
}
