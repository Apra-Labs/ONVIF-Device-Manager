using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Practices.Prism.Events;
using odm.ui.core;
using utils;

namespace odm.ui.views
{
    /// <summary>
    /// Wrapper for a credential pair that supports DataGrid inline editing.
    /// </summary>
    public class CredentialItem : INotifyPropertyChanged
    {
        string _name;
        string _password;

        public string Name
        {
            get { return _name ?? string.Empty; }
            set { _name = value; OnPropertyChanged("Name"); }
        }

        public string Password
        {
            get { return _password ?? string.Empty; }
            set { _password = value; OnPropertyChanged("Password"); }
        }

        public Account ToAccount()
        {
            return new Account { Name = Name, Password = Password };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged(string propertyName)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Child window for managing stored credential pairs.
    /// Supports add (inline DataGrid row), edit, remove, and reorder.
    /// All changes are saved immediately to CredentialStore and a Refresh is published.
    /// </summary>
    public partial class CredentialManagerView : Window
    {
        readonly IEventAggregator _eventAggregator;
        ObservableCollection<CredentialItem> _items;

        public CredentialManagerView(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            InitializeComponent();
            LoadCredentials();

            credGrid.RowEditEnding += CredGrid_RowEditEnding;
            btMoveUp.Click   += BtMoveUp_Click;
            btMoveDown.Click += BtMoveDown_Click;
            btRemove.Click   += BtRemove_Click;
            btClose.Click    += (s, e) => Close();
        }

        void LoadCredentials()
        {
            _items = new ObservableCollection<CredentialItem>();
            foreach (var account in CredentialStore.Instance.GetAll())
            {
                _items.Add(new CredentialItem { Name = account.Name, Password = account.Password });
            }
            credGrid.ItemsSource = _items;
        }

        void SaveAndRefresh()
        {
            var list = new List<Account>();
            foreach (var item in _items)
            {
                if (!string.IsNullOrEmpty(item.Name))
                    list.Add(item.ToAccount());
            }
            AccountManager.Instance.SetCredentials(list);
            _eventAggregator.GetEvent<Refresh>().Publish(true);
        }

        void CredGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // Defer until after the DataGrid has committed the edit to the binding source.
                Dispatcher.BeginInvoke(
                    new Action(SaveAndRefresh),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        void BtMoveUp_Click(object sender, RoutedEventArgs e)
        {
            int idx = credGrid.SelectedIndex;
            if (idx <= 0 || idx >= _items.Count) return;
            _items.Move(idx, idx - 1);
            credGrid.SelectedIndex = idx - 1;
            SaveAndRefresh();
        }

        void BtMoveDown_Click(object sender, RoutedEventArgs e)
        {
            int idx = credGrid.SelectedIndex;
            if (idx < 0 || idx >= _items.Count - 1) return;
            _items.Move(idx, idx + 1);
            credGrid.SelectedIndex = idx + 1;
            SaveAndRefresh();
        }

        void BtRemove_Click(object sender, RoutedEventArgs e)
        {
            var item = credGrid.SelectedItem as CredentialItem;
            if (item == null) return;

            var result = MessageBox.Show(
                string.Format("Remove credential for '{0}'?", item.Name),
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _items.Remove(item);
                SaveAndRefresh();
            }
        }
    }
}
