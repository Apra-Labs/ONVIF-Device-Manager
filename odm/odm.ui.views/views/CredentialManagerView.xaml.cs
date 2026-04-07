using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Practices.Prism.Events;
using odm.ui.controls;
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
    /// Supports add (+ Add button), edit, remove (× column or Delete key), and reorder.
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

        void BtAdd_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new CredentialItem();
            _items.Add(newItem);
            credGrid.SelectedItem = newItem;
            credGrid.ScrollIntoView(newItem);
            credGrid.CurrentCell = new DataGridCellInfo(newItem, credGrid.Columns[0]);
            credGrid.BeginEdit();
        }

        void BtDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var item = btn.DataContext as CredentialItem;
            if (item == null) return;
            _items.Remove(item);
            SaveAndRefresh();
        }

        void CredGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                var item = credGrid.SelectedItem as CredentialItem;
                if (item != null)
                {
                    _items.Remove(item);
                    SaveAndRefresh();
                    e.Handled = true;
                }
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

        void CredGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Tab) return;

            var cell = credGrid.CurrentCell;
            if (!cell.IsValid) return;

            int colIdx = credGrid.Columns.IndexOf(cell.Column);
            int rowIdx = _items.IndexOf(cell.Item as CredentialItem);
            if (rowIdx < 0) return;

            e.Handled = true;

            if (colIdx == 0)
            {
                // Username → Password: commit, begin edit on password column, select all
                credGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                credGrid.CurrentCell = new DataGridCellInfo(_items[rowIdx], credGrid.Columns[1]);
                credGrid.SelectedItem = _items[rowIdx];
                credGrid.BeginEdit();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var tpb = GetTogglePasswordBoxInCurrentCell();
                    if (tpb != null) tpb.FocusPasswordInput();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            else if (colIdx == 1)
            {
                // Password → Delete button of same row
                credGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                credGrid.CurrentCell = new DataGridCellInfo(_items[rowIdx], credGrid.Columns[2]);
                credGrid.SelectedItem = _items[rowIdx];
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var btn = GetButtonInCurrentCell();
                    btn?.Focus();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            else if (colIdx == 2)
            {
                // Delete → Username of next row, or btAdd if no more rows
                int nextRow = rowIdx + 1;
                if (nextRow < _items.Count)
                {
                    credGrid.CurrentCell = new DataGridCellInfo(_items[nextRow], credGrid.Columns[0]);
                    credGrid.SelectedItem = _items[nextRow];
                    credGrid.BeginEdit();
                }
                else
                {
                    btAdd.Focus();
                }
            }
        }

        TogglePasswordBox GetTogglePasswordBoxInCurrentCell()
        {
            var cell = GetCurrentDataGridCell();
            return cell == null ? null : FindVisualChild<TogglePasswordBox>(cell);
        }

        Button GetButtonInCurrentCell()
        {
            var cell = GetCurrentDataGridCell();
            return cell == null ? null : FindVisualChild<Button>(cell);
        }

        DataGridCell GetCurrentDataGridCell()
        {
            if (!credGrid.CurrentCell.IsValid) return null;
            var col = credGrid.CurrentCell.Column;
            var row = credGrid.ItemContainerGenerator.ContainerFromItem(credGrid.CurrentCell.Item) as DataGridRow;
            if (row == null) return null;
            var presenter = FindVisualChild<DataGridCellsPresenter>(row);
            if (presenter == null) return null;
            return presenter.ItemContainerGenerator.ContainerFromIndex(col.DisplayIndex) as DataGridCell;
        }

        static T FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            var storeCount = CredentialStore.Instance.GetAll().Count;
            AccountManager.Instance.LoggedOutExplicitly = (storeCount == 0);
            AccountManager.Instance.SetCurrentAccount(Account.Anonymous, remember: false);
            _eventAggregator.GetEvent<Refresh>().Publish(true);
        }
    }
}
