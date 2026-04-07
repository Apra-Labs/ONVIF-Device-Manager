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

        void Init()
        {
            btLogin.Command = new DelegateCommand(new Action(btLogin_Click));
            btLogout.Command = new DelegateCommand(new Action(btLogout_Click));
            btManageCredentials.Click += BtManageCredentials_Click;
            username.KeyDown += (s, e) => { if (e.Key == Key.Enter) btLogin_Click(); };
            password.KeyDown += (s, e) => { if (e.Key == Key.Enter) btLogin_Click(); };
            this.Loaded += AuthView_Loaded;

            AccountManager.Instance.CurrentAccountChanged += delegate { Update(); };
        }

        void Update()
        {
            Authorized = AccountManager.Instance.Autorized;
            var account = AccountManager.Instance.CurrentAccount;
            username.Text = account.Name;
            password.Password = account.Password;
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

        void btLogin_Click()
        {
            try
            {
                var name = username.Text;
                var pwd  = password.Password;
                var doRemember = remember.IsChecked == true;

                // When remembering, check for an existing entry with the same username
                // (case-insensitive). If found and password differs, ask before overwriting.
                if (doRemember && !string.IsNullOrEmpty(name))
                {
                    var all = CredentialStore.Instance.GetAll();
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (string.Equals(all[i].Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (all[i].Password != pwd)
                            {
                                var result = MessageBox.Show(
                                    string.Format("A credential for '{0}' already exists. Update the stored password?", name),
                                    "Update Credential",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question);
                                if (result == MessageBoxResult.No)
                                {
                                    // Set as current account without persisting the new password.
                                    AccountManager.Instance.SetCurrentAccount(
                                        new Account { Name = name, Password = pwd }, false);
                                    eventAggregator.GetEvent<Refresh>().Publish(true);
                                    return;
                                }
                            }
                            break;
                        }
                    }
                }

                AccountManager.Instance.SetCurrentAccount(
                    new Account { Name = name, Password = pwd }, doRemember);
                eventAggregator.GetEvent<Refresh>().Publish(true);
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
