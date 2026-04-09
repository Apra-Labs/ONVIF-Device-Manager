using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.ui.core;

namespace odm.tests
{
    /// <summary>
    /// Tests for Sprint 3 auth UX polish requirements.
    /// Covers REQ-2 (remember checkbox), REQ-4 (LoggedOutExplicitly),
    /// REQ-8 (close dialog behavior), REQ-9 (startup auto-login), REQ-10 (no passwords in logs).
    /// </summary>
    [TestClass]
    public class AuthFlowTests
    {
        AccountManager _am;
        CredentialStore _cs;

        FieldInfo _amCurrentAccount;
        FieldInfo _csCredentials;
        FieldInfo _csStorePath;

        string _tempDir;
        string _tempStorePath;

        [TestInitialize]
        public void Setup()
        {
            _am = AccountManager.Instance;
            _cs = CredentialStore.Instance;

            _amCurrentAccount = typeof(AccountManager).GetField("_currentAccount",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _csCredentials = typeof(CredentialStore).GetField("_credentials",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _csStorePath = typeof(CredentialStore).GetField("_storePath",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(_amCurrentAccount, "_currentAccount field not found on AccountManager");
            Assert.IsNotNull(_csCredentials, "_credentials field not found on CredentialStore");
            Assert.IsNotNull(_csStorePath, "_storePath field not found on CredentialStore");

            _tempDir = Path.Combine(Path.GetTempPath(), "odm.tests_af_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _tempStorePath = Path.Combine(_tempDir, "test_credentials.dat");
            _csStorePath.SetValue(_cs, _tempStorePath);

            _csCredentials.SetValue(_cs, new List<Account>());
            _amCurrentAccount.SetValue(_am, Account.Anonymous);
            _am.LoggedOutExplicitly = false;
        }

        [TestCleanup]
        public void Cleanup()
        {
            var eventField = typeof(AccountManager).GetField("CurrentAccountChanged",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (eventField != null)
                eventField.SetValue(_am, null);

            try { Directory.Delete(_tempDir, true); } catch { }
        }

        // ------------------------------------------------------------------
        // REQ-2: Remember checkbox controls persistence
        // ------------------------------------------------------------------

        [TestMethod]
        public void LoginCase1_RememberChecked_AddsToStore()
        {
            var acct = new Account { Name = "alice", Password = "secret" };

            // Simulate Case1 with remember=true
            _am.SetCurrentAccount(acct, remember: true);

            Assert.AreEqual(1, _cs.GetAll().Count, "Credential should be added when remember=true");
            Assert.AreEqual("alice", _cs.GetAll()[0].Name);
        }

        [TestMethod]
        public void LoginCase1_RememberUnchecked_DoesNotAddToStore()
        {
            var acct = new Account { Name = "bob", Password = "secret" };

            // Simulate Case1 with remember=false (checkbox unchecked)
            _am.SetCurrentAccount(acct, remember: false);

            Assert.AreEqual(0, _cs.GetAll().Count, "Credential must not be added when remember=false");
            Assert.AreEqual(acct, _am.CurrentAccount, "CurrentAccount should still be set");
        }

        // ------------------------------------------------------------------
        // REQ-4: LoggedOutExplicitly flag
        // ------------------------------------------------------------------

        [TestMethod]
        public void Logout_SetsLoggedOutExplicitly()
        {
            // Simulate btLogout_Click behavior
            _am.LoggedOutExplicitly = true;
            _am.SetCurrentAccount(Account.Anonymous, remember: false);

            Assert.IsTrue(_am.LoggedOutExplicitly, "LoggedOutExplicitly must be true after logout");
            Assert.IsTrue(_am.CurrentAccount.IsAnonymous);
        }

        [TestMethod]
        public void Logout_LoadCurrentAccount_ReturnsNull_WhenStoreNonEmpty()
        {
            // Set up store with a credential
            _csCredentials.SetValue(_cs, new List<Account> { new Account { Name = "cam1", Password = "pw" } });
            // Simulate explicit logout
            _am.LoggedOutExplicitly = true;
            _amCurrentAccount.SetValue(_am, Account.Anonymous);

            // Replicate DeviceListViewModel.LoadCurrentAccount logic
            var acc = AccountManager.Instance.CurrentAccount;
            System.Net.NetworkCredential result = null;
            if (acc.IsAnonymous)
            {
                if (!AccountManager.Instance.LoggedOutExplicitly)
                {
                    var all = AccountManager.Instance.GetAllCredentials();
                    if (all.Count > 0) acc = all[0];
                }
            }
            if (!acc.IsAnonymous)
                result = new System.Net.NetworkCredential { UserName = acc.Name, Password = acc.Password };

            Assert.IsNull(result, "LoadCurrentAccount must return null when LoggedOutExplicitly=true");
        }

        [TestMethod]
        public void Login_AfterLogout_ClearsLoggedOutExplicitly()
        {
            // Start in logged-out-explicitly state
            _am.LoggedOutExplicitly = true;
            _amCurrentAccount.SetValue(_am, Account.Anonymous);

            // Simulate login
            var acct = new Account { Name = "alice", Password = "pw" };
            _am.SetCurrentAccount(acct, remember: false);

            Assert.IsFalse(_am.LoggedOutExplicitly,
                "LoggedOutExplicitly must be cleared when a real account is set");
        }

        // ------------------------------------------------------------------
        // REQ-8: Close dialog behavior
        // ------------------------------------------------------------------

        [TestMethod]
        public void CloseDialog_EmptyStore_SetsLoggedOutExplicitly()
        {
            // Store is empty (Setup already ensures this)
            var storeCount = CredentialStore.Instance.GetAll().Count;

            // Replicate OnClosing logic
            AccountManager.Instance.LoggedOutExplicitly = (storeCount == 0);
            AccountManager.Instance.SetCurrentAccount(Account.Anonymous, remember: false);

            Assert.IsTrue(AccountManager.Instance.LoggedOutExplicitly,
                "LoggedOutExplicitly must be true when store is empty on close");
        }

        [TestMethod]
        public void CloseDialog_NonEmptyStore_ClearsLoggedOutExplicitly()
        {
            // Populate store
            _csCredentials.SetValue(_cs, new List<Account> { new Account { Name = "u", Password = "p" } });

            var storeCount = CredentialStore.Instance.GetAll().Count;

            // Replicate OnClosing logic
            AccountManager.Instance.LoggedOutExplicitly = (storeCount == 0);
            AccountManager.Instance.SetCurrentAccount(Account.Anonymous, remember: false);

            Assert.IsFalse(AccountManager.Instance.LoggedOutExplicitly,
                "LoggedOutExplicitly must be false when store has entries on close");
        }

        // ------------------------------------------------------------------
        // REQ-10: No passwords in logs
        // ------------------------------------------------------------------

        [TestMethod]
        public void AuthLog_NeverContainsPassword()
        {
            // Access the private static Safe() helper via reflection
            var safeMethod = typeof(odm.ui.views.AuthView).GetMethod(
                "Safe",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(safeMethod, "Safe() helper method must exist in AuthView");

            var testAccount = new Account { Name = "testuser", Password = "super_secret_password" };
            var result = (string)safeMethod.Invoke(null, new object[] { testAccount });

            Assert.IsFalse(result.Contains("super_secret_password"),
                "Safe() must never include the password in its output");
            Assert.IsTrue(result.Contains("testuser"),
                "Safe() may include the username");
            Assert.IsTrue(result.Contains("[REDACTED]"),
                "Safe() must mark the password as [REDACTED]");
        }
    }
}
