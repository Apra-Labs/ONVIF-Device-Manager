using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.ui.views.SectionNVT;
using onvif.services;
using onvif.utils;

namespace odm.tests
{
    /// <summary>
    /// Some cameras list the same video source more than once; ODM used to crash loading them
    /// (Dictionary.Add in SectionSources.LoadChannel). One channel per video source is shown.
    /// </summary>
    [TestClass]
    public class DuplicateVideoSourceTests
    {
        static ChannelDescription Channel(string token)
        {
            return new ChannelDescription(new VideoSource { token = token }, new Profile[0]);
        }

        [TestMethod]
        public void SameVideoSourceTwice_LoadsOnce()
        {
            var channels = new[] { Channel("VideoSource_1"), Channel("VideoSource_1") };

            var shown = SectionSources.DistinctChannels(channels).ToArray();

            Assert.AreEqual(1, shown.Length, "a repeated video source is one channel");
            Assert.AreSame(channels[0], shown[0], "the first listing is the one kept");
        }

        [TestMethod]
        public void DifferentVideoSources_AllKept_InOrder()
        {
            var channels = new[] { Channel("A"), Channel("B"), Channel("A"), Channel("C"), Channel("B") };

            var tokens = SectionSources.DistinctChannels(channels).Select(c => c.videoSource.token).ToArray();

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, tokens);
        }

        [TestMethod]
        public void NoChannels_NothingShown()
        {
            Assert.AreEqual(0, SectionSources.DistinctChannels(new ChannelDescription[0]).Count());
        }
    }
}
