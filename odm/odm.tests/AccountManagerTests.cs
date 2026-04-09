using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.ui.core;

namespace odm.tests
{
    /// <summary>
    /// Tests for AccountManager.
    ///
    /// Strategy:
    ///   AccountManager reads from CredentialStore at construction time (singleton).
    ///   We control CredentialStore._credentials and AccountManager._currentAccount
    ///   via reflection before each test, and redirect CredentialStore._storePath to
    ///   a temp file so DPAPI writes go to a safe location.
    /// </summary>
    [TestClass]
    public class AccountManagerTests
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

            // AccountManager private fields
            _amCurrentAccount = typeof(AccountManager).GetField("_currentAccount",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // CredentialStore private fields
            _csCredentials = typeof(CredentialStore).GetField("_credentials",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _csStorePath = typeof(CredentialStore).GetField("_storePath",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(_amCurrentAccount, "_currentAccount field not found on AccountManager");
            Assert.IsNotNull(_csCredentials,    "_credentials field not found on CredentialStore");
            Assert.IsNotNull(_csStorePath,      "_storePath field not found on CredentialStore");

            // Redirect store writes to a temp location
            _tempDir = Path.Combine(Path.GetTempPath(), "odm.tests_am_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _tempStorePath = Path.Combine(_tempDir, "test_credentials.dat");
            _csStorePath.SetValue(_cs, _tempStorePath);

            // Reset CredentialStore to empty
            _csCredentials.SetValue(_cs, new List<Account>());

            // Reset AccountManager to Anonymous
            _amCurrentAccount.SetValue(_am, Account.Anonymous);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Detach all event handlers to prevent cross-test contamination
            var eventField = typeof(AccountManager).GetField("CurrentAccountChanged",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (eventField != null)
                eventField.SetValue(_am, null);

            try { Directory.Delete(_tempDir, true); } catch { }
        }

        // ------------------------------------------------------------------
        // Initial state with empty store
        // ------------------------------------------------------------------

        [TestMethod]
        public void EmptyStore_CurrentAccountIsAnonymous()
        {
            // Store is empty (set in Setup)
            Assert.AreEqual(Account.Anonymous, _am.CurrentAccount);
        }

        [TestMethod]
        public void EmptyStore_AutorizedIsFalse()
        {
            Assert.IsFalse(_am.Autorized);
        }

        // ------------------------------------------------------------------
        // Initial state with non-empty store
        // (AccountManager reads store at construction, so we simulate by setting
        //  _currentAccount to stored[0] directly, mirroring the constructor logic)
        // ------------------------------------------------------------------

        [TestMethod]
        public void NonEmptyStore_CurrentAccountIsFirstStored()
        {
            // Pre-populate store and simulate what constructor would do
            var stored = new Account { Name = "alice", Password = "pw" };
            _csCredentials.SetValue(_cs, new List<Account> { stored });

            // Simulate constructor: set _currentAccount to first stored
            _amCurrentAccount.SetValue(_am, stored);

            Assert.AreEqual(stored, _am.CurrentAccount);
        }

        [TestMethod]
        public void NonEmptyStore_AutorizedIsTrue()
        {
            var stored = new Account { Name = "alice", Password = "pw" };
            _csCredentials.SetValue(_cs, new List<Account> { stored });
            _amCurrentAccount.SetValue(_am, stored);

            Assert.IsTrue(_am.Autorized);
        }

        // ------------------------------------------------------------------
        // SetCurrentAccount
        // ------------------------------------------------------------------

        [TestMethod]
        public void SetCurrentAccount_RememberFalse_SetsAccount_DoesNotPersist()
        {
            var acct = new Account { Name = "bob", Password = "secret" };

            _am.SetCurrentAccount(acct, remember: false);

            Assert.AreEqual(acct, _am.CurrentAccount);
            Assert.AreEqual(0, _cs.GetAll().Count, "Store must not be modified when remember=false");
        }

        [TestMethod]
        public void SetCurrentAccount_RememberTrue_SetsAccount_AddsToStore()
        {
            var acct = new Account { Name = "bob", Password = "secret" };

            _am.SetCurrentAccount(acct, remember: true);

            Assert.AreEqual(acct, _am.CurrentAccount);
            Assert.AreEqual(1, _cs.GetAll().Count);
            Assert.AreEqual("bob", _cs.GetAll()[0].Name);
        }

        [TestMethod]
        public void SetCurrentAccount_RememberTrue_DuplicateNotAddedTwice()
        {
            var acct = new Account { Name = "bob", Password = "secret" };
            _csCredentials.SetValue(_cs, new List<Account> { acct });

            _am.SetCurrentAccount(acct, remember: true);

            // Exact duplicate → should not be added again
            Assert.AreEqual(1, _cs.GetAll().Count);
        }

        [TestMethod]
        public void SetCurrentAccount_RememberTrue_SameNameDifferentPassword_AddedAsNew()
        {
            // BUG-1 fix: same name, different password → new entry
            var existing = new Account { Name = "bob", Password = "old" };
            _csCredentials.SetValue(_cs, new List<Account> { existing });

            var updated = new Account { Name = "bob", Password = "new" };
            _am.SetCurrentAccount(updated, remember: true);

            Assert.AreEqual(2, _cs.GetAll().Count, "Different password on same name should add a new entry");
        }

        [TestMethod]
        public void SetCurrentAccount_RememberTrue_AnonymousNotPersisted()
        {
            _am.SetCurrentAccount(Account.Anonymous, remember: true);

            Assert.AreEqual(0, _cs.GetAll().Count, "Anonymous account must never be persisted");
        }

        // ------------------------------------------------------------------
        // CurrentAccountChanged event
        // ------------------------------------------------------------------

        [TestMethod]
        public void CurrentAccountChanged_FiresWhenAccountChanges()
        {
            int fired = 0;
            _am.CurrentAccountChanged += (s, e) => fired++;

            var acct = new Account { Name = "charlie", Password = "pw" };
            _am.SetCurrentAccount(acct, remember: false);

            Assert.AreEqual(1, fired, "Event should fire once when account changes");
        }

        [TestMethod]
        public void CurrentAccountChanged_DoesNotFireWhenSameAccountSet()
        {
            var acct = new Account { Name = "charlie", Password = "pw" };
            // Set initial state
            _amCurrentAccount.SetValue(_am, acct);

            int fired = 0;
            _am.CurrentAccountChanged += (s, e) => fired++;

            // Set same account again
            _am.SetCurrentAccount(acct, remember: false);

            Assert.AreEqual(0, fired, "Event must not fire when same account is re-set");
        }

        [TestMethod]
        public void CurrentAccountChanged_FiresWhenAccountChangesFromNonAnonymous()
        {
            var acct1 = new Account { Name = "u1", Password = "p1" };
            var acct2 = new Account { Name = "u2", Password = "p2" };

            _amCurrentAccount.SetValue(_am, acct1);

            int fired = 0;
            _am.CurrentAccountChanged += (s, e) => fired++;

            _am.SetCurrentAccount(acct2, remember: false);

            Assert.AreEqual(1, fired);
        }

        // ------------------------------------------------------------------
        // GetAllCredentials delegates to CredentialStore
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetAllCredentials_ReflectsCredentialStore()
        {
            var list = new List<Account>
            {
                new Account { Name = "x", Password = "1" },
                new Account { Name = "y", Password = "2" }
            };
            _csCredentials.SetValue(_cs, list);

            var result = _am.GetAllCredentials();
            Assert.AreEqual(2, result.Count);
        }

        // ------------------------------------------------------------------
        // SetCredentials delegates to CredentialStore.SetAll
        // ------------------------------------------------------------------

        [TestMethod]
        public void SetCredentials_ReplacesStoreContents()
        {
            _csCredentials.SetValue(_cs, new List<Account> { new Account { Name = "old", Password = "p" } });

            var newCreds = new List<Account> { new Account { Name = "new1", Password = "np1" } };
            _am.SetCredentials(newCreds);

            var all = _cs.GetAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("new1", all[0].Name);
        }
    }
}
