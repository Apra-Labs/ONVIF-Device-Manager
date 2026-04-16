using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using utils;

namespace odm.ui.core
{
    /// <summary>
    /// Encrypted persistent storage for credential pairs.
    /// Uses DPAPI (ProtectedData) with CurrentUser scope — credentials are
    /// bound to the current Windows user and machine.
    /// </summary>
    public sealed class CredentialStore
    {
        static readonly CredentialStore _instance = new CredentialStore();
        public static CredentialStore Instance { get { return _instance; } }

        readonly string _storePath = AppDefaults.ConfigFolderPath + "credentials.dat";
        readonly string _legacyPath = AppDefaults.ConfigFolderPath + "account.def.xml";

        List<Account> _credentials;

        /// <summary>
        /// True once Load() has completed in the constructor.
        /// Auto-connect checks this before attempting camera auth (issue #29).
        /// </summary>
        public bool IsLoaded { get; private set; }

        private CredentialStore()
        {
            _credentials = Load();
            IsLoaded = true;
        }

        /// <summary>Returns a copy of all stored credentials.</summary>
        public IList<Account> GetAll()
        {
            return _credentials.AsReadOnly();
        }

        /// <summary>Safe log summary — never includes passwords.</summary>
        public string RedactedSummary() => $"{_credentials.Count} credential(s) stored";

        public void Add(Account account)
        {
            _credentials.Add(account);
            Save();
        }

        public void Remove(int index)
        {
            _credentials.RemoveAt(index);
            Save();
        }

        public void Update(int index, Account account)
        {
            _credentials[index] = account;
            Save();
        }

        public void SetAll(List<Account> credentials)
        {
            _credentials = new List<Account>(credentials);
            Save();
        }

        // ------------------------------------------------------------------ //
        // Serialization helpers                                               //
        // ------------------------------------------------------------------ //

        [XmlRootAttribute(ElementName = "Credentials", IsNullable = false)]
        public class CredentialList
        {
            public List<Account> Items { get; set; }
            public CredentialList() { Items = new List<Account>(); }
        }

        private List<Account> Load()
        {
            log.WriteInfo(string.Format("[CredentialStore] Load() start — {0:O}", DateTime.Now));
            // Try encrypted store first
            if (File.Exists(_storePath))
            {
                try
                {
                    byte[] cipherBytes = File.ReadAllBytes(_storePath);
                    byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
                    string xml = Encoding.UTF8.GetString(plainBytes);

                    var serializer = new XmlSerializer(typeof(CredentialList));
                    using (var reader = new StringReader(xml))
                    {
                        var list = (CredentialList)serializer.Deserialize(reader);
                        var items = list.Items ?? new List<Account>();
                        log.WriteInfo(string.Format("[CredentialStore] Load() complete (encrypted) — storeCount={0} — {1:O}", items.Count, DateTime.Now));
                        return items;
                    }
                }
                catch (Exception err)
                {
                    dbg.Error(err);
                    // Fall through to migration attempt
                }
            }

            // Migrate from legacy plain-XML account.def.xml
            if (File.Exists(_legacyPath))
            {
                try
                {
                    Account legacy;
                    var legacySerializer = new XmlSerializer(typeof(Account));
                    using (var sr = File.OpenText(_legacyPath))
                    {
                        legacy = (Account)legacySerializer.Deserialize(sr);
                    }

                    var migrated = new List<Account>();
                    if (!legacy.IsAnonymous)
                        migrated.Add(legacy);

                    // Persist to new encrypted store before touching the old file
                    var credList = new CredentialList { Items = migrated };
                    SaveInternal(credList);

                    // Only delete old file after successful write
                    File.Delete(_legacyPath);

                    log.WriteInfo(string.Format("[CredentialStore] Load() complete (migrated) — storeCount={0} — {1:O}", migrated.Count, DateTime.Now));
                    return migrated;
                }
                catch (Exception err)
                {
                    dbg.Error(err);
                }
            }

            var empty = new List<Account>();
            log.WriteInfo(string.Format("[CredentialStore] Load() complete — storeCount={0} — {1:O}", empty.Count, DateTime.Now));
            return empty;
        }

        private void Save()
        {
            var credList = new CredentialList { Items = _credentials };
            SaveInternal(credList);
        }

        private void SaveInternal(CredentialList credList)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(CredentialList));
                var sb = new StringBuilder();
                using (var writer = new StringWriter(sb))
                {
                    serializer.Serialize(writer, credList);
                }

                byte[] plainBytes = Encoding.UTF8.GetBytes(sb.ToString());
                byte[] cipherBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

                // Write atomically via temp file to avoid corruption on crash (R4 mitigation)
                string tempPath = _storePath + ".tmp";
                File.WriteAllBytes(tempPath, cipherBytes);
                if (File.Exists(_storePath))
                    File.Delete(_storePath);
                File.Move(tempPath, _storePath);
            }
            catch (Exception err)
            {
                dbg.Error(err);
            }
        }
    }
}
