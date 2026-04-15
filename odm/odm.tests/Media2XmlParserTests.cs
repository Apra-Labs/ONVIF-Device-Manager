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

        // ----------------------------------------------------------------
        // Test 5: GetVideoEncoderConfigurationOptions H265 + H264 options
        //         → parser populates options.h265 and options.h264 with
        //           correct ranges (this is the fix for issue #21)
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetVideoEncoderConfigurationOptionsResponse_H265AndH264_PopulatesCorrectSubObjects()
        {
            const string xml = @"<GetVideoEncoderConfigurationOptionsResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'
                xmlns:tt='http://www.onvif.org/ver10/schema'>
              <tr2:Options>
                <tt:Encoding>H265</tt:Encoding>
                <tt:ResolutionsAvailable>
                  <tt:Width>3840</tt:Width>
                  <tt:Height>2160</tt:Height>
                </tt:ResolutionsAvailable>
                <tt:ResolutionsAvailable>
                  <tt:Width>1920</tt:Width>
                  <tt:Height>1080</tt:Height>
                </tt:ResolutionsAvailable>
                <tt:GovLengthRange>
                  <tt:Min>1</tt:Min>
                  <tt:Max>300</tt:Max>
                </tt:GovLengthRange>
                <tt:FrameRateRange>
                  <tt:Min>1</tt:Min>
                  <tt:Max>30</tt:Max>
                </tt:FrameRateRange>
                <tt:BitrateRange>
                  <tt:Min>128</tt:Min>
                  <tt:Max>16000</tt:Max>
                </tt:BitrateRange>
              </tr2:Options>
              <tr2:Options>
                <tt:Encoding>H264</tt:Encoding>
                <tt:ResolutionsAvailable>
                  <tt:Width>1920</tt:Width>
                  <tt:Height>1080</tt:Height>
                </tt:ResolutionsAvailable>
                <tt:GovLengthRange>
                  <tt:Min>2</tt:Min>
                  <tt:Max>150</tt:Max>
                </tt:GovLengthRange>
                <tt:FrameRateRange>
                  <tt:Min>1</tt:Min>
                  <tt:Max>60</tt:Max>
                </tt:FrameRateRange>
                <tt:BitrateRange>
                  <tt:Min>256</tt:Min>
                  <tt:Max>8000</tt:Max>
                </tt:BitrateRange>
              </tr2:Options>
            </GetVideoEncoderConfigurationOptionsResponse>";

            var opts = Media2XmlParser.ParseGetVideoEncoderConfigurationOptionsResponse(xml);

            Assert.IsNotNull(opts, "Result must not be null");

            // H265 sub-object
            Assert.IsNotNull(opts.h265, "h265 options must be populated");
            Assert.AreEqual(2, opts.h265.resolutionsAvailable.Length, "h265 should have 2 resolutions");
            Assert.AreEqual(3840, opts.h265.resolutionsAvailable[0].width);
            Assert.AreEqual(2160, opts.h265.resolutionsAvailable[0].height);
            Assert.IsNotNull(opts.h265.govLengthRange);
            Assert.AreEqual(1,   opts.h265.govLengthRange.min);
            Assert.AreEqual(300, opts.h265.govLengthRange.max);
            Assert.IsNotNull(opts.h265.frameRateRange);
            Assert.AreEqual(1,  opts.h265.frameRateRange.min);
            Assert.AreEqual(30, opts.h265.frameRateRange.max);

            // H264 sub-object
            Assert.IsNotNull(opts.h264, "h264 options must be populated");
            Assert.AreEqual(1, opts.h264.resolutionsAvailable.Length, "h264 should have 1 resolution");
            Assert.AreEqual(1920, opts.h264.resolutionsAvailable[0].width);
            Assert.AreEqual(1080, opts.h264.resolutionsAvailable[0].height);
            Assert.IsNotNull(opts.h264.govLengthRange);
            Assert.AreEqual(2,   opts.h264.govLengthRange.min);
            Assert.AreEqual(150, opts.h264.govLengthRange.max);
            Assert.IsNotNull(opts.h264.frameRateRange);
            Assert.AreEqual(1,  opts.h264.frameRateRange.min);
            Assert.AreEqual(60, opts.h264.frameRateRange.max);

            // H265 was parsed — h264 must also be set, jpeg must remain null
            Assert.IsNull(opts.jpeg, "jpeg should not be set when only H265+H264 options present");
        }

        // ----------------------------------------------------------------
        // Test 6: Options response with missing GovLengthRange
        //         → field is null (not default-initialised), no exception
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetVideoEncoderConfigurationOptionsResponse_MissingGovLengthRange_IsNullNoException()
        {
            const string xml = @"<GetVideoEncoderConfigurationOptionsResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'
                xmlns:tt='http://www.onvif.org/ver10/schema'>
              <tr2:Options>
                <tt:Encoding>H265</tt:Encoding>
                <tt:ResolutionsAvailable>
                  <tt:Width>1920</tt:Width>
                  <tt:Height>1080</tt:Height>
                </tt:ResolutionsAvailable>
                <tt:FrameRateRange>
                  <tt:Min>1</tt:Min>
                  <tt:Max>25</tt:Max>
                </tt:FrameRateRange>
                <tt:BitrateRange>
                  <tt:Min>128</tt:Min>
                  <tt:Max>8000</tt:Max>
                </tt:BitrateRange>
              </tr2:Options>
            </GetVideoEncoderConfigurationOptionsResponse>";

            VideoEncoderConfigurationOptions opts = null;
            var ex = default(Exception);
            try { opts = Media2XmlParser.ParseGetVideoEncoderConfigurationOptionsResponse(xml); }
            catch (Exception e) { ex = e; }

            Assert.IsNull(ex, "Parser must not throw when GovLengthRange is absent");
            Assert.IsNotNull(opts);
            Assert.IsNotNull(opts.h265, "h265 must be populated");
            Assert.IsNull(opts.h265.govLengthRange, "govLengthRange must be null when element is absent");
        }

        // ----------------------------------------------------------------
        // Test 7: Options response with empty body
        //         → returns default (non-null) options object, no exception
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetVideoEncoderConfigurationOptionsResponse_EmptyBody_ReturnsDefaultNoException()
        {
            const string xml = @"<GetVideoEncoderConfigurationOptionsResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'
                xmlns:tt='http://www.onvif.org/ver10/schema'>
            </GetVideoEncoderConfigurationOptionsResponse>";

            var opts = Media2XmlParser.ParseGetVideoEncoderConfigurationOptionsResponse(xml);

            Assert.IsNotNull(opts, "Must return non-null default options");
            Assert.IsNull(opts.h264, "h264 must be null for empty response");
            Assert.IsNull(opts.h265, "h265 must be null for empty response");
            Assert.IsNull(opts.jpeg, "jpeg must be null for empty response");
        }

        // ----------------------------------------------------------------
        // Test 8: BuildSetVideoEncoderConfigurationElement constructs
        //         correct XML structure for an H265 configuration
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSetVideoEncoderConfigurationElement_H265Config_ProducesCorrectXml()
        {
            var config = new VideoEncoderConfiguration {
                token      = "VEC_H265",
                name       = "H265Stream",
                encoding   = VideoEncoding.h265,
                resolution = new VideoResolution { width = 1920, height = 1080 },
                rateControl = new VideoRateControl {
                    frameRateLimit   = 25,
                    encodingInterval = 1,
                    bitrateLimit     = 4000
                },
                h265    = new H265Configuration { govLength = 60 },
                quality = 5.0f
            };

            var el = Media2XmlParser.BuildSetVideoEncoderConfigurationElement(config);

            Assert.IsNotNull(el, "Returned element must not be null");

            const string nsTt = "http://www.onvif.org/ver10/schema";

            // token attribute
            Assert.AreEqual("VEC_H265", (string)el.Attribute("token"));

            // name
            Assert.AreEqual("H265Stream", (string)el.Element(System.Xml.Linq.XName.Get("Name", nsTt)));

            // encoding
            Assert.AreEqual("H265", (string)el.Element(System.Xml.Linq.XName.Get("Encoding", nsTt)));

            // resolution
            var resEl = el.Element(System.Xml.Linq.XName.Get("Resolution", nsTt));
            Assert.IsNotNull(resEl, "Resolution element required");
            Assert.AreEqual("1920", (string)resEl.Element(System.Xml.Linq.XName.Get("Width",  nsTt)));
            Assert.AreEqual("1080", (string)resEl.Element(System.Xml.Linq.XName.Get("Height", nsTt)));

            // rateControl
            var rcEl = el.Element(System.Xml.Linq.XName.Get("RateControl", nsTt));
            Assert.IsNotNull(rcEl, "RateControl element required");
            Assert.AreEqual("25",   (string)rcEl.Element(System.Xml.Linq.XName.Get("FrameRateLimit",   nsTt)));
            Assert.AreEqual("1",    (string)rcEl.Element(System.Xml.Linq.XName.Get("EncodingInterval", nsTt)));
            Assert.AreEqual("4000", (string)rcEl.Element(System.Xml.Linq.XName.Get("BitrateLimit",     nsTt)));

            // H265 block
            var h265El = el.Element(System.Xml.Linq.XName.Get("H265", nsTt));
            Assert.IsNotNull(h265El, "H265 element required for H265 encoding");
            Assert.AreEqual("60", (string)h265El.Element(System.Xml.Linq.XName.Get("GovLength", nsTt)));

            // H264 block must NOT be present
            Assert.IsNull(el.Element(System.Xml.Linq.XName.Get("H264", nsTt)), "H264 element must be absent for H265 encoding");
        }
        // ----------------------------------------------------------------
        // Test 9: GetVideoEncoderConfigurations response with full fields
        //         → parser returns VideoEncoderConfiguration[] with all fields
        //           populated (encoding, resolution, rateControl, govLength,
        //           h265 profile)
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetVideoEncoderConfigurationsResponse_FullFields_AllFieldsPopulated()
        {
            const string xml = @"<GetVideoEncoderConfigurationsResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'
                xmlns:tt='http://www.onvif.org/ver10/schema'>
              <tr2:Configurations token='VEC_H265'>
                <tt:Name>H265Stream</tt:Name>
                <tt:Encoding>H265</tt:Encoding>
                <tt:Resolution>
                  <tt:Width>1920</tt:Width>
                  <tt:Height>1080</tt:Height>
                </tt:Resolution>
                <tt:RateControl>
                  <tt:FrameRateLimit>25</tt:FrameRateLimit>
                  <tt:EncodingInterval>1</tt:EncodingInterval>
                  <tt:BitrateLimit>4000</tt:BitrateLimit>
                </tt:RateControl>
                <tt:H265>
                  <tt:GovLength>60</tt:GovLength>
                </tt:H265>
                <tt:Quality>5</tt:Quality>
              </tr2:Configurations>
              <tr2:Configurations token='VEC_H264'>
                <tt:Name>H264Stream</tt:Name>
                <tt:Encoding>H264</tt:Encoding>
                <tt:Resolution>
                  <tt:Width>1280</tt:Width>
                  <tt:Height>720</tt:Height>
                </tt:Resolution>
                <tt:RateControl>
                  <tt:FrameRateLimit>30</tt:FrameRateLimit>
                  <tt:EncodingInterval>1</tt:EncodingInterval>
                  <tt:BitrateLimit>2000</tt:BitrateLimit>
                </tt:RateControl>
                <tt:H264>
                  <tt:GovLength>30</tt:GovLength>
                </tt:H264>
              </tr2:Configurations>
            </GetVideoEncoderConfigurationsResponse>";

            var cfgs = Media2XmlParser.ParseGetVideoEncoderConfigurationsResponse(xml);

            Assert.AreEqual(2, cfgs.Length, "Expected 2 configurations");

            // H265 config
            var c1 = cfgs[0];
            Assert.AreEqual("VEC_H265", c1.token);
            Assert.AreEqual("H265Stream", c1.name);
            Assert.AreEqual(VideoEncoding.h265, c1.encoding);
            Assert.IsNotNull(c1.resolution);
            Assert.AreEqual(1920, c1.resolution.width);
            Assert.AreEqual(1080, c1.resolution.height);
            Assert.IsNotNull(c1.rateControl);
            Assert.AreEqual(25, c1.rateControl.frameRateLimit);
            Assert.AreEqual(4000, c1.rateControl.bitrateLimit);
            Assert.IsNotNull(c1.h265, "H265 block must be populated");
            Assert.AreEqual(60, c1.h265.govLength);
            Assert.IsNull(c1.h264, "H264 block must be absent for H265 config");

            // H264 config
            var c2 = cfgs[1];
            Assert.AreEqual("VEC_H264", c2.token);
            Assert.AreEqual(VideoEncoding.h264, c2.encoding);
            Assert.AreEqual(1280, c2.resolution.width);
            Assert.AreEqual(720, c2.resolution.height);
            Assert.IsNotNull(c2.h264, "H264 block must be populated");
            Assert.AreEqual(30, c2.h264.govLength);
            Assert.IsNull(c2.h265, "H265 block must be absent for H264 config");
        }

        // ----------------------------------------------------------------
        // Test 10: GetVideoSourceConfigurations response
        //          → parser returns VideoSourceConfiguration[] with token,
        //            name, sourceToken, bounds
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetVideoSourceConfigurationsResponse_ValidResponse_AllFieldsPopulated()
        {
            const string xml = @"<GetVideoSourceConfigurationsResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'
                xmlns:tt='http://www.onvif.org/ver10/schema'>
              <tr2:Configurations token='VSC_1'>
                <tt:Name>VideoSource_1</tt:Name>
                <tt:SourceToken>VideoSource_Channel1</tt:SourceToken>
                <tt:Bounds x='0' y='0' width='1920' height='1080'/>
              </tr2:Configurations>
              <tr2:Configurations token='VSC_2'>
                <tt:Name>VideoSource_2</tt:Name>
                <tt:SourceToken>VideoSource_Channel2</tt:SourceToken>
                <tt:Bounds x='0' y='0' width='1280' height='720'/>
              </tr2:Configurations>
            </GetVideoSourceConfigurationsResponse>";

            var vscs = Media2XmlParser.ParseGetVideoSourceConfigurationsResponse(xml);

            Assert.AreEqual(2, vscs.Length, "Expected 2 source configurations");

            var v1 = vscs[0];
            Assert.AreEqual("VSC_1", v1.token);
            Assert.AreEqual("VideoSource_1", v1.name);
            Assert.AreEqual("VideoSource_Channel1", v1.sourceToken);
            Assert.IsNotNull(v1.bounds, "Bounds must be populated");
            Assert.AreEqual(1920, v1.bounds.width);
            Assert.AreEqual(1080, v1.bounds.height);
            Assert.AreEqual(0, v1.bounds.x);
            Assert.AreEqual(0, v1.bounds.y);

            var v2 = vscs[1];
            Assert.AreEqual("VSC_2", v2.token);
            Assert.AreEqual("VideoSource_Channel2", v2.sourceToken);
            Assert.AreEqual(1280, v2.bounds.width);
            Assert.AreEqual(720, v2.bounds.height);
        }

        // ----------------------------------------------------------------
        // Test 11: Empty responses for encoder configs and source configs
        //          → both return empty arrays with no exceptions
        // ----------------------------------------------------------------
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseGetVideoEncoderAndSourceConfigurationsResponse_EmptyBody_ReturnsEmptyArraysNoException()
        {
            const string emptyEncoderXml = @"<GetVideoEncoderConfigurationsResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'
                xmlns:tt='http://www.onvif.org/ver10/schema'>
            </GetVideoEncoderConfigurationsResponse>";

            const string emptySourceXml = @"<GetVideoSourceConfigurationsResponse
                xmlns:tr2='http://www.onvif.org/ver20/media/wsdl'
                xmlns:tt='http://www.onvif.org/ver10/schema'>
            </GetVideoSourceConfigurationsResponse>";

            VideoEncoderConfiguration[] encoderCfgs = null;
            VideoSourceConfiguration[] sourceCfgs = null;
            Exception ex = null;

            try {
                encoderCfgs = Media2XmlParser.ParseGetVideoEncoderConfigurationsResponse(emptyEncoderXml);
                sourceCfgs  = Media2XmlParser.ParseGetVideoSourceConfigurationsResponse(emptySourceXml);
            } catch (Exception e) {
                ex = e;
            }

            Assert.IsNull(ex, "No exception should be thrown for empty responses");
            Assert.IsNotNull(encoderCfgs);
            Assert.AreEqual(0, encoderCfgs.Length, "Encoder configs: expected empty array");
            Assert.IsNotNull(sourceCfgs);
            Assert.AreEqual(0, sourceCfgs.Length, "Source configs: expected empty array");
        }
    }
}
