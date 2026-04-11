using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// Offline unit tests for the UpgradeSchemeIfNeeded logic exposed via
    /// NvtSessionFactory.UpgradeScheme(deviceUri, url).
    ///
    /// The private UpgradeSchemeIfNeeded closure in NvtSession.fs is mirrored
    /// by the public static UpgradeScheme for testability (same logic).
    ///
    /// Behavior:
    ///   - http:// url + https:// deviceUri  => https:// url (port 80 -> 443)
    ///   - https:// url                       => unchanged (already HTTPS)
    ///   - http:// url + http:// deviceUri    => unchanged (session is HTTP)
    ///   - non-standard port preserved as-is (e.g. 8080 stays 8080)
    /// </summary>
    [TestClass]
    public class FixUrlHttpsTests
    {
        private static readonly Uri HttpsDeviceUri =
            new Uri("https://192.168.1.190/onvif/device_service");

        private static readonly Uri HttpDeviceUri =
            new Uri("http://192.168.1.190/onvif/device_service");

        [TestMethod]
        public void UpgradeScheme_HttpUrl_WhenSessionIsHttps_ReturnsHttpsUrl()
        {
            var input  = new Uri("http://192.168.1.190/onvif/device_service");
            var result = NvtSessionFactory.UpgradeScheme(HttpsDeviceUri, input);

            Assert.AreEqual(Uri.UriSchemeHttps, result.Scheme,
                "Scheme should be upgraded to https when session uses HTTPS");
            Assert.AreEqual("192.168.1.190", result.Host,
                "Host must be preserved");
            Assert.AreEqual("/onvif/device_service", result.AbsolutePath,
                "Path must be preserved");
        }

        [TestMethod]
        public void UpgradeScheme_HttpUrl_Port80_WhenSessionIsHttps_ReturnsHttpsPort443()
        {
            var input  = new Uri("http://192.168.1.190:80/onvif/media");
            var result = NvtSessionFactory.UpgradeScheme(HttpsDeviceUri, input);

            Assert.AreEqual(Uri.UriSchemeHttps, result.Scheme,
                "Scheme should be upgraded to https");
            Assert.AreEqual(443, result.Port,
                "Port 80 should be mapped to 443 when upgrading to HTTPS");
            Assert.AreEqual("/onvif/media", result.AbsolutePath,
                "Path must be preserved");
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
        public void UpgradeScheme_NonStandardHttpPort_MapsToSameNonStandardHttpsPort()
        {
            // Port 8080 is non-standard — UpgradeScheme only maps 80->443.
            // Any other port is kept as-is.
            var input  = new Uri("http://192.168.1.190:8080/onvif/device_service");
            var result = NvtSessionFactory.UpgradeScheme(HttpsDeviceUri, input);

            Assert.AreEqual(Uri.UriSchemeHttps, result.Scheme,
                "Scheme should still be upgraded to https");
            Assert.AreEqual(8080, result.Port,
                "Non-standard port 8080 must be preserved unchanged (only 80->443 is mapped)");
            Assert.AreEqual("/onvif/device_service", result.AbsolutePath,
                "Path must be preserved");
        }
    }
}
