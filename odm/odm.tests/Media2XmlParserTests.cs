using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using onvif.services;

namespace odm.tests
{
    /// <summary>
    /// Offline unit tests for Media2XmlParser — the static helper that converts
    /// raw WCF Message body strings from Media2 responses into ONVIF types.
    ///
    /// XML shape: body is whatever response.GetReaderAtBodyContents().ReadOuterXml()
    /// produces — the response wrapper element is the XDocument root.
    /// </summary>
    [TestClass]
    public class Media2XmlParserTests
    {
        // ----------------------------------------------------------------
        // Test 1: GetProfiles with two profiles (H264 + H265)
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetProfilesResponse_TwoProfiles_ReturnsBothWithCorrectFields()
        {
            const string xml = @"<GetProfilesResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'
                xmlns:tt='http://www.onvif.org/ver10/schema'>
              <tr2:Profiles token='Profile_H264' fixed='true'>
                <tt:Name>MainStream</tt:Name>
                <tt:VideoSourceConfiguration token='VSC_1'>
                  <tt:Name>VideoSource</tt:Name>
                  <tt:SourceToken>VideoSource_1</tt:SourceToken>
                </tt:VideoSourceConfiguration>
                <tt:VideoEncoderConfiguration token='VEC_H264'>
                  <tt:Name>H264Config</tt:Name>
                  <tt:Encoding>H264</tt:Encoding>
                  <tt:Resolution>
                    <tt:Width>1920</tt:Width>
                    <tt:Height>1080</tt:Height>
                  </tt:Resolution>
                  <tt:RateControl>
                    <tt:FrameRateLimit>30</tt:FrameRateLimit>
                    <tt:EncodingInterval>1</tt:EncodingInterval>
                    <tt:BitrateLimit>4000</tt:BitrateLimit>
                  </tt:RateControl>
                  <tt:H264>
                    <tt:GovLength>30</tt:GovLength>
                    <tt:H264Profile>Main</tt:H264Profile>
                  </tt:H264>
                </tt:VideoEncoderConfiguration>
              </tr2:Profiles>
              <tr2:Profiles token='Profile_H265' fixed='false'>
                <tt:Name>SubStream</tt:Name>
                <tt:VideoSourceConfiguration token='VSC_1'>
                  <tt:Name>VideoSource</tt:Name>
                  <tt:SourceToken>VideoSource_1</tt:SourceToken>
                </tt:VideoSourceConfiguration>
                <tt:VideoEncoderConfiguration token='VEC_H265'>
                  <tt:Name>H265Config</tt:Name>
                  <tt:Encoding>H265</tt:Encoding>
                  <tt:Resolution>
                    <tt:Width>1280</tt:Width>
                    <tt:Height>720</tt:Height>
                  </tt:Resolution>
                  <tt:RateControl>
                    <tt:FrameRateLimit>25</tt:FrameRateLimit>
                    <tt:EncodingInterval>1</tt:EncodingInterval>
                    <tt:BitrateLimit>2000</tt:BitrateLimit>
                  </tt:RateControl>
                  <tt:H265>
                    <tt:GovLength>60</tt:GovLength>
                  </tt:H265>
                </tt:VideoEncoderConfiguration>
              </tr2:Profiles>
            </GetProfilesResponse>";

            var profiles = Media2XmlParser.ParseGetProfilesResponse(xml);

            Assert.AreEqual(2, profiles.Length, "Expected 2 profiles");

            // ---- H264 profile ----
            var p1 = profiles[0];
            Assert.AreEqual("Profile_H264", p1.token);
            Assert.AreEqual("MainStream", p1.name);

            Assert.IsNotNull(p1.videoSourceConfiguration, "VSC should be set");
            Assert.AreEqual("VSC_1", p1.videoSourceConfiguration.token);
            Assert.AreEqual("VideoSource_1", p1.videoSourceConfiguration.sourceToken);

            Assert.IsNotNull(p1.videoEncoderConfiguration, "VEC should be set");
            Assert.AreEqual("VEC_H264", p1.videoEncoderConfiguration.token);
            Assert.AreEqual("H264Config", p1.videoEncoderConfiguration.name);
            Assert.AreEqual(VideoEncoding.h264, p1.videoEncoderConfiguration.encoding);
            Assert.AreEqual(1920, p1.videoEncoderConfiguration.resolution.width);
            Assert.AreEqual(1080, p1.videoEncoderConfiguration.resolution.height);
            Assert.IsNotNull(p1.videoEncoderConfiguration.rateControl);
            Assert.AreEqual(30, p1.videoEncoderConfiguration.rateControl.frameRateLimit);
            Assert.AreEqual(4000, p1.videoEncoderConfiguration.rateControl.bitrateLimit);
            Assert.IsNotNull(p1.videoEncoderConfiguration.h264);
            Assert.AreEqual(30, p1.videoEncoderConfiguration.h264.govLength);

            // ---- H265 profile ----
            var p2 = profiles[1];
            Assert.AreEqual("Profile_H265", p2.token);
            Assert.AreEqual("SubStream", p2.name);
            Assert.AreEqual(VideoEncoding.h265, p2.videoEncoderConfiguration.encoding);
            Assert.AreEqual(1280, p2.videoEncoderConfiguration.resolution.width);
            Assert.AreEqual(720, p2.videoEncoderConfiguration.resolution.height);
            Assert.IsNotNull(p2.videoEncoderConfiguration.h265, "H265 config block should be set");
            Assert.AreEqual(60, p2.videoEncoderConfiguration.h265.govLength);
        }

        // ----------------------------------------------------------------
        // Test 2: GetProfiles with no profiles returns empty array
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetProfilesResponse_NoProfiles_ReturnsEmptyArray()
        {
            const string xml = @"<GetProfilesResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'
                xmlns:tt='http://www.onvif.org/ver10/schema'>
            </GetProfilesResponse>";

            var profiles = Media2XmlParser.ParseGetProfilesResponse(xml);

            Assert.AreEqual(0, profiles.Length, "Expected empty array for response with no profiles");
        }

        // ----------------------------------------------------------------
        // Test 3: GetStreamUri response extracts URI correctly
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetStreamUriResponse_ValidUri_ReturnsUriString()
        {
            const string xml = @"<GetStreamUriResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'>
              <tr2:Uri>rtsp://192.168.1.100:554/onvif/stream1</tr2:Uri>
            </GetStreamUriResponse>";

            var uri = Media2XmlParser.ParseGetStreamUriResponse(xml);

            Assert.AreEqual("rtsp://192.168.1.100:554/onvif/stream1", uri);
        }

        // ----------------------------------------------------------------
        // Test 4: GetStreamUri response with empty URI returns null
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetStreamUriResponse_EmptyUri_ReturnsNull()
        {
            const string xml = @"<GetStreamUriResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'>
              <tr2:Uri></tr2:Uri>
            </GetStreamUriResponse>";

            var uri = Media2XmlParser.ParseGetStreamUriResponse(xml);

            Assert.IsNull(uri, "Empty Uri element should return null");
        }
    }
}
