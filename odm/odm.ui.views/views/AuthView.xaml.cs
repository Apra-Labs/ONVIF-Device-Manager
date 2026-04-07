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
            btManageCredentials.Click += BtManageCredentials_Click;

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
                bool hasFields = !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(pwd);

                AuthLog("btLogin_Click: hasFields=" + hasFields + " storeCount=" + AccountManager.Instance.GetAllCredentials().Count);

                if (hasFields)
                {
                    // Case 1: explicit credentials entered — always save and connect.
                    AccountManager.Instance.SetCurrentAccount(
                        new Account { Name = name, Password = pwd }, remember: true);

                    _loginCommand.RaiseCanExecuteChanged();

                    eventAggregator.GetEvent<Refresh>().Publish(true);
                }
                else if (AccountManager.Instance.GetAllCredentials().Count > 0)
                {
                    // Case 2: no fields entered but store has entries — use first stored credential.
                    var stored = AccountManager.Instance.GetAllCredentials();
                    AccountManager.Instance.SetCurrentAccount(stored[0], remember: false);
                    eventAggregator.GetEvent<Refresh>().Publish(true);
                }
                else
                {
                    // Case 3: no fields and no stored credentials — block.
                    MessageBox.Show(
                        "Please enter a username and password.",
                        "Credentials Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
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
                var last = AccountManager.Instance.CurrentAccount;
                AccountManager.Instance.SetCurrentAccount(Account.Anonymous, true);
                //username.Text = last.Name;
                //password.Password = last.Password;

                eventAggregator.GetEvent<Refresh>().Publish(true);
            }
            catch (Exception err)
            {
                dbg.Error(err);
            }
        }
    }
}
