using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class HelpWindow : Window
    {
        public static HelpWindow? _OpenWindow = null;

        public HelpWindow()
        {
            InitializeComponent();

            Opened += (_, _) => _OpenWindow = this;
            Closed += (_, _) => _OpenWindow = null;

            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            var version = typeof(MainWindow).Assembly.GetName().Version;
            var versionText = this.FindControl<TextBlock>("Version");
            if (versionText != null && version != null)
            {
                versionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
            }
        }

        // [Certo] No Linux, abrir uma URL exige UseShellExecute=true (aciona
        // xdg-open por baixo); sem isso o .NET tenta executar a URL como se
        // fosse um binário e falha com Win32Exception. Comportamento de
        // plataforma, não específico do Avalonia.
        private const string UpstreamUrl = "https://github.com/R-Smith/vmPing";
        private const string ForkUrl = "https://github.com/rickdeckard82/vmPing-linux";

        private void Hyperlink_PointerPressed(object? sender, PointerPressedEventArgs e)
            => OpenUrl(UpstreamUrl);

        private void ForkLink_PointerPressed(object? sender, PointerPressedEventArgs e)
            => OpenUrl(ForkUrl);

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Sem navegador configurado / xdg-open ausente: falha
                // silenciosamente, igual ao comportamento tolerante do resto
                // do port para ações não essenciais.
            }
        }

        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Constants.HelpKeyBinding)
            {
                e.Handled = true;
                Close();
            }
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _OpenWindow = null;
        }
    }
}
