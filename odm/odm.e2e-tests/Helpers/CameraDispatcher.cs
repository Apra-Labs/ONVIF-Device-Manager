using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using odm.e2e_tests.Config;

namespace odm.e2e_tests.Helpers
{
    /// <summary>
    /// Performs ONVIF WS-Discovery (UDP multicast) to locate cameras on the local network,
    /// then calls GetDeviceInformation for each to classify against cameraProfiles in config.
    /// </summary>
    public static class CameraDispatcher
    {
        private const string WsDiscoveryAddress = "239.255.255.250";
        private const int WsDiscoveryPort = 3702;

        public static List<DiscoveredCamera> DiscoverAndClassify(SmokeConfig config, TimeSpan timeout)
        {
            var endpoints = ProbeOnvif(timeout);
            var cameras = new List<DiscoveredCamera>();

            foreach (var ep in endpoints)
            {
                try
                {
                    var info = GetDeviceInformation(ep, config);
                    var profile = MatchProfile(config.CameraProfiles, info.Manufacturer, info.Model);

                    cameras.Add(new DiscoveredCamera
                    {
                        ServiceEndpoint = ep,
                        IpAddress = ExtractIp(ep),
                        Manufacturer = info.Manufacturer,
                        Model = info.Model,
                        FirmwareVersion = info.FirmwareVersion,
                        Profile = profile
                    });
                }
                catch
                {
                    // Skip cameras that fail GetDeviceInformation — they may be non-ONVIF devices
                }
            }

            return cameras;
        }

        private static List<string> ProbeOnvif(TimeSpan timeout)
        {
            var messageId = Guid.NewGuid().ToString();
            var probe = BuildProbeMessage(messageId);
            var probeBytes = Encoding.UTF8.GetBytes(probe);

            var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var udp = new UdpClient())
            {
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                udp.Client.ReceiveTimeout = 500;

                var multicast = new IPEndPoint(IPAddress.Parse(WsDiscoveryAddress), WsDiscoveryPort);
                udp.Send(probeBytes, probeBytes.Length, multicast);

                var deadline = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var remote = new IPEndPoint(IPAddress.Any, 0);
                        var data = udp.Receive(ref remote);
                        var xml = Encoding.UTF8.GetString(data);
                        ExtractXAddrs(xml, endpoints);
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        // Normal — no response in this window, keep waiting
                    }
                }
            }

            return endpoints.ToList();
        }

        private static string BuildProbeMessage(string messageId)
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope""
            xmlns:a=""http://schemas.xmlsoap.org/ws/2004/08/addressing""
            xmlns:d=""http://schemas.xmlsoap.org/ws/2005/04/discovery""
            xmlns:dn=""http://www.onvif.org/ver10/network/wsdl"">
  <s:Header>
    <a:Action s:mustUnderstand=""1"">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>
    <a:MessageID>uuid:{messageId}</a:MessageID>
    <a:ReplyTo><a:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</a:Address></a:ReplyTo>
    <a:To s:mustUnderstand=""1"">urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>
  </s:Header>
  <s:Body>
    <d:Probe>
      <d:Types>dn:NetworkVideoTransmitter</d:Types>
    </d:Probe>
  </s:Body>
</s:Envelope>";
        }

        private static void ExtractXAddrs(string xml, HashSet<string> endpoints)
        {
            // XAddrs contains space-separated ONVIF service endpoint URLs
            var match = Regex.Match(xml, @"<[^:>]*:?XAddrs[^>]*>([^<]+)</[^:>]*:?XAddrs>");
            if (!match.Success) return;
            foreach (var addr in match.Groups[1].Value.Split(' '))
            {
                var trimmed = addr.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    endpoints.Add(trimmed);
            }
        }

        private static (string Manufacturer, string Model, string FirmwareVersion) GetDeviceInformation(
            string endpoint, SmokeConfig config)
        {
            // Build a minimal SOAP GetDeviceInformation request
            var body = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope""
            xmlns:wsse=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"">
  <s:Body>
    <tds:GetDeviceInformation xmlns:tds=""http://www.onvif.org/ver10/device/wsdl""/>
  </s:Body>
</s:Envelope>";

            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            request.ContentType = "application/soap+xml; charset=utf-8";
            request.Timeout = 10000;
            request.Headers.Add("SOAPAction", "\"http://www.onvif.org/ver10/device/wsdl/GetDeviceInformation\"");

            var bodyBytes = Encoding.UTF8.GetBytes(body);
            using (var stream = request.GetRequestStream())
                stream.Write(bodyBytes, 0, bodyBytes.Length);

            string responseXml;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
                responseXml = reader.ReadToEnd();

            return ParseDeviceInfo(responseXml);
        }

        private static (string Manufacturer, string Model, string FirmwareVersion) ParseDeviceInfo(string xml)
        {
            // Parse out Manufacturer, Model, FirmwareVersion from SOAP response
            string Extract(string tag)
            {
                var m = Regex.Match(xml, $@"<[^:>]*:?{tag}[^>]*>([^<]*)</[^:>]*:?{tag}>");
                return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
            }

            return (Extract("Manufacturer"), Extract("Model"), Extract("FirmwareVersion"));
        }

        private static CameraProfile MatchProfile(List<CameraProfile> profiles, string manufacturer, string model)
        {
            foreach (var profile in profiles)
            {
                bool manufacturerMatch = !string.IsNullOrEmpty(profile.ManufacturerContains)
                    && manufacturer.IndexOf(profile.ManufacturerContains, StringComparison.OrdinalIgnoreCase) >= 0;

                bool modelMatch = !string.IsNullOrEmpty(profile.ModelContains)
                    && model.IndexOf(profile.ModelContains, StringComparison.OrdinalIgnoreCase) >= 0;

                if (manufacturerMatch || modelMatch)
                    return profile;
            }
            return null; // unknown
        }

        private static string ExtractIp(string endpoint)
        {
            try
            {
                return new Uri(endpoint).Host;
            }
            catch
            {
                return endpoint;
            }
        }
    }
}
