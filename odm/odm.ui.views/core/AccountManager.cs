using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.IO;
using utils;
using System.Xml.Serialization;

namespace odm.ui.core
{
    [XmlRootAttribute(ElementName = "Account", IsNullable = false)]
    public struct Account
    {
        string _password;
        public string Password { get { return _password ?? string.Empty; } set { _password = value; } }
        string _name;
        public string Name { get { return _name ?? string.Empty; } set { _name = value; } }

        public static readonly Account Anonymous = new Account() { Name=string.Empty, Password = string.Empty };
        public bool IsAnonymous { get { return Anonymous.Equals(this); } }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;

            Account another = (Account)obj;
            return this.Name == another.Name && this.Password == another.Password;
        }

        public static bool operator == (Account that, Account another)
        {
            return that.Equals(another);
        }
        public static bool operator !=(Account that, Account another)
        {
            return !(that == another);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (this.Name.GetHashCode() * 397) ^ this.Password.GetHashCode();
            }
        }
    }




    public sealed class AccountManager
    {

        static readonly AccountManager _instance = new AccountManager();
        public static AccountManager Instance { get { return _instance; } }

        private AccountManager()
        {
            // Load CurrentAccount from CredentialStore (first credential) or Anonymous
            var all = CredentialStore.Instance.GetAll();
            _currentAccount = all.Count > 0 ? all[0] : Account.Anonymous;
        }

        /// <summary>
        /// True after an explicit logout. Cleared whenever a real (non-anonymous) account
        /// is set. Used by DeviceListViewModel to suppress the stored-credential fallback.
        /// </summary>
        public bool LoggedOutExplicitly { get; set; } = false;

        public event EventHandler CurrentAccountChanged;
        Account _currentAccount = Account.Anonymous;
        public Account CurrentAccount
        {
            get { return _currentAccount; }
            private set
            {
                if (_currentAccount == value)
                    return;
                _currentAccount = value;

                if (this.CurrentAccountChanged != null)
                    this.CurrentAccountChanged(this, EventArgs.Empty);
            }
        }

        public bool Autorized
        {
            get { return Account.Anonymous != this.CurrentAccount; }
        }

        /// <summary>
        /// Returns all stored credentials from CredentialStore.
        /// </summary>
        public IList<Account> GetAllCredentials()
        {
            return CredentialStore.Instance.GetAll();
        }

        /// <summary>
        /// Replaces the entire credential list in CredentialStore.
        /// </summary>
        public void SetCredentials(List<Account> credentials)
        {
            CredentialStore.Instance.SetAll(credentials);
        }

        /// <summary>
        /// Sets the current active credential and optionally persists it.
        /// When remember=true the credential is stored in the encrypted store
        /// (added if not already present); when false the store is unchanged.
        /// </summary>
        public void SetCurrentAccount(Account account, bool remember)
        {
            if (!account.IsAnonymous)
                LoggedOutExplicitly = false;
            this.CurrentAccount = account;
            if (remember && !account.IsAnonymous)
            {
                var all = CredentialStore.Instance.GetAll();
                // Only skip if exact (name, password) pair already stored.
                // Same name with different password → add as new entry (BUG-1 fix).
                bool exactMatch = false;
                for (int i = 0; i < all.Count; i++)
                {
                    if (string.Equals(all[i].Name, account.Name, StringComparison.OrdinalIgnoreCase)
                        && all[i].Password == account.Password)
                    {
                        exactMatch = true;
                        break;
                    }
                }
                if (!exactMatch)
                    CredentialStore.Instance.Add(account);
            }
        }
    }
}
