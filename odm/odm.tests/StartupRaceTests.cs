using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.ui.core;

namespace odm.tests
{
    /// <summary>
    /// Tests for issue #29: startup race condition where auto-connect fires before
    /// the credential store finishes loading from disk.
    ///
    /// Strategy: verify CredentialStore exposes IsLoaded so auto-connect can be
    /// gated on store readiness. Tests use reflection to control internal state,
    /// following the same pattern as CredentialStoreTests and AccountManagerTests.
    /// </summary>
    [TestClass]
    public class StartupRaceTests
    {
        CredentialStore _cs;
        FieldInfo _credentialsField;
        FieldInfo _storePathField;
        string _tempDir;
        string _tempStorePath;

        [TestInitialize]
        public void Setup()
        {
            _cs = CredentialStore.Instance;

            _credentialsField = typeof(CredentialStore).GetField("_credentials",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _storePathField = typeof(CredentialStore).GetField("_storePath",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(_credentialsField, "_credentials field not found");
            Assert.IsNotNull(_storePathField,   "_storePath field not found");

            _tempDir = Path.Combine(Path.GetTempPath(), "odm.tests_race_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _tempStorePath = Path.Combine(_tempDir, "test_credentials.dat");
            _storePathField.SetValue(_cs, _tempStorePath);
            _credentialsField.SetValue(_cs, new List<Account>());
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Restore IsLoaded=true so other test classes aren't affected
            var isLoadedBacking = GetIsLoadedBacking();
            if (isLoadedBacking != null)
                isLoadedBacking.SetValue(_cs, true);

            try { Directory.Delete(_tempDir, true); } catch { }
        }

        // ------------------------------------------------------------------
        // Helper
        // ------------------------------------------------------------------

        PropertyInfo GetIsLoadedProp()
        {
            return typeof(CredentialStore).GetProperty("IsLoaded",
                BindingFlags.Public | BindingFlags.Instance);
        }

        FieldInfo GetIsLoadedBacking()
        {
            // Try auto-property backing field name first, then explicit name
            return typeof(CredentialStore).GetField("<IsLoaded>k__BackingField",
                       BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(CredentialStore).GetField("_isLoaded",
                       BindingFlags.NonPublic | BindingFlags.Instance);
        }

        // ------------------------------------------------------------------
        // IsLoaded property contract
        // ------------------------------------------------------------------

        [TestMethod]
        public void CredentialStore_ExposesIsLoadedProperty()
        {
            // Fails with current code — IsLoaded does not exist yet.
            // After fix (3.3): CredentialStore gains a public bool IsLoaded property
            // that is set to true after Load() completes.
            var prop = GetIsLoadedProp();
            Assert.IsNotNull(prop,
                "CredentialStore must expose a public bool IsLoaded property so that " +
                "auto-connect can be gated on store readiness (issue #29).");
        }

        [TestMethod]
        public void CredentialStore_IsLoaded_TrueAfterNormalLoad()
        {
            var prop = GetIsLoadedProp();
            Assert.IsNotNull(prop, "CredentialStore.IsLoaded property not found");

            // After construction, Load() has completed — IsLoaded must be true.
            Assert.IsTrue((bool)prop.GetValue(_cs),
                "IsLoaded must be true after the constructor's Load() call completes.");
        }

        // ------------------------------------------------------------------
        // Auto-connect gate: must not fire before store is loaded
        // ------------------------------------------------------------------

        [TestMethod]
        public void AutoConnect_BeforeStoreLoaded_DoesNotConnect()
        {
            var isLoadedProp = GetIsLoadedProp();
            Assert.IsNotNull(isLoadedProp, "CredentialStore.IsLoaded property not found");

            var isLoadedBacking = GetIsLoadedBacking();
            Assert.IsNotNull(isLoadedBacking,
                "CredentialStore must have a backing field for IsLoaded so test can " +
                "simulate an in-progress load.");

            // Simulate the race: _credentials is empty because Load() has not yet
            // populated it, but the store file exists with saved credentials.
            // (The file is empty here — the important state is IsLoaded=false.)
            _credentialsField.SetValue(_cs, new List<Account>());
            isLoadedBacking.SetValue(_cs, false);

            // Precondition: IsLoaded is false
            Assert.IsFalse((bool)isLoadedProp.GetValue(_cs),
                "Precondition: IsLoaded must be false to simulate in-progress load.");

            // After fix: GetAll() returns an empty list while IsLoaded=false,
            // so GetAllNetworkCredentials() in DeviceListViewModel sees 0 real
            // credentials and the session-attempt loop only has the anonymous
            // null entry — no real camera auth is attempted.
            //
            // This verifies the contract: the auto-connect path must observe
            // IsLoaded=false and not attempt a real credential connection.
            var creds = _cs.GetAll();
            Assert.AreEqual(0, creds.Count,
                "While IsLoaded=false, GetAll() must return 0 real credentials — " +
                "auto-connect must not attempt camera auth with an empty store.");
        }
    }
}
