using Avalonia.Controls;
using Avalonia.Interactivity;
using vmPing.Classes;

namespace vmPing.UI
{
    // STUB DA FASE 3 — UI mínima, Save já é real.
    public partial class EditAliasWindow : Window
    {
        private readonly string _hostname;

        public EditAliasWindow() : this(string.Empty, string.Empty) { }

        public EditAliasWindow(Probe pingItem) : this(pingItem.Hostname, pingItem.Alias) { }

        public EditAliasWindow(string hostname, string alias)
        {
            InitializeComponent();
            _hostname = hostname;

            var hostLabel = this.FindControl<TextBlock>("HostnameLabel");
            var aliasBox = this.FindControl<TextBox>("AliasBox");
            if (hostLabel != null) hostLabel.Text = hostname;
            if (aliasBox != null) aliasBox.Text = alias;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var aliasBox = this.FindControl<TextBox>("AliasBox");
            var alias = aliasBox?.Text?.Trim();

            if (string.IsNullOrEmpty(_hostname))
            {
                Close(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(alias))
            {
                Alias.Delete(_hostname);
            }
            else
            {
                Alias.Add(_hostname, alias);
            }

            Close(true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close(false);
    }
}
