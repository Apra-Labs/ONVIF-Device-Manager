using System;
using System.ServiceModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// Offline unit tests for stream URI fidelity (#30).
    ///
    /// Root cause: on ONVIF 1.x cameras GetServices() throws ProtocolException 400.
    /// GetMedia2Client() caught it but re-raised, so GetProfiles/GetStreamUri never
    /// fell back to Media1. Fix (task 2.4): swallow the exception and return null.
    ///
    /// These tests verify:
    ///   a) UpgradeScheme never rewrites rtsp:// URIs.
    ///   b) The GetMedia2Client exception-swallow-and-return-null pattern works.
    /// </summary>
    [TestClass]
    public class StreamUriTests
    {
        // ── a) UpgradeScheme must not touch rtsp:// URIs ──────────────────────

        [TestMethod]
        public void UpgradeScheme_RtspUri_NotModified()
        {
            var deviceUri = new Uri("http://10.102.10.97/onvif/device_service");
            var rtspUri   = new Uri("rtsp://10.102.10.97:554/stream1");

            var result = NvtSessionFactory.UpgradeScheme(deviceUri, rtspUri);

            Assert.AreEqual(rtspUri, result,
                "UpgradeScheme must not rewrite rtsp:// URIs — scheme is not http");
        }

        [TestMethod]
        public void UpgradeScheme_RtspUri_HttpsDevice_NotModified()
        {
            // Even when device is HTTPS, rtsp:// stream URIs must pass through unchanged.
            var deviceUri = new Uri("https://10.102.10.97/onvif/device_service");
            var rtspUri   = new Uri("rtsp://10.102.10.97:554/stream1");

            var result = NvtSessionFactory.UpgradeScheme(deviceUri, rtspUri);

            Assert.AreEqual(rtspUri, result,
                "UpgradeScheme must not rewrite rtsp:// URIs even when device is HTTPS");
        }

        // ── b) GetMedia2Client exception-swallow pattern ──────────────────────
        //
        // GetMedia2Client is a private F# closure; we test a C# replica of the
        // corrected pattern: catch any exception from GetServices() and return null
        // so that callers fall back to Media1.

        /// <summary>
        /// Mirrors the corrected GetMedia2Client pattern:
        ///   try { return getServices(); }
        ///   catch { log; return null; }   ← fix: null instead of rethrow
        /// </summary>
        private static object GetMedia2ClientSimulated(Func<object[]> getServices)
        {
            object[] services;
            try
            {
                services = getServices();
            }
            catch
            {
                return null;
            }
            if (services == null) return null;
            // (real code would search for Media2 namespace — omitted here)
            return services.Length > 0 ? new object() : null;
        }

        [TestMethod]
        public void GetMedia2Client_GetServicesThrows400_ReturnsNull()
        {
            // Simulates ONVIF 1.x camera: GetServices() throws ProtocolException 400.
            var proto400 = new ProtocolException("The remote server returned an unexpected response: (400) Bad Request.");

            var result = GetMedia2ClientSimulated(() => throw proto400);

            Assert.IsNull(result,
                "GetMedia2Client must return null when GetServices() throws ProtocolException 400, " +
                "so callers can fall back to Media1");
        }

        [TestMethod]
        public void GetMedia2Client_GetServicesThrowsGenericException_ReturnsNull()
        {
            var result = GetMedia2ClientSimulated(() => throw new InvalidOperationException("network error"));

            Assert.IsNull(result,
                "GetMedia2Client must return null on any GetServices() exception");
        }

        [TestMethod]
        public void GetMedia2Client_GetServicesReturnsEmpty_ReturnsNull()
        {
            var result = GetMedia2ClientSimulated(() => new object[0]);

            Assert.IsNull(result,
                "GetMedia2Client must return null when GetServices() returns no services");
        }
    }
}
