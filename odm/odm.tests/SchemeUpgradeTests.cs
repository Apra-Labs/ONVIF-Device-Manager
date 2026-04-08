using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    [TestClass]
    public class SchemeUpgradeTests
    {
        [TestMethod]
        public void GenerateHttpsVariants_HttpPort80_ReturnsBothHttpsVariants()
        {
            var input = new[] { new Uri("http://192.168.1.190:80/onvif/device_service") };

            var result = NvtSessionFactory.GenerateHttpsVariants(input);

            var uriStrings = result.Select(u => u.ToString()).ToArray();
            Assert.IsTrue(uriStrings.Contains("https://192.168.1.190/onvif/device_service")
                          || uriStrings.Any(s => s.Contains(":443/")),
                "Result should contain an https://...443/... variant");
            Assert.IsTrue(uriStrings.Any(s => s.Contains(":8443/")),
                "Result should contain an https://...8443/... variant");
            Assert.IsTrue(result.All(u => u.Scheme == Uri.UriSchemeHttps),
                "All output URIs should use https scheme");
        }

        [TestMethod]
        public void GenerateHttpsVariants_AlreadyHttps_ReturnsEmpty()
        {
            var input = new[] { new Uri("https://192.168.1.190/onvif/device_service") };

            var result = NvtSessionFactory.GenerateHttpsVariants(input);

            Assert.AreEqual(0, result.Length,
                "HTTPS input should produce no upgrade variants (already upgraded)");
        }

        [TestMethod]
        public void GenerateHttpsVariants_NonStandardPort_PreservesPath()
        {
            var input = new[] { new Uri("http://192.168.1.190:8080/onvif/device_service") };

            var result = NvtSessionFactory.GenerateHttpsVariants(input);

            Assert.IsTrue(result.Length >= 1, "Should produce at least one variant");
            Assert.IsTrue(result.All(u => u.Scheme == Uri.UriSchemeHttps),
                "All output URIs should use https scheme");
            Assert.IsTrue(result.All(u => u.AbsolutePath == "/onvif/device_service"),
                "Path should be preserved across scheme upgrade");
            Assert.IsTrue(result.All(u => u.Host == "192.168.1.190"),
                "Host should be preserved");
        }

        [TestMethod]
        public void GenerateHttpsVariants_MultipleUris_DeduplicatesResults()
        {
            var uri = new Uri("http://192.168.1.190:80/onvif/device_service");
            // Pass the same URI twice — expect deduplicated output
            var input = new[] { uri, uri };

            var result = NvtSessionFactory.GenerateHttpsVariants(input);

            var distinct = result.Distinct().ToArray();
            Assert.AreEqual(distinct.Length, result.Length,
                "Output URIs should be deduplicated — no duplicates expected");
        }
    }
}
