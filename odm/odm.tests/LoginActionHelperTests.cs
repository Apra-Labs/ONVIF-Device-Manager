using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.ui.core;

namespace odm.tests
{
    [TestClass]
    public class LoginActionHelperTests
    {
        // Both name and password provided → Case1 regardless of store count

        [TestMethod]
        public void BothFields_ZeroStored_ReturnsCase1()
        {
            var result = LoginActionHelper.Determine("admin", "pass", 0);
            Assert.AreEqual(LoginAction.Case1SetAndRefresh, result);
        }

        [TestMethod]
        public void BothFields_ThreeStored_ReturnsCase1()
        {
            var result = LoginActionHelper.Determine("admin", "pass", 3);
            Assert.AreEqual(LoginAction.Case1SetAndRefresh, result);
        }

        // No fields, but store has entries → Case2

        [TestMethod]
        public void NoFields_ThreeStored_ReturnsCase2()
        {
            var result = LoginActionHelper.Determine("", "", 3);
            Assert.AreEqual(LoginAction.Case2RefreshWithStored, result);
        }

        // No fields, empty store → Case3

        [TestMethod]
        public void NoFields_ZeroStored_ReturnsCase3()
        {
            var result = LoginActionHelper.Determine("", "", 0);
            Assert.AreEqual(LoginAction.Case3Block, result);
        }

        // Username only (no password) → Case3 (hasFields requires both)

        [TestMethod]
        public void UsernameOnly_ThreeStored_ReturnsCase3()
        {
            var result = LoginActionHelper.Determine("admin", "", 3);
            Assert.AreEqual(LoginAction.Case3Block, result);
        }

        // Password only (no username) → Case3

        [TestMethod]
        public void PasswordOnly_ThreeStored_ReturnsCase3()
        {
            var result = LoginActionHelper.Determine("", "pass", 3);
            Assert.AreEqual(LoginAction.Case3Block, result);
        }

        // Null strings treated same as empty

        [TestMethod]
        public void NullName_NullPwd_ZeroStored_ReturnsCase3()
        {
            var result = LoginActionHelper.Determine(null, null, 0);
            Assert.AreEqual(LoginAction.Case3Block, result);
        }

        [TestMethod]
        public void NullName_NullPwd_OneStored_ReturnsCase2()
        {
            var result = LoginActionHelper.Determine(null, null, 1);
            Assert.AreEqual(LoginAction.Case2RefreshWithStored, result);
        }
    }
}
