using System.Windows;
using System.Windows.Controls;

namespace odm.ui.controls
{
    /// <summary>
    /// Reusable password field with a Show/Hide toggle button.
    /// Exposes a <see cref="Password"/> dependency property that external code can bind to
    /// or read/write directly, just like PasswordBox.Password.
    /// </summary>
    public partial class TogglePasswordBox : UserControl
    {
        bool _updating;

        public TogglePasswordBox()
        {
            InitializeComponent();

            passwordBox.PasswordChanged += (s, e) =>
            {
                if (_updating) return;
                _updating = true;
                Password = passwordBox.Password;
                textBox.Text = passwordBox.Password;
                _updating = false;
            };

            textBox.TextChanged += (s, e) =>
            {
                if (_updating) return;
                _updating = true;
                Password = textBox.Text;
                passwordBox.Password = textBox.Text;
                _updating = false;
            };

            toggleBtn.Checked += (s, e) =>
            {
                textBox.Text = passwordBox.Password;
                passwordBox.Visibility = Visibility.Collapsed;
                textBox.Visibility = Visibility.Visible;
                textBox.CaretIndex = textBox.Text.Length;
                textBox.Focus();
            };

            toggleBtn.Unchecked += (s, e) =>
            {
                passwordBox.Password = textBox.Text;
                textBox.Visibility = Visibility.Collapsed;
                passwordBox.Visibility = Visibility.Visible;
                passwordBox.Focus();
            };
        }

        // ------------------------------------------------------------------
        // Password dependency property
        // ------------------------------------------------------------------

        public static readonly DependencyProperty PasswordProperty =
            DependencyProperty.Register(
                "Password",
                typeof(string),
                typeof(TogglePasswordBox),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnPasswordPropertyChanged));

        public string Password
        {
            get { return (string)GetValue(PasswordProperty); }
            set { SetValue(PasswordProperty, value); }
        }

        static void OnPasswordPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (TogglePasswordBox)d;
            if (ctrl._updating) return;
            ctrl._updating = true;
            string val = (e.NewValue as string) ?? string.Empty;
            ctrl.passwordBox.Password = val;
            ctrl.textBox.Text = val;
            ctrl._updating = false;
        }
    }
}
