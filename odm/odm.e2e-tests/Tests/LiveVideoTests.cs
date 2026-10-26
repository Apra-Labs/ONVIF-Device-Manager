using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace odm.e2e_tests.Tests
{
    /// <summary>
    /// Scenario 4: Live video stream — connect to camera and verify video player shows feed.
    /// PoC phase 2 — marked Ignore until Scenarios 1-3 are verified.
    /// </summary>
    [TestClass]
    [Ignore("PoC phase 2 — implement after Scenarios 1-3 are verified")]
    public class LiveVideoTests : SmokeTestBase
    {
        [TestMethod]
        [TestCategory("Scenario4")]
        public void LiveVideo_MilesightStreamLoads()
        {
            // TODO: Click on Milesight camera in device list
            // TODO: Navigate to live video view
            // TODO: Wait for video player to load
            // TODO: Capture screenshot and verify with Claude
        }

        [TestMethod]
        [TestCategory("Scenario4")]
        public void LiveVideo_VirtualCameraStreamLoads()
        {
            // TODO: Click on Virtual camera in device list
            // TODO: Navigate to live video view
            // TODO: Wait for video player to load
            // TODO: Capture screenshot and verify with Claude
        }
    }
}
