using Avalonia.Controls;
using Avalonia.Interactivity;
using vmPing.Properties;

namespace vmPing.UI
{
    // [Certo] Substitui a dependência em MsBox.Avalonia (removida do .csproj).
    // Motivo: o único release publicado no NuGet, 3.0.0-rc2, tem uma API que não
    // bate com a documentada no branch master do projeto no GitHub (o build real
    // do usuário deu CS0103 em MsBox.Avalonia.Enums.ButtonEnum) — sinal de que é
    // um pacote pré-release com superfície instável. Em vez de continuar
    // adivinhando a API de um RC de terceiros, implementei aqui uma versão
    // enxuta do UI/DialogWindow.xaml original: mesmo contrato público
    // (ErrorWindow/WarningWindow), sem dependência externa. Bônus: isso também
    // resolve um dos débitos da Fase 2 (CommandLine.cs e Probe-Util.cs tinham
    // TODOs esperando por este tipo).
    public partial class DialogWindow : Window
    {
        public enum DialogIcon
        {
            Warning,
            Error,
            Info,
            None
        }

        public DialogWindow()
        {
            InitializeComponent();
        }

        public DialogWindow(DialogIcon icon, string title, string body, string confirmationText, bool isCancelButtonVisible)
            : this()
        {
            var header = this.FindControl<TextBlock>("MessageHeader");
            var bodyText = this.FindControl<TextBlock>("MessageBody");
            var okButton = this.FindControl<Button>("OK");
            var cancelButton = this.FindControl<Button>("Cancel");

            if (header != null) header.Text = title;
            if (bodyText != null) bodyText.Text = body;
            if (okButton != null) okButton.Content = confirmationText;
            if (cancelButton != null) cancelButton.IsVisible = isCancelButtonVisible;

            // TODO Fase 4/5: reintroduzir o ícone vetorial (icon.exclamation-circle
            // etc.) quando ResourceDictionaries/Icons.xaml for portado.
        }

        public static DialogWindow ErrorWindow(string message)
        {
            return new DialogWindow(
                icon: DialogIcon.Error,
                title: Strings.DialogTitle_Error,
                body: message,
                confirmationText: Strings.DialogButton_OK,
                isCancelButtonVisible: false);
        }

        // [Certo] FASE 4 — factory nova, usada por StatusHistoryWindow.Export_Click
        // (confirmação de sucesso, sem botão Cancelar). O enum DialogIcon.Info já
        // existia desde a Fase 3 mas nunca tinha um factory que o usasse.
        public static DialogWindow InfoWindow(string title, string message)
        {
            return new DialogWindow(
                icon: DialogIcon.Info,
                title: title,
                body: message,
                confirmationText: Strings.DialogButton_OK,
                isCancelButtonVisible: false);
        }

        public static DialogWindow WarningWindow(string message, string confirmButtonText)
        {
            return new DialogWindow(
                icon: DialogIcon.Warning,
                title: Strings.DialogTitle_Warning,
                body: message,
                confirmationText: confirmButtonText,
                isCancelButtonVisible: true);
        }

        // [Certo] FASE 4 — correção: fechar com Close() (sem argumento) faz
        // ShowDialog<bool>(owner) sempre voltar `false`, porque o valor nunca
        // foi setado (default(bool) = false), tanto pra OK quanto pra Cancel.
        // Isso não dava problema em ErrorWindow (só tem OK, Cancel oculto, e
        // sempre foi chamado via .Show()/.ShowDialog() não-genérico, ignorando
        // o resultado) mas quebraria silenciosamente qualquer uso de
        // WarningWindow com ShowDialog<bool> (ex: confirmação de sobrescrita
        // em NewFavoriteWindow) — os dois botões pareceriam "cancelar".
        private void OK_Click(object sender, RoutedEventArgs e) => Close(true);

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close(false);
    }
}
