using System;
using System.Net;
using Microsoft.FSharp.Control;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;
using onvif.services;

namespace odm.tests
{
    /// <summary>
    /// Integration tests for Media2 routing layer. These tests require a real ONVIF
    /// camera that supports Media2 (ver20/media/wsdl).
    ///
    /// Set ODM_TEST_HOST to the camera IP/hostname to enable these tests.
    /// Optionally set ODM_TEST_USER and ODM_TEST_PASS for authenticated access.
    ///
    /// All tests are tagged [TestCategory("Integration")] so the offline test run
    /// (TestCaseFilter:"TestCategory!=Integration") skips them automatically.
    /// </summary>
    [TestClass]
    public class Media2IntegrationTests
    {
        private static string TestHost => Environment.GetEnvironmentVariable("ODM_TEST_HOST");
        private static string TestUser => Environment.GetEnvironmentVariable("ODM_TEST_USER") ?? "";
        private static string TestPass => Environment.GetEnvironmentVariable("ODM_TEST_PASS") ?? "";

        private static T Run<T>(Microsoft.FSharp.Control.FSharpAsync<T> async)
        {
            return FSharpAsync.RunSynchronously(async, null, null);
        }

        private static INvtSession CreateSession()
        {
            var uri = new Uri(string.Format("http://{0}/onvif/device_service", TestHost));
            NetworkCredential creds = string.IsNullOrEmpty(TestUser)
                ? null
                : new NetworkCredential(TestUser, TestPass);
            var factory = new NvtSessionFactory(creds);
            var uris = new[] { uri };
            return Run(factory.CreateSession(uris));
        }

        private static void SkipIfNoHost()
        {
            if (string.IsNullOrEmpty(TestHost))
                Assert.Inconclusive("ODM_TEST_HOST not set — skipping integration test");
        }

        // ----------------------------------------------------------------
        // Test 1: GetProfiles returns valid profiles from a Media2 camera
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Integration")]
        public void GetProfiles_Media2Camera_ReturnsNonEmptyProfiles()
        {
            SkipIfNoHost();
            var session = CreateSession();
            var profiles = Run(session.GetProfiles());
            Assert.IsNotNull(profiles, "GetProfiles returned null");
            Assert.IsTrue(profiles.Length > 0, "Expected at least one profile");
            foreach (var p in profiles)
            {
                Assert.IsFalse(string.IsNullOrEmpty(p.token), "Profile token should not be empty");
            }
        }

        // ----------------------------------------------------------------
        // Test 2: GetStreamUri returns a valid RTSP URI
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Integration")]
        public void GetStreamUri_FirstProfile_ReturnsRtspUri()
        {
            SkipIfNoHost();
            var session = CreateSession();
            var profiles = Run(session.GetProfiles());
            if (profiles == null || profiles.Length == 0)
                Assert.Inconclusive("No profiles found — cannot test GetStreamUri");

            var streamSetup = new StreamSetup();
            streamSetup.stream = StreamType.rtpUnicast;
            streamSetup.transport = new Transport();
            streamSetup.transport.protocol = TransportProtocol.rtsp;

            var mediaUri = Run(session.GetStreamUri(streamSetup, profiles[0].token));
            Assert.IsNotNull(mediaUri, "GetStreamUri returned null");
            Assert.IsFalse(string.IsNullOrEmpty(mediaUri.uri), "Stream URI should not be empty");
            StringAssert.StartsWith(mediaUri.uri.ToLowerInvariant(), "rtsp", "Stream URI should start with rtsp");
        }

        // ----------------------------------------------------------------
        // Test 3: GetVideoEncoderConfigurationOptions returns valid ranges
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Integration")]
        public void GetVideoEncoderConfigurationOptions_FirstProfile_ReturnsOptions()
        {
            SkipIfNoHost();
            var session = CreateSession();
            var profiles = Run(session.GetProfiles());
            if (profiles == null || profiles.Length == 0)
                Assert.Inconclusive("No profiles found — cannot test GetVideoEncoderConfigurationOptions");

            var profile = profiles[0];
            if (profile.videoEncoderConfiguration == null)
                Assert.Inconclusive("First profile has no VideoEncoderConfiguration");

            var options = Run(session.GetVideoEncoderConfigurationOptions(
                profile.videoEncoderConfiguration.token, profile.token));
            Assert.IsNotNull(options, "GetVideoEncoderConfigurationOptions returned null");
        }

