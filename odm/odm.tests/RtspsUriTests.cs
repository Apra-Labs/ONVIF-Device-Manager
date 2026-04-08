using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.ui.activities;

namespace odm.tests
{
    /// <summary>
    /// Offline unit tests for the RTSPS URI detection logic exposed via
    /// VideoPlayerActivity.IsRtspsUri(uri).
    ///
    /// Live555 does not support native RTSPS streaming; the check prevents a
    /// silent failure or crash when the camera returns an rtsps:// stream URI.
    ///
    /// Behavior:
    ///   - rtsps:// URI (any case) => true
    ///   - rtsp://  URI            => false
    ///   - http://  URI            => false
    ///   - null or empty string    => false
    /// </summary>
    [TestClass]
    public class RtspsUriTests
    {
        [TestMethod]
        public void IsRtsps_RtspsScheme_ReturnsTrue()
        {
            var result = VideoPlayerActivity.IsRtspsUri("rtsps://192.168.1.190/stream");

            Assert.IsTrue(result, "rtsps:// URI must be detected as RTSPS");
        }

        [TestMethod]
        public void IsRtsps_RtspsSchemeUppercase_ReturnsTrue()
        {
            var result = VideoPlayerActivity.IsRtspsUri("RTSPS://camera.local/live");

            Assert.IsTrue(result, "Uppercase RTSPS:// must be detected (check is case-insensitive)");
        }

        [TestMethod]
        public void IsRtsps_RtspScheme_ReturnsFalse()
        {
            var result = VideoPlayerActivity.IsRtspsUri("rtsp://192.168.1.190/stream");

            Assert.IsFalse(result, "Plain rtsp:// URI must not be detected as RTSPS");
        }

        [TestMethod]
        public void IsRtsps_HttpScheme_ReturnsFalse()
        {
            var result = VideoPlayerActivity.IsRtspsUri("http://camera.local/stream");

            Assert.IsFalse(result, "http:// URI must not be detected as RTSPS");
        }

        [TestMethod]
        public void IsRtsps_NullOrEmpty_ReturnsFalse()
        {
            Assert.IsFalse(VideoPlayerActivity.IsRtspsUri(null),
                "null URI must return false (no exception)");
            Assert.IsFalse(VideoPlayerActivity.IsRtspsUri(string.Empty),
                "Empty string URI must return false");
        }
    }
}
