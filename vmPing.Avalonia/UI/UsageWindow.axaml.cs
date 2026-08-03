using Avalonia.Controls;
using Avalonia.Interactivity;

namespace vmPing.UI
{
    public partial class UsageWindow : Window
    {
        public UsageWindow()
        {
            InitializeComponent();

            var version = typeof(MainWindow).Assembly.GetName().Version;
            var appVersion = this.FindControl<TextBlock>("AppVersion");
            if (appVersion != null && version != null)
            {
                appVersion.Text = $"{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        private void OK_Click(object? sender, RoutedEventArgs e) => Close();
    }
}
