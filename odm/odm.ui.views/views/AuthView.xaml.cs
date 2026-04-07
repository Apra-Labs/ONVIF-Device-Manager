using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using utils;
using odm.ui.core;
using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Unity;
using Microsoft.Practices.Prism.Events;
using odm.ui.controls;

namespace odm.ui.views
{
    /// <summary>
    /// Interaction logic for AuthView.xaml
    /// </summary>
    public partial class AuthView : UserControl
    {

        IEventAggregator eventAggregator;
        DelegateCommand _loginCommand;

        public AuthView(IUnityContainer container)
        {
            eventAggregator = container.Resolve<IEventAggregator>();

            InitializeComponent();

            Init();
        }

        #region Dependency Properties

        

        public bool Authorized
        {
            get { return (bool)GetValue(AuthorizedProperty); }
            set { SetValue(AuthorizedProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Authorized.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AuthorizedProperty =
            DependencyProperty.Register("Authorized", typeof(bool), typeof(AuthView), new PropertyMetadata(false, (s,e) => 
                {
                    var auth = (AuthView)s;
                    if (true.Equals(e.NewValue))
                    {
                        auth.panelEdit.Visibility = Visibility.Collapsed;
                        auth.panelView.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        auth.panelEdit.Visibility = Visibility.Visible;
                        auth.panelView.Visibility = Visibility.Collapsed;
                    }
                }));


        #endregion Dependency Properties

        bool CanLogin()
        {
            bool hasFields = !string.IsNullOrEmpty(username.Text)
                          && !string.IsNullOrEmpty(password.Password);
            bool hasStored = AccountManager.Instance.GetAllCredentials().Count > 0;
            return hasFields || hasStored;
        }

        void Init()
        {
            _loginCommand = new DelegateCommand(btLogin_Click);
            btLogin.Command = _loginCommand;
            btLogout.Command = new DelegateCommand(new Action(btLogout_Click));
            lnkManageCredentials.Click += BtManageCredentials_Click;

            username.KeyDown += (s, e) => { if (e.Key == Key.Enter) btLogin_Click(); };
            password.KeyDown += (s, e) => { if (e.Key == Key.Enter) btLogin_Click(); };
            this.Loaded += AuthView_Loaded;

            AccountManager.Instance.CurrentAccountChanged += delegate { Update(); };
            AuthLog("AuthView.Init: startup — version timestamp " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
        }

        void Update()
        {
            Authorized = AccountManager.Instance.Autorized;
            var account = AccountManager.Instance.CurrentAccount;
            username.Text = account.Name;
            password.Password = account.Password ?? string.Empty;
            loggedUsername.Text = account.Name;
        }

        void AuthView_Loaded(object sender, RoutedEventArgs e)
        {
            Update();
            if (AccountManager.Instance.GetAllCredentials().Count > 0
                && !AccountManager.Instance.LoggedOutExplicitly)
            {
                eventAggregator.GetEvent<Refresh>().Publish(true);
            }
        }

        void BtManageCredentials_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new CredentialManagerView(eventAggregator);
                win.Owner = Window.GetWindow(this);
                win.ShowDialog();
            }
            catch (Exception err)
            {
                dbg.Error(err);
            }
        }

        static string Safe(Account a) => $"name={a.Name}, pwd=[REDACTED]";

        static void AuthLog(string msg)
        {
            try
            {
                string logPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "logs", "auth.log");
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n";
                System.IO.File.AppendAllText(logPath, line);
            }
            catch { }
        }

        void btLogin_Click()
        {
            try
            {
                var name = username.Text;
                var pwd  = password.Password;
                int storeCount = AccountManager.Instance.GetAllCredentials().Count;

                AuthLog("btLogin_Click: name=" + name + " storeCount=" + storeCount);

                switch (LoginActionHelper.Determine(name, pwd, storeCount))
                {
                    case LoginAction.Case1SetAndRefresh:
                        // Case 1: explicit credentials entered — save only if checkbox is checked.
                        AccountManager.Instance.SetCurrentAccount(
                            new Account { Name = name, Password = pwd },
                            remember: remember.IsChecked == true);

                        _loginCommand.RaiseCanExecuteChanged();

                        eventAggregator.GetEvent<Refresh>().Publish(true);
                        break;

                    case LoginAction.Case2RefreshWithStored:
                        // Case 2: no fields entered but store has entries — use first stored credential.
                        var stored = AccountManager.Instance.GetAllCredentials();
                        AccountManager.Instance.SetCurrentAccount(stored[0], remember: false);
                        Update(); // force panel update even if CurrentAccount didn't change
                        AuthLog("btLogin_Click Case2: " + Safe(stored[0]) + " Autorized=" + AccountManager.Instance.Autorized);
                        eventAggregator.GetEvent<Refresh>().Publish(true);
                        break;

                    default: // Case3Block
                        // Case 3: no fields and no stored credentials — block.
                        MessageBox.Show(
                            "No credentials available. Enter a username and password, or add entries via Manage Credentials.",
                            "Credentials Required",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        break;
                }
            }
            catch (Exception err)
            {
                dbg.Error(err);
            }
        }

        void btLogout_Click()
        {
            try
            {
                AccountManager.Instance.LoggedOutExplicitly = true;
                AccountManager.Instance.SetCurrentAccount(Account.Anonymous, remember: false);
                eventAggregator.GetEvent<Refresh>().Publish(true);
            }
            catch (Exception err)
            {
                dbg.Error(err);
            }
        }
    }
}
