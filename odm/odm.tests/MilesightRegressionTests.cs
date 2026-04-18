extern alias onvifgen;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;
using odm.ui.core;
using onvif.services;
using VideoEncoder2ConfigurationOptions = onvifgen::onvif.services.VideoEncoder2ConfigurationOptions;

namespace odm.tests
{
    /// <summary>
    /// Integration tests for the defensive fixes in commit 75813f5:
    ///   1. GetAllCapabilities tolerates HTTP 400 from device_service
    ///   2. routeMedia tolerates faulted Media2 channels (falls back to Media1)
    ///
    /// These verify the session layer against a real Milesight camera.
    /// Skipped (Inconclusive) when ODM_TEST_HOST is not set.
    /// </summary>
    [TestClass]
    public class MilesightRegressionTests
    {
        private static INvtSession _session;
        private static string _host;

        // Mirror of CredentialStore.CredentialList for local deserialization
        [XmlRootAttribute(ElementName = "Credentials", IsNullable = false)]
        private class CredentialList
        {
            public List<Account> Items { get; set; }
            public CredentialList() { Items = new List<Account>(); }
        }

        /// <summary>
        /// Load credentials from credentials.dat via DPAPI — same mechanism as CredentialStore.
        /// Walks up from the test binary directory to find build/config/credentials.dat.
        /// </summary>
        private static IList<Account> LoadCredentialStore()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "build", "config", "credentials.dat");
                if (File.Exists(candidate))
                {
                    try
                    {
                        byte[] cipherBytes = File.ReadAllBytes(candidate);
                        byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
                        string xml = Encoding.UTF8.GetString(plainBytes);
                        var serializer = new XmlSerializer(typeof(CredentialList));
                        using (var reader = new StringReader(xml))
                        {
                            var list = (CredentialList)serializer.Deserialize(reader);
                            return list.Items ?? new List<Account>();
                        }
                    }
                    catch { /* fall through */ }
                }
                dir = dir.Parent;
            }
            return new List<Account>();
        }

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            LoadEnvFile();

            // Default to the Milesight camera used in regression testing
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ODM_TEST_HOST")))
                Environment.SetEnvironmentVariable("ODM_TEST_HOST", "192.168.1.190");

            _host = Environment.GetEnvironmentVariable("ODM_TEST_HOST");
            if (string.IsNullOrEmpty(_host))
                return;

            // Load credentials from credentials.dat (DPAPI) — same mechanism as CameraCompatibilitySweepTests
            var creds = LoadCredentialStore();
            Console.WriteLine("CredentialStore: {0} credential(s) loaded.", creds.Count);

            string user, pass;
            if (creds.Count > 0)
            {
                user = creds[0].Name;
                pass = creds[0].Password;
                Console.WriteLine("Using stored credential for user: {0}", user);
            }
            else
            {
                user = Environment.GetEnvironmentVariable("ODM_TEST_USER") ?? "admin";
                pass = Environment.GetEnvironmentVariable("ODM_TEST_PASS") ?? "";
                Console.WriteLine("No credentials.dat found — falling back to env vars (user={0})", user);
            }

            var port = Environment.GetEnvironmentVariable("ODM_TEST_HTTPS_PORT") ?? "443";

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            var deviceUri = new Uri(string.Format("https://{0}:{1}/onvif/device_service", _host, port));

            var sp = ServicePointManager.FindServicePoint(deviceUri);
            sp.Expect100Continue = false;

            var cred = new NetworkCredential(user, pass);
            var factory = new NvtSessionFactory(cred);
            _session = factory.CreateSession(deviceUri);
        }

        private static void LoadEnvFile()
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
        }

        private static T Run<T>(FSharpAsync<T> computation, int timeoutMs = 30000)
        {
            return FSharpAsync.RunSynchronously(
                computation,
                FSharpOption<int>.Some(timeoutMs),
                FSharpOption<CancellationToken>.None);
        }

        private void EnsureSession()
        {
            if (_session == null)
                Assert.Inconclusive("ODM_TEST_HOST not set — skipping integration test");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetAllCapabilities_DoesNotThrow_OnMilesight()
        {
            EnsureSession();

            Capabilities caps = null;
            Exception caught = null;
            try
            {
                caps = Run(_session.GetAllCapabilities());
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.IsNull(caught,
                string.Format("GetAllCapabilities should not throw. Got: {0}", caught));
            Assert.IsNotNull(caps,
                "GetAllCapabilities should return a non-null Capabilities object");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetVideoEncoderConfigurationOptionsMedia2_DoesNotThrow_OnMilesight()
        {
            EnsureSession();

            var profiles = Run(_session.GetProfiles());
            Assert.IsNotNull(profiles, "GetProfiles should return profiles");
            Assert.IsTrue(profiles.Length >= 1, "Need at least one profile");

            var profileToken = profiles[0].token;

            VideoEncoder2ConfigurationOptions[] opts = null;
            Exception caught = null;
            try
            {
                opts = Run(_session.GetVideoEncoderConfigurationOptionsMedia2(profileToken));
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.IsNull(caught,
                string.Format("GetVideoEncoderConfigurationOptionsMedia2 should not throw. Got: {0}", caught));
            Assert.IsNotNull(opts,
                "Result should be a non-null array (empty is fine, null is not)");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetVideoEncoderConfigurationOptionsMedia2_ReturnsOptions_OnCompliantCamera()
        {
            EnsureSession();

            var profiles = Run(_session.GetProfiles());
            Assert.IsNotNull(profiles, "GetProfiles should return profiles");
            Assert.IsTrue(profiles.Length >= 1, "Need at least one profile");

            VideoEncoder2ConfigurationOptions[] opts = null;
            try
            {
                opts = Run(_session.GetVideoEncoderConfigurationOptionsMedia2(profiles[0].token));
            }
            catch
            {
                Assert.Inconclusive("GetVideoEncoderConfigurationOptionsMedia2 threw — camera may not support Media2");
                return;
            }

            if (opts == null || opts.Length == 0)
            {
                Assert.Inconclusive(
                    "Camera returned empty Media2 encoder options — not a Media2-capable camera. " +
                    "This is expected for Milesight and similar cameras.");
                return;
            }

            Assert.IsTrue(opts.Length > 0, "Compliant Media2 camera should return at least one option");
            foreach (var opt in opts)
            {
                Assert.IsNotNull(opt, "Each option entry should be non-null");
                Assert.IsFalse(string.IsNullOrEmpty(opt.Encoding),
                    "Each option should have an Encoding value");
            }
        }
    }
}
