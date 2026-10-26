using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace odm.e2e_tests.Tests
{
    /// <summary>
    /// Scenario 5: HTTPS connectivity verification (Milesight cameras only).
    /// PoC phase 2 — marked Ignore until Scenarios 1-3 are verified.
    /// </summary>
    [TestClass]
    [Ignore("PoC phase 2 — implement after Scenarios 1-3 are verified")]
    public class HttpsTests : SmokeTestBase
    {
        [TestMethod]
        [TestCategory("Scenario5")]
        public void Https_MilesightConnectionSucceeds()
        {
            // TODO: Find Milesight camera in DiscoveredCameras
            // TODO: Assert.Inconclusive if no Milesight found
            // TODO: Connect via HTTPS port from profile
            // TODO: Verify no certificate error dialogs via screenshot + Claude
        }

        [TestMethod]
        [TestCategory("Scenario5")]
        public void Https_NoPlaintextFallbackWarning()
        {
            // TODO: After HTTPS connection, verify no plaintext fallback messages
        }
    }
}
