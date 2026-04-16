using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// Offline unit tests for the UpgradeSchemeIfNeeded logic exposed via
    /// NvtSessionFactory.UpgradeScheme(deviceUri, url).
    ///
    /// The private UpgradeSchemeIfNeeded closure in NvtSession.fs delegates to
    /// this public static for testability — single source of truth.
    ///
    /// Behavior (post-#26 fix — port-exact-match required):
    ///   - http:// url + https:// deviceUri, same port  => https:// url (upgrade)
    ///   - http:// url + https:// deviceUri, port differs => unchanged (no upgrade — bug #26)
    ///   - https:// url                                  => unchanged (already HTTPS)
    ///   - http:// url + http:// deviceUri               => unchanged (session is HTTP)
    /// </summary>
    [TestClass]
    public class FixUrlHttpsTests
    {
        private static readonly Uri HttpsDeviceUri =
            new Uri("https://192.168.1.190/onvif/device_service");

        private static readonly Uri HttpDeviceUri =
            new Uri("http://192.168.1.190/onvif/device_service");

        [TestMethod]
        public void UpgradeScheme_HttpUrl_WhenSessionIsHttps_PortDiffers_NoUpgrade()
        {
            // Device is https:443; sub-service is http:80. Ports differ — no upgrade (bug #26 fix).
            var input  = new Uri("http://192.168.1.190/onvif/device_service");
            var result = NvtSessionFactory.UpgradeScheme(HttpsDeviceUri, input);

            Assert.AreEqual(input.ToString(), result.ToString(),
                "Sub-service on port 80 must not be upgraded when device is on port 443 (ports differ)");
        }

        [TestMethod]
        public void UpgradeScheme_HttpUrl_Port80_WhenSessionIsHttps_PortDiffers_NoUpgrade()
        {
            // Device is https:443; sub-service is http:80. Ports differ — no upgrade (bug #26 fix).
            var input  = new Uri("http://192.168.1.190:80/onvif/media");
            var result = NvtSessionFactory.UpgradeScheme(HttpsDeviceUri, input);

            Assert.AreEqual(input.ToString(), result.ToString(),
                "Sub-service on explicit port 80 must not be upgraded when device is on port 443 (ports differ)");
        }

        [TestMethod]
        public void UpgradeScheme_HttpsUrl_Unchanged()
        {
            var input  = new Uri("https://192.168.1.190/onvif/device_service");
            var result = NvtSessionFactory.UpgradeScheme(HttpsDeviceUri, input);

            Assert.AreEqual(input.ToString(), result.ToString(),
                "HTTPS URL must be returned unchanged (already upgraded)");
        }

        [TestMethod]
        public void UpgradeScheme_HttpUrl_WhenSessionIsHttp_Unchanged()
        {
            var input  = new Uri("http://192.168.1.190/onvif/device_service");
            var result = NvtSessionFactory.UpgradeScheme(HttpDeviceUri, input);

            Assert.AreEqual(input.ToString(), result.ToString(),
                "HTTP URL must be unchanged when session itself uses HTTP");
        }

        [TestMethod]
        public void UpgradeScheme_HttpUrl_PortDiffersFromDevicePort_NoUpgrade()
        {
            // Device is https:443; sub-service is http:8080. Ports differ — no upgrade.
            var input  = new Uri("http://192.168.1.190:8080/onvif/device_service");
            var result = NvtSessionFactory.UpgradeScheme(HttpsDeviceUri, input);

            Assert.AreEqual(input.ToString(), result.ToString(),
                "Sub-service on port 8080 must not be upgraded when device is on port 443 (ports differ)");
        }

        [TestMethod]
        public void UpgradeScheme_HttpUrl_WithNonDefaultHttpsDevicePort_SamePort_Upgrades()
        {
            // Device is https:8443; sub-service is http:8443. Ports match — upgrade.
            var deviceUri8443 = new Uri("https://192.168.1.190:8443/onvif/device_service");
            var input         = new Uri("http://192.168.1.190:8443/onvif/media");
            var result        = NvtSessionFactory.UpgradeScheme(deviceUri8443, input);

            Assert.AreEqual(Uri.UriSchemeHttps, result.Scheme,
                "Scheme should be upgraded to https when ports match");
            Assert.AreEqual(8443, result.Port,
                "Port must be preserved after upgrade");
            Assert.AreEqual("/onvif/media", result.AbsolutePath,
                "Path must be preserved");
        }
    }
}
