using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace odm.e2e_tests.Tests
{
    /// <summary>
    /// Scenario 6: Error state detection — verify no unexpected error dialogs at end of test run.
    /// PoC phase 2 — marked Ignore until Scenarios 1-3 are verified.
    /// </summary>
    [TestClass]
    [Ignore("PoC phase 2 — implement after Scenarios 1-3 are verified")]
    public class ErrorDetectionTests : SmokeTestBase
    {
        [TestMethod]
        [TestCategory("Scenario6")]
        public void ErrorDetection_NoUnhandledExceptionWindows()
        {
            // TODO: Launch ODM and run through typical flows
            // TODO: Capture full window at end
            // TODO: Claude analysis: check for error dialogs, "not responding", crash reporters
        }
    }
}
