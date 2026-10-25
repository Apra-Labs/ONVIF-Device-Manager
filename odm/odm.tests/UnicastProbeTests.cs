using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// The unicast discovery probe's handling of answers (UnicastProbe.Parse): datagrams come from
    /// any host on the network, so only well-formed answers to our own probe are taken.
    /// </summary>
    [TestClass]
    public class UnicastProbeTests
    {
        const string ProbeId = "uuid:7d6e3b0a-1c2d-4e5f-8a9b-0c1d2e3f4a5b";

        static string ProbeMatch(string relatesTo, string appSequence = "<d:AppSequence InstanceId=\"1700000000\" SequenceId=\"urn:uuid:aaaa-bbbb\" MessageNumber=\"7\"/>",
                                 string xaddrs = "http://192.168.1.58:8080/onvif/device_service", string doctype = "")
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + doctype +
                "<e:Envelope xmlns:e=\"http://www.w3.org/2003/05/soap-envelope\" " +
                "xmlns:w=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" " +
                "xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\">" +
                "<e:Header>" +
                "<w:MessageID>uuid:11111111-2222-3333-4444-555555555555</w:MessageID>" +
                "<w:RelatesTo>" + relatesTo + "</w:RelatesTo>" +
                "<w:To>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</w:To>" +
                "<w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches</w:Action>" +
                appSequence +
                "</e:Header><e:Body><d:ProbeMatches><d:ProbeMatch>" +
                "<w:EndpointReference><w:Address>urn:uuid:12345678-90ab-cdef-1234-567890abcdef</w:Address></w:EndpointReference>" +
                "<d:Types>dn:NetworkVideoTransmitter</d:Types>" +
                "<d:Scopes>onvif://www.onvif.org/name/IPCAM onvif://www.onvif.org/location/country/china</d:Scopes>" +
                "<d:XAddrs>" + xaddrs + "</d:XAddrs>" +
                "<d:MetadataVersion>1</d:MetadataVersion>" +
                "</d:ProbeMatch></d:ProbeMatches></e:Body></e:Envelope>";
        }

        [TestMethod]
        public void Parse_ProbeMatch_GivesEndpointAddressesAndScopes()
        {
            var answer = UnicastProbe.Parse(ProbeMatch(ProbeId), ProbeId);

            Assert.IsNotNull(answer, "a ProbeMatch answering our probe should be taken");
            Assert.AreEqual("urn:uuid:12345678-90ab-cdef-1234-567890abcdef", answer.metadata.Address.Uri.OriginalString);
            CollectionAssert.AreEqual(new[] { "http://192.168.1.58:8080/onvif/device_service" },
                answer.metadata.ListenUris.Select(u => u.OriginalString).ToArray());
            CollectionAssert.AreEqual(new[] { "onvif://www.onvif.org/name/IPCAM", "onvif://www.onvif.org/location/country/china" },
                answer.metadata.Scopes.Select(u => u.OriginalString).ToArray());
        }

        [TestMethod]
        public void Parse_SeveralXAddrs_AllKept()
        {
            var answer = UnicastProbe.Parse(ProbeMatch(ProbeId, xaddrs: "http://192.168.1.58:8080/onvif/device_service http://[fe80::1]/onvif/device_service"), ProbeId);

            Assert.IsNotNull(answer);
            Assert.AreEqual(2, answer.metadata.ListenUris.Count);
        }

        [TestMethod]
        public void Parse_KeepsTheDevicesOwnAppSequence()
        {
            var answer = UnicastProbe.Parse(ProbeMatch(ProbeId), ProbeId);

            Assert.IsNotNull(answer);
            Assert.AreEqual(1700000000L, answer.sequence.InstanceId, "the device's InstanceId, so its Bye can be compared");
            Assert.AreEqual(7L, answer.sequence.MessageNumber);
            Assert.AreEqual("urn:uuid:aaaa-bbbb", answer.sequence.SequenceId.OriginalString);
        }

        [TestMethod]
        public void Parse_AppSequenceOutOfRange_GeneratedOneUsed()
        {
            // InstanceId and MessageNumber are xs:unsignedInt: negative or over 32 bits isn't the device's
            foreach (var bad in new[] { "InstanceId=\"-5\" MessageNumber=\"7\"", "InstanceId=\"5000000000\" MessageNumber=\"7\"",
                                        "InstanceId=\"1700000000\" MessageNumber=\"x\"" })
            {
                var answer = UnicastProbe.Parse(ProbeMatch(ProbeId, appSequence: "<d:AppSequence " + bad + " SequenceId=\"urn:uuid:aaaa-bbbb\"/>"), ProbeId);

                Assert.IsNotNull(answer, "still a camera: " + bad);
                Assert.IsNotNull(answer.sequence, bad);
                Assert.AreNotEqual("urn:uuid:aaaa-bbbb", answer.sequence.SequenceId?.OriginalString, "a generated sequence: " + bad);
            }
        }

        [TestMethod]
        public void Sequence_TheDevicesValuesKept()
        {
            var sequence = UnicastProbe.Sequence(" 4294967295 ", "urn:uuid:cccc", "0");

            Assert.AreEqual(4294967295L, sequence.InstanceId, "the largest xs:unsignedInt");
            Assert.AreEqual(0L, sequence.MessageNumber);
            Assert.AreEqual("urn:uuid:cccc", sequence.SequenceId.OriginalString);
        }

        [TestMethod]
        public void Parse_NoAppSequence_StillTaken()
        {
            var answer = UnicastProbe.Parse(ProbeMatch(ProbeId, appSequence: ""), ProbeId);

            Assert.IsNotNull(answer, "an answer without an AppSequence is still a camera");
            Assert.IsNotNull(answer.sequence);
        }

        [TestMethod]
        public void Parse_AnswerToAnotherProbe_Ignored()
        {
            var answer = UnicastProbe.Parse(ProbeMatch("uuid:00000000-0000-0000-0000-000000000000"), ProbeId);

            Assert.IsNull(answer, "an answer whose RelatesTo isn't our probe's MessageID must not add a device");
        }

        [TestMethod]
        public void Parse_NoRelatesTo_Ignored()
        {
            var xml = ProbeMatch(ProbeId).Replace("<w:RelatesTo>" + ProbeId + "</w:RelatesTo>", "");

            Assert.IsNull(UnicastProbe.Parse(xml, ProbeId));
        }

        [TestMethod]
        public void Parse_XmlWithDtd_Rejected()
        {
            var xml = ProbeMatch(ProbeId, doctype: "<!DOCTYPE e:Envelope [<!ENTITY x \"192.168.1.58\">]>");

            Assert.IsNull(UnicastProbe.Parse(xml, ProbeId), "a DTD must not be processed - the answer is dropped");
        }

        [TestMethod]
        public void Parse_NotXml_Ignored()
        {
            Assert.IsNull(UnicastProbe.Parse("M-SEARCH * HTTP/1.1\r\n\r\n", ProbeId));
            Assert.IsNull(UnicastProbe.Parse("", ProbeId));
        }

        [TestMethod]
        public void Parse_NoUsableAddress_Ignored()
        {
            Assert.IsNull(UnicastProbe.Parse(ProbeMatch(ProbeId, xaddrs: "not-a-uri"), ProbeId));
        }
    }
}
