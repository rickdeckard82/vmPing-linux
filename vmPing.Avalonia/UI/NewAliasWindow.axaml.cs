using Avalonia.Controls;
using Avalonia.Interactivity;
using vmPing.Classes;
using vmPing.Properties;

namespace vmPing.UI
{
    public partial class NewAliasWindow : Window
    {
        private readonly TextBox? _hostname;
        private readonly TextBox? _newAlias;

        public NewAliasWindow()
        {
            InitializeComponent();

            _hostname = this.FindControl<TextBox>("Hostname");
            _newAlias = this.FindControl<TextBox>("NewAlias");
        }

        private async void Save_Click(object? sender, RoutedEventArgs e)
        {
            var hostname = _hostname?.Text ?? string.Empty;
            var alias = _newAlias?.Text ?? string.Empty;

            // Validate hostname.
            if (Alias.IsHostInvalid(hostname))
            {
                await ShowError(Strings.NewAlias_Error_InvalidHost);
                _hostname?.Focus();
                _hostname?.SelectAll();
                return;
            }

            // Validate alias name.
            if (Alias.IsNameInvalid(alias))
            {
                await ShowError(Strings.NewAlias_Error_InvalidAlias);
                _newAlias?.Focus();
                _newAlias?.SelectAll();
                return;
            }

            // Validation passed. Add alias.
            Alias.Add(hostname.Trim(), alias);
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private async System.Threading.Tasks.Task ShowError(string message)
        {
            await DialogWindow.ErrorWindow(message).ShowDialog(this);
        }
    }
}
