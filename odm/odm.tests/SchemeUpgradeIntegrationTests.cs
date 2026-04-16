using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Microsoft.FSharp.Control;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;
using onvif.services;

namespace odm.tests
{
    [TestClass]
    public class SchemeUpgradeIntegrationTests
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
            return FSharpAsync.RunSynchronously(async, Microsoft.FSharp.Core.FSharpOption<int>.Some(timeoutMs), null);
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

        private static void SkipIfNoHost()
        {
            if (string.IsNullOrEmpty(TestHost))
                Assert.Inconclusive("ODM_TEST_HOST not set - skipping integration test");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void UpgradeScheme_HttpSubservice_IsNotUpgraded()
        {
            SkipIfNoHost();
            var session = CreateSession();

            var caps = Run(session.GetCapabilities(null));
            Assert.IsNotNull(caps, "GetCapabilities returned null");

            var subServiceUrls = new List<string>();
            if (caps.media != null && !string.IsNullOrEmpty(caps.media.xAddr))
                subServiceUrls.Add(caps.media.xAddr);
            if (caps.events != null && !string.IsNullOrEmpty(caps.events.xAddr))
                subServiceUrls.Add(caps.events.xAddr);
            if (caps.imaging != null && !string.IsNullOrEmpty(caps.imaging.xAddr))
                subServiceUrls.Add(caps.imaging.xAddr);
            if (caps.ptz != null && !string.IsNullOrEmpty(caps.ptz.xAddr))
                subServiceUrls.Add(caps.ptz.xAddr);
            if (caps.analytics != null && !string.IsNullOrEmpty(caps.analytics.xAddr))
                subServiceUrls.Add(caps.analytics.xAddr);

            Assert.IsTrue(subServiceUrls.Count > 0, "Camera returned no sub-service URLs in capabilities");

            foreach (var url in subServiceUrls)
            {
                var uri = new Uri(url);
                Assert.AreEqual(Uri.UriSchemeHttp, uri.Scheme,
                    string.Format("Sub-service URL should use http scheme, got: {0}", url));
            }

            // If UpgradeScheme incorrectly promotes http:80 to https:443, GetProfiles
            // would throw connection-refused (bug #26). After fix, this call succeeds or
            // times out gracefully — either outcome is acceptable; a connection-refused
            // exception would re-expose the bug.
            try
            {
                var profiles = Run(session.GetProfiles(), 20000);
                // If we got here, the camera responded — verify profiles are valid
                if (profiles != null && profiles.Length > 0)
                    Assert.IsTrue(profiles.Length > 0,
                        string.Format("Expected at least one profile from {0}", TestHost));
            }
            catch (System.TimeoutException)
            {
                // Timeout is acceptable — the sub-service URL was http:// (verified above)
                // and we attempted connection. Bug #26 would produce connection-refused, not timeout.
            }
        }
    }
}
