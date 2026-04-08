using System;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
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
        [TestMethod]
        public void SoapProbe_OnSilentPort_TriggersHttpsFallback()
        {
            // Simulate the real-world scenario: camera has port 80 TCP-open but
            // silently drops SOAP payloads. CreateSession(Uri[]) with an http:// URI
            // pointing to a non-ONVIF endpoint should fail the SOAP probe and generate
            // HTTPS fallback variants.
            //
            // We use localhost with a random open TCP port that accepts connections
            // but doesn't speak ONVIF — this mimics the "silent port 80" camera behavior.

            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            try
            {
                var httpUri = new Uri(string.Format("http://127.0.0.1:{0}/onvif/device_service", port));

                // CreateSession should try SOAP probe on port, fail, then try HTTPS variants.
                // HTTPS variants will also fail (no HTTPS listener), so the whole call fails —
                // but the key assertion is that it DOES attempt HTTPS variants (not just TCP).
                var factory = new NvtSessionFactory(new NetworkCredential("admin", ""));
                try
                {
                    FSharpAsync.RunSynchronously(
                        factory.CreateSession(new[] { httpUri }),
                        FSharpOption<int>.Some(15000),
                        FSharpOption<CancellationToken>.None);

                    Assert.Fail("Expected failure — no real ONVIF device is running");
                }
                catch (AggregateException ex)
                {
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    // The error should mention HTTPS fallback, confirming SOAP probe failed
                    // on the HTTP endpoint and the code proceeded to try HTTPS variants
                    Assert.IsTrue(
                        msg.Contains("HTTPS fallback"),
                        string.Format("Expected HTTPS fallback attempt, got: {0}", msg));
                }
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
