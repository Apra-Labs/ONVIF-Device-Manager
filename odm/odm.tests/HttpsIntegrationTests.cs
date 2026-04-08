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
    public class HttpsIntegrationTests
    {
        private static string _host;
        private static string _user;
        private static string _pass;
        private static int _httpsPort;
        private static INvtSession _session;

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
            var portStr = Environment.GetEnvironmentVariable("ODM_TEST_HTTPS_PORT");
            _httpsPort = string.IsNullOrEmpty(portStr) ? 443 : int.Parse(portStr);

            // Mirror App.xaml.cs:72-75 — some devices don't understand Expect: 100-Continue;
            // accept self-signed certs in headless test context
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            var cred = new NetworkCredential(_user, _pass);
            var factory = new NvtSessionFactory(cred);
            // Use http:// input — the scheme-upgrade fallback should auto-detect that
            // SOAP on port 80 is non-functional and upgrade to https:// transparently
            var deviceUris = new[] { new Uri(string.Format("http://{0}/onvif/device_service", _host)) };
            // CreateSession(Uri[]) returns FSharpAsync — run synchronously with generous timeout
            // for SOAP probe + scheme-upgrade fallback (probe timeout is 5s per endpoint)
            _session = FSharpAsync.RunSynchronously(
                factory.CreateSession(deviceUris),
                FSharpOption<int>.Some(60000),
                FSharpOption<CancellationToken>.None);
        }

        private static T Run<T>(FSharpAsync<T> computation, int timeoutMs = 30000)
        {
            return FSharpAsync.RunSynchronously(
                computation,
                FSharpOption<int>.Some(timeoutMs),
                FSharpOption<CancellationToken>.None);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void Connect_ToHttpsCamera_Succeeds()
        {
            Assert.IsNotNull(_session, "Session should not be null");
            Assert.IsNotNull(_session.deviceUri, "Session deviceUri should not be null");
            Assert.AreEqual(Uri.UriSchemeHttps, _session.deviceUri.Scheme, "Session URI scheme should be https");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetCapabilities_ReturnsValidResult()
        {
            var result = Run(_session.GetCapabilities(null));

            Assert.IsNotNull(result, "GetCapabilities result should not be null");
            bool hasAtLeastOne =
                result.device != null ||
                result.media != null ||
                result.ptz != null ||
                result.imaging != null ||
                result.events != null ||
                result.analytics != null;
            Assert.IsTrue(hasAtLeastOne, "At least one capability set should be populated");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetProfiles_ReturnsAtLeastOneProfile()
        {
            var profiles = Run(_session.GetProfiles());

            Assert.IsNotNull(profiles, "Profiles result should not be null");
            Assert.IsTrue(profiles.Length >= 1, "At least one profile expected");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetStreamUri_ReturnsValidUri()
        {
            var profiles = Run(_session.GetProfiles());
            Assert.IsTrue(profiles.Length >= 1, "Need at least one profile to test GetStreamUri");

            var token = profiles[0].token;
            var setup = new StreamSetup
            {
                stream = StreamType.rtpUnicast,
                transport = new Transport { protocol = TransportProtocol.udp }
            };

            var mediaUri = Run(_session.GetStreamUri(setup, token));

            Assert.IsNotNull(mediaUri, "MediaUri result should not be null");
            Assert.IsFalse(string.IsNullOrEmpty(mediaUri.uri), "Stream URI string should not be empty");

            var scheme = new Uri(mediaUri.uri).Scheme;
            var validSchemes = new[] { "rtsp", "rtsps", "http", "https" };
            Assert.IsTrue(validSchemes.Contains(scheme),
                string.Format("Stream URI scheme '{0}' should be one of: {1}", scheme, string.Join(", ", validSchemes)));
        }
    }
}
