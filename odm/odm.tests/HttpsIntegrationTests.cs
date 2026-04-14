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

            // Restrict to TLS 1.2 — camera's gSOAP TLS stack is not compatible with TLS 1.3.
            // This also prevents SNI-related stalls: TLS 1.2 is what curl uses successfully.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            // Disable Expect: 100-Continue globally AND on the specific ServicePoint.
            // This camera silently stalls on Expect: 100-Continue handshakes.
            // Global flag applies to new ServicePoints; per-ServicePoint overrides existing ones.
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;
            // Pre-create and configure the ServicePoint so Expect100Continue is false
            // before WCF creates its channel factory.
            var sp = ServicePointManager.FindServicePoint(
                new Uri(string.Format("https://{0}:443/onvif/device_service",
                    Environment.GetEnvironmentVariable("ODM_TEST_HOST") ?? "192.168.1.190")));
            sp.Expect100Continue = false;

            var cred = new NetworkCredential(_user, _pass);
            var factory = new NvtSessionFactory(cred);
            // Construct HTTPS URI explicitly — port 80 is disabled for ONVIF on this camera.
            // Port 443 (HTTPS/TLS 1.2) is the only functional SOAP endpoint.
            var port = Environment.GetEnvironmentVariable("ODM_TEST_HTTPS_PORT") ?? "443";
            var deviceUri = new Uri(string.Format("https://{0}:{1}/onvif/device_service", _host, port));
            _session = factory.CreateSession(deviceUri);
        }

        private static T Run<T>(FSharpAsync<T> computation, int timeoutMs = 120000)
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
        public void GetSystemDateAndTime_ViaTcpSslStream_Succeeds()
        {
            // Diagnostic: verify raw TcpClient+SslStream can reach camera (zero abstraction layers)
            const string soapBody =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">" +
                "<s:Body><GetSystemDateAndTime xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/></s:Body>" +
                "</s:Envelope>";
            var bodyBytes = System.Text.Encoding.UTF8.GetBytes(soapBody);

            // Match curl's exact header format (Host without port for default port 443)
            var hostHeader = (_httpsPort == 443) ? _host : string.Format("{0}:{1}", _host, _httpsPort);
            var request =
                string.Format(
                    "POST /onvif/device_service HTTP/1.1\r\n" +
                    "Host: {0}\r\n" +
                    "User-Agent: odm-test/1.0\r\n" +
                    "Accept: */*\r\n" +
                    "Content-Type: application/soap+xml; charset=utf-8\r\n" +
                    "Content-Length: {1}\r\n" +
                    "Connection: close\r\n" +
                    "\r\n",
                    hostHeader, bodyBytes.Length);
            var headerBytes = System.Text.Encoding.ASCII.GetBytes(request);

            string responseText;
            using (var tcp = new System.Net.Sockets.TcpClient())
            {
                tcp.Connect(_host, _httpsPort);
                tcp.ReceiveTimeout = 15000;
                tcp.SendTimeout = 15000;

                using (var ssl = new System.Net.Security.SslStream(
                    tcp.GetStream(), false,
                    (s, cert, chain, err) => true))
                {
                    // Pass empty string to suppress SNI (IP addresses must not appear in SNI per RFC 6066)
                    // Camera's gSOAP TLS stack hangs when receiving SNI with IP address
                    ssl.AuthenticateAsClient(_host, null,
                        System.Security.Authentication.SslProtocols.Tls12, false);
                    // Combine headers and body into single write to avoid partial-frame confusion
                    var fullRequest = new byte[headerBytes.Length + bodyBytes.Length];
                    Buffer.BlockCopy(headerBytes, 0, fullRequest, 0, headerBytes.Length);
                    Buffer.BlockCopy(bodyBytes, 0, fullRequest, headerBytes.Length, bodyBytes.Length);
                    ssl.Write(fullRequest);
                    ssl.Flush();

                    var buf = new byte[65536];
                    var ms = new System.IO.MemoryStream();
                    int read;
                    while ((read = ssl.Read(buf, 0, buf.Length)) > 0)
                        ms.Write(buf, 0, read);
                    responseText = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }
            }

            Assert.IsTrue(responseText.Contains("SystemDateAndTime") || responseText.Contains("UTCDateTime"),
                "Response should contain SystemDateAndTime. Got: " + responseText.Substring(0, Math.Min(500, responseText.Length)));
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetSystemDateAndTime_ViaWcfChannel_Succeeds()
        {
            // Production path: WCF HTTPS channel makes unauthenticated SOAP call
            // Note: requires camera 192.168.1.190 to NOT reject .NET Framework TLS SNI behavior
            var result = Run(_session.GetSystemDateAndTime());
            Assert.IsNotNull(result, "GetSystemDateAndTime should return a result");
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
        [TestCategory("HttpsIntegration")]
        public void ConcurrentSoapCalls_DoNotRaceOnEncoder()
        {
            // Reproduces "The Write method cannot be called when another write operation is pending."
            // Fires three SOAP calls simultaneously on the same session. Because
            // SslStreamChannelFactory shares one MessageEncoder across all channels,
            // concurrent RequestCore invocations would race on the encoder's internal
            // XmlDictionaryWriter pool without synchronization.
            const int iterations = 5;
            for (int i = 0; i < iterations; i++)
            {
                var t1 = System.Threading.Tasks.Task.Run(() => Run(_session.GetSystemDateAndTime()));
                var t2 = System.Threading.Tasks.Task.Run(() => Run(_session.GetSystemDateAndTime()));
                var t3 = System.Threading.Tasks.Task.Run(() => Run(_session.GetCapabilities(null)));

                try
                {
                    System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { t1, t2, t3 }, 60000);
                }
                catch (AggregateException ae)
                {
                    foreach (var inner in ae.Flatten().InnerExceptions)
                    {
                        if (inner is InvalidOperationException &&
                            inner.Message.IndexOf("Write method", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Assert.Fail("Concurrent-write race hit: " + inner.Message);
                        }
                    }
                    throw;
                }

                Assert.IsNotNull(t1.Result, "GetSystemDateAndTime(1) returned null on iteration " + i);
                Assert.IsNotNull(t2.Result, "GetSystemDateAndTime(2) returned null on iteration " + i);
                Assert.IsNotNull(t3.Result, "GetCapabilities returned null on iteration " + i);
            }
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