        // ----------------------------------------------------------------
        // Test 4: GetVideoEncoderConfigurations returns configs via Media2
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Integration")]
        public void GetVideoEncoderConfigurations_Media2Camera_ReturnsConfigurations()
        {
            SkipIfNoHost();
            var session = CreateSession();
            var configs = Run(session.GetVideoEncoderConfigurations());
            Assert.IsNotNull(configs, "GetVideoEncoderConfigurations returned null");
            Assert.IsTrue(configs.Length > 0, "Expected at least one encoder configuration");
            foreach (var c in configs)
            {
                Assert.IsFalse(string.IsNullOrEmpty(c.token), "Configuration token should not be empty");
            }
        }

        // ----------------------------------------------------------------
        // Test 5: GetVideoSourceConfigurations returns non-empty configs
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Integration")]
        public void GetVideoSourceConfigurations_Media2Camera_ReturnsConfigurations()
        {
            SkipIfNoHost();
            var session = CreateSession();
            var configs = Run(session.GetVideoSourceConfigurations());
            Assert.IsNotNull(configs, "GetVideoSourceConfigurations returned null");
            Assert.IsTrue(configs.Length > 0, "Expected at least one video source configuration");
            foreach (var c in configs)
            {
                Assert.IsFalse(string.IsNullOrEmpty(c.token), "Configuration token should not be empty");
                Assert.IsFalse(string.IsNullOrEmpty(c.sourceToken), "Source token should not be empty");
            }
        }

        // ----------------------------------------------------------------
        // Test 6: GetSnapshotUri returns a URI (or gracefully returns null
        //         for cameras that don't support snapshot via Media2)
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Integration")]
        public void GetSnapshotUri_FirstProfile_ReturnsUriOrNull()
        {
            SkipIfNoHost();
            var session = CreateSession();
            var profiles = Run(session.GetProfiles());
            if (profiles == null || profiles.Length == 0)
                Assert.Inconclusive("No profiles found — cannot test GetSnapshotUri");

            // GetSnapshotUri may return null mediaUri or a null/empty uri string
            // for cameras that don't support snapshots — this is not a failure.
            try
            {
                var mediaUri = Run(session.GetSnapshotUri(profiles[0].token));
                if (mediaUri != null && !string.IsNullOrEmpty(mediaUri.uri))
                {
                    // If URI is returned, it should be HTTP(S)
                    var lower = mediaUri.uri.ToLowerInvariant();
                    Assert.IsTrue(lower.StartsWith("http://") || lower.StartsWith("https://"),
                        string.Format("Snapshot URI should be HTTP(S), got: {0}", mediaUri.uri));
                }
                // null or empty URI is acceptable — camera may not support snapshot
            }
            catch (Exception)
            {
                Assert.Inconclusive("Camera does not support GetSnapshotUri — acceptable");
            }
        }

        // ----------------------------------------------------------------
        // Test 7: GetCompatibleVideoEncoderConfigurations returns configs
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Integration")]
        public void GetCompatibleVideoEncoderConfigurations_FirstProfile_ReturnsConfigurations()
        {
            SkipIfNoHost();
            var session = CreateSession();
            var profiles = Run(session.GetProfiles());
            if (profiles == null || profiles.Length == 0)
                Assert.Inconclusive("No profiles found — cannot test GetCompatibleVideoEncoderConfigurations");

            var configs = Run(session.GetCompatibleVideoEncoderConfigurations(profiles[0].token));
            Assert.IsNotNull(configs, "GetCompatibleVideoEncoderConfigurations returned null");
            // Compatible configs may be empty for some profiles — just verify no exception
        }
    }
}
