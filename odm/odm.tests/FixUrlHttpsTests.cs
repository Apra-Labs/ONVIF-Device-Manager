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
    /// Behavior:
    ///   - http:// url + https:// deviceUri  => https:// url, port from deviceUri
    ///     (default device port -> 443; non-default device port e.g. 8443 -> 8443)
    ///   - https:// url                       => unchanged (already HTTPS)
    ///   - http:// url + http:// deviceUri    => unchanged (session is HTTP)
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
        public void UpgradeScheme_HttpUrl_MapsToDeviceHttpsPort()
        {
            // Device is on the default HTTPS port (443), so deviceUri.IsDefaultPort = true.
            // Input URL has a non-standard port (8080), but the result port comes from
            // the device URI, not the input URL — i.e. 443.
            var input  = new Uri("http://192.168.1.190:8080/onvif/device_service");
            var result = NvtSessionFactory.UpgradeScheme(HttpsDeviceUri, input);

            Assert.AreEqual(Uri.UriSchemeHttps, result.Scheme,
                "Scheme should be upgraded to https");
            Assert.AreEqual(443, result.Port,
                "Result port must be the device's HTTPS port (443), not the input URL's port");
            Assert.AreEqual("/onvif/device_service", result.AbsolutePath,
                "Path must be preserved");
        }

        [TestMethod]
        public void UpgradeScheme_HttpUrl_WithNonDefaultHttpsDevicePort_MapsToDevicePort()
        {
            // Device is on a non-default HTTPS port (8443).
            // Input URL is http://host:80/... -> result must be https://host:8443/...
            var deviceUri8443 = new Uri("https://192.168.1.190:8443/onvif/device_service");
            var input         = new Uri("http://192.168.1.190:80/onvif/media");
            var result        = NvtSessionFactory.UpgradeScheme(deviceUri8443, input);

            Assert.AreEqual(Uri.UriSchemeHttps, result.Scheme,
                "Scheme should be upgraded to https");
            Assert.AreEqual(8443, result.Port,
                "Result port must match the device's non-default HTTPS port (8443)");
            Assert.AreEqual("/onvif/media", result.AbsolutePath,
                "Path must be preserved");
        }
    }
}
