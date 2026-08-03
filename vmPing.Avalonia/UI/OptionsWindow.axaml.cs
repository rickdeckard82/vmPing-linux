using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using vmPing.Classes;
using vmPing.Properties;

namespace vmPing.UI
{
    public partial class OptionsWindow : Window
    {
        // [Certo] FASE 4 — guarda pra impedir que handlers de evento
        // (Checked/SelectionChanged) rodem durante a construção da árvore
        // XAML (InitializeComponent), antes dos campos gerados de controles
        // "irmãos" mais abaixo no arquivo estarem garantidamente atribuídos.
        // O original usava `if (IsLoaded)` (propriedade real do WPF); o
        // Avalonia não tem uma `IsLoaded` equivalente confirmada, então troquei
        // por um campo simples setado no fim do construtor — mesmo efeito,
        // zero dependência de API não verificada.
        private bool _isReady;

        public OptionsWindow()
        {
            InitializeComponent();

            PopulateGeneralOptions();
            PopulateNotificationOptions();
            PopulateEmailAlertOptions();
            PopulateAudioAlertOptions();
            PopulateLogOutputOptions();
            PopulateAdvancedOptions();
            PopulateDisplayOptions();
            PopulateLayoutOptions();

            _isReady = true;
        }

        // [Certo] GetComboText removido na rodada de i18n: era usado só pra
        // decidir o multiplicador do intervalo comparando o texto do combo,
        // o que quebra assim que o texto é traduzido. Quem precisar do valor
        // de um combo deve usar SelectedIndex, não o rótulo exibido.

        private async Task ShowError(string message, TabItem? tabItem, Control? control, bool isWarning = false)
        {
            if (tabItem != null)
            {
                tabItem.IsSelected = true;
            }

            var errorWindow = isWarning
                ? DialogWindow.WarningWindow(message, Strings.DialogButton_Save)
                : DialogWindow.ErrorWindow(message);

            await errorWindow.ShowDialog(this);
            control?.Focus();
        }

        // Histórico dos botões "Browse..." (3 rodadas, cada uma resolvendo o
        // problema deixado pela anterior — mantido aqui como memória):
        //   1. Sem try/catch: a DBusException do xdg-desktop-portal derrubava o
        //      APP INTEIRO ao clicar em Browse (exceção não tratada em handler
        //      assíncrono sobe até o loop de dispatch do Avalonia e é fatal).
        //   2. Com try/catch + mensagem de erro: não crashava mais, mas o
        //      usuário ficava sem seletor — só digitar o caminho à mão.
        //   3. Atual (PickFolderAsync/PickFileAsync): tenta o nativo e, se ele
        //      falhar, abre o seletor próprio (UI/FileBrowserWindow). O bug do
        //      portal é upstream e sem correção (issues 1653/1756 do
        //      flatpak/xdg-desktop-portal), mas isso não obriga o app a ficar
        //      sem a funcionalidade.
        // ShowFolderPickerError/GetFallbackDirectory foram removidos junto: o
        // fallback deixou de ser "mostrar erro e preencher um caminho padrão"
        // e passou a ser um seletor que funciona.

        private void PopulateGeneralOptions()
        {
            string pingIntervalUnits;
            int pingIntervalDivisor;
            int pingInterval = ApplicationOptions.PingInterval;
            int pingTimeout = ApplicationOptions.PingTimeout;

            if (ApplicationOptions.PingInterval >= 3600000 && ApplicationOptions.PingInterval % 3600000 == 0)
            {
                pingIntervalUnits = "hours";
                pingIntervalDivisor = 3600000;
            }
            else if (ApplicationOptions.PingInterval >= 60000 && ApplicationOptions.PingInterval % 60000 == 0)
            {
                pingIntervalUnits = "minutes";
                pingIntervalDivisor = 60000;
            }
            else
            {
                pingIntervalUnits = "seconds";
                pingIntervalDivisor = 1000;
            }

            pingInterval /= pingIntervalDivisor;
            pingTimeout /= 1000;

            PingInterval.Text = pingInterval.ToString();
            PingTimeout.Text = pingTimeout.ToString();
            AlertThreshold.Text = ApplicationOptions.AlertThreshold.ToString();
            PingIntervalUnits.SelectedIndex = pingIntervalUnits switch { "minutes" => 1, "hours" => 2, _ => 0 };

            InitialProbeCount.Text = ApplicationOptions.InitialProbeCount.ToString();
            InitialColumnCount.Text = ApplicationOptions.InitialColumnCount.ToString();
            StartupMode.SelectedIndex = (int)ApplicationOptions.InitialStartMode;
            InitialFavorite.ItemsSource = Favorite.GetTitles();
            InitialFavorite.SelectedItem = ApplicationOptions.InitialFavorite;

            var isFavoriteStartup = StartupMode.SelectedIndex == (int)ApplicationOptions.StartMode.Favorite;
            StandardStartupPanel.IsVisible = !isFavoriteStartup;
            FavoriteStartupPanel.IsVisible = isFavoriteStartup;
        }

        private void PopulateNotificationOptions()
        {
            PopupsDisabledOption.IsChecked = false;
            PopupsMinimizedOption.IsChecked = false;
            PopupsAlwaysOption.IsChecked = false;
            switch (ApplicationOptions.PopupOption)
            {
                case ApplicationOptions.PopupNotificationOption.Never:
                    PopupsDisabledOption.IsChecked = true;
                    break;
                case ApplicationOptions.PopupNotificationOption.WhenMinimized:
                    PopupsMinimizedOption.IsChecked = true;
                    break;
                case ApplicationOptions.PopupNotificationOption.Always:
                    PopupsAlwaysOption.IsChecked = true;
                    break;
            }
            IsAutoDismissEnabled.IsChecked = ApplicationOptions.IsAutoDismissEnabled;
            AutoDismissInterval.Text = (ApplicationOptions.AutoDismissMilliseconds / 1000).ToString();
            AutoDismissIntervalPanel.IsVisible = ApplicationOptions.IsAutoDismissEnabled;
        }

        private void PopulateEmailAlertOptions()
        {
            IsEmailAlertsEnabled.IsChecked = ApplicationOptions.IsEmailAlertEnabled;
            IsSmtpAuthenticationRequired.IsChecked = ApplicationOptions.IsEmailAuthenticationRequired;
            IsSmtpSslEnabled.IsChecked = ApplicationOptions.IsEmailSslEnabled;
            SmtpServer.Text = ApplicationOptions.EmailServer;
            SmtpPort.Text = ApplicationOptions.EmailPort;
            SmtpUsername.Text = ApplicationOptions.EmailUser;
            SmtpPassword.Text = ApplicationOptions.EmailPassword;
            EmailRecipientAddress.Text = ApplicationOptions.EmailRecipient;
            EmailFromAddress.Text = ApplicationOptions.EmailFromAddress;

            EmailOptionsPanel.IsVisible = ApplicationOptions.IsEmailAlertEnabled;
            SmtpAuthPanel.IsVisible = ApplicationOptions.IsEmailAuthenticationRequired;
        }

        private void PopulateAudioAlertOptions()
        {
            IsAudioDownAlertEnabled.IsChecked = ApplicationOptions.IsAudioDownAlertEnabled;
            AudioDownFilePath.Text = ApplicationOptions.AudioDownFilePath;
            IsAudioUpAlertEnabled.IsChecked = ApplicationOptions.IsAudioUpAlertEnabled;
            AudioUpFilePath.Text = ApplicationOptions.AudioUpFilePath;

            AudioDownPanel.IsVisible = ApplicationOptions.IsAudioDownAlertEnabled;
            AudioUpPanel.IsVisible = ApplicationOptions.IsAudioUpAlertEnabled;
        }

        private void PopulateLogOutputOptions()
        {
            LogPath.Text = ApplicationOptions.LogPath;
            IsLogOutputEnabled.IsChecked = ApplicationOptions.IsLogOutputEnabled;
            LogStatusChangesPath.Text = ApplicationOptions.LogStatusChangesPath;
            IsLogStatusChangesEnabled.IsChecked = ApplicationOptions.IsLogStatusChangesEnabled;

            LogOutputPanel.IsVisible = ApplicationOptions.IsLogOutputEnabled;
            LogStatusChangesPanel.IsVisible = ApplicationOptions.IsLogStatusChangesEnabled;
        }

        private void PopulateAdvancedOptions()
        {
            TTL.Text = ApplicationOptions.TTL.ToString();
            DontFragment.IsChecked = ApplicationOptions.DontFragment;

            if (ApplicationOptions.UseCustomBuffer)
            {
                UseCustomPacketOption.IsChecked = true;
                PacketData.Text = Encoding.ASCII.GetString(ApplicationOptions.Buffer);
            }
            else
            {
                PacketSizeOption.IsChecked = true;
                PacketSize.Text = ApplicationOptions.Buffer.Length.ToString();
            }

            PacketSize.IsVisible = PacketSizeOption.IsChecked == true;
            PacketData.IsVisible = UseCustomPacketOption.IsChecked == true;

            UpdateByteCount();
        }

        private void PopulateDisplayOptions()
        {
            IsAlwaysOnTopEnabled.IsChecked = ApplicationOptions.IsAlwaysOnTopEnabled;
            IsMinimizeToTrayEnabled.IsChecked = ApplicationOptions.IsMinimizeToTrayEnabled;
            IsExitToTrayEnabled.IsChecked = ApplicationOptions.IsExitToTrayEnabled;

            // i18n (FASE 5): índice fixo -> código de idioma (ver
            // LanguageIndexToCode/LanguageCodeToIndex). Os dois nomes de
            // idioma ficam SEMPRE no próprio idioma nativo ("English" /
            // "Português (Brasil)") — convenção de seletores de idioma:
            // quem não entende o idioma atual precisa achar o seu.
            LanguageCombo.ItemsSource = new[]
            {
                Strings.Options_LanguageAuto,
                "English",
                "Português (Brasil)",
            };
            LanguageCombo.SelectedIndex = LanguageCodeToIndex(ApplicationOptions.Language);
        }

        private static int LanguageCodeToIndex(string code) => code switch
        {
            Localization.English => 1,
            Localization.PortugueseBrazil => 2,
            _ => 0,
        };

        private static string LanguageIndexToCode(int index) => index switch
        {
            1 => Localization.English,
            2 => Localization.PortugueseBrazil,
            _ => Localization.Auto,
        };

        private void PopulateLayoutOptions()
        {
            BackgroundColor_Probe_Inactive.Text = ApplicationOptions.BackgroundColor_Probe_Inactive;
            BackgroundColor_Probe_Up.Text = ApplicationOptions.BackgroundColor_Probe_Up;
            BackgroundColor_Probe_Down.Text = ApplicationOptions.BackgroundColor_Probe_Down;
            BackgroundColor_Probe_Error.Text = ApplicationOptions.BackgroundColor_Probe_Error;
            BackgroundColor_Probe_Indeterminate.Text = ApplicationOptions.BackgroundColor_Probe_Indeterminate;
            ForegroundColor_Probe_Inactive.Text = ApplicationOptions.ForegroundColor_Probe_Inactive;
            ForegroundColor_Probe_Up.Text = ApplicationOptions.ForegroundColor_Probe_Up;
            ForegroundColor_Probe_Down.Text = ApplicationOptions.ForegroundColor_Probe_Down;
            ForegroundColor_Probe_Error.Text = ApplicationOptions.ForegroundColor_Probe_Error;
            ForegroundColor_Probe_Indeterminate.Text = ApplicationOptions.ForegroundColor_Probe_Indeterminate;
            ForegroundColor_Stats_Inactive.Text = ApplicationOptions.ForegroundColor_Stats_Inactive;
            ForegroundColor_Stats_Up.Text = ApplicationOptions.ForegroundColor_Stats_Up;
            ForegroundColor_Stats_Down.Text = ApplicationOptions.ForegroundColor_Stats_Down;
            ForegroundColor_Stats_Error.Text = ApplicationOptions.ForegroundColor_Stats_Error;
            ForegroundColor_Stats_Indeterminate.Text = ApplicationOptions.ForegroundColor_Stats_Inactive;
            ForegroundColor_Alias_Inactive.Text = ApplicationOptions.ForegroundColor_Alias_Inactive;
            ForegroundColor_Alias_Up.Text = ApplicationOptions.ForegroundColor_Alias_Up;
            ForegroundColor_Alias_Down.Text = ApplicationOptions.ForegroundColor_Alias_Down;
            ForegroundColor_Alias_Error.Text = ApplicationOptions.ForegroundColor_Alias_Error;
            ForegroundColor_Alias_Indeterminate.Text = ApplicationOptions.ForegroundColor_Alias_Indeterminate;
        }

        private async void OK_Click(object? sender, RoutedEventArgs e)
        {
            if (!await SaveGeneralOptions()) return;
            if (!await SaveNotificationOptions()) return;
            if (!await SaveEmailAlertOptions()) return;
            if (!await SaveAudioAlertOptions()) return;
            if (!await SaveLogOutputOptions()) return;
            if (!await SaveAdvancedOptions()) return;
            if (!await SaveLayoutOptions()) return;
            if (!SaveDisplayOptions()) return;

            if (SaveAsDefaults.IsChecked == true)
            {
                Configuration.Save();
            }

            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private async Task<bool> SaveGeneralOptions()
        {
            if (string.IsNullOrEmpty(PingInterval.Text))
            {
                await ShowError(Strings.Msg_InvalidInterval, GeneralTab, PingInterval);
                return false;
            }
            else if (string.IsNullOrEmpty(PingTimeout.Text))
            {
                await ShowError(Strings.Msg_InvalidTimeout, GeneralTab, PingTimeout);
                return false;
            }
            else if (string.IsNullOrEmpty(AlertThreshold.Text))
            {
                await ShowError(Strings.Msg_InvalidThreshold, GeneralTab, AlertThreshold);
                return false;
            }

            // [Certo] i18n — antes isto comparava o TEXTO do combo
            // ("minutes"/"hours"). Com os itens traduzidos, a comparação
            // nunca bateria e todo intervalo viraria segundos, silenciosamente.
            // Passou a usar o índice (0=segundos, 1=minutos, 2=horas), que é
            // o mesmo usado por PopulateGeneralOptions pra selecionar o item.
            var multiplier = PingIntervalUnits.SelectedIndex switch
            {
                1 => 1000 * 60,
                2 => 1000 * 60 * 60,
                _ => 1000,
            };

            if (int.TryParse(PingInterval.Text, out var pingInterval) && pingInterval > 0 && pingInterval <= 86400)
            {
                pingInterval *= multiplier;
            }
            else
            {
                pingInterval = Constants.DefaultInterval;
            }
            ApplicationOptions.PingInterval = pingInterval;

            if (int.TryParse(PingTimeout.Text, out var pingTimeout) && pingTimeout > 0 && pingTimeout <= 60)
            {
                pingTimeout *= 1000;
            }
            else
            {
                pingTimeout = Constants.DefaultTimeout;
            }
            ApplicationOptions.PingTimeout = pingTimeout;

            var isThresholdValid = int.TryParse(AlertThreshold.Text, out var alertThreshold) && alertThreshold > 0 && alertThreshold <= 60;
            if (!isThresholdValid)
            {
                alertThreshold = 1;
            }
            ApplicationOptions.AlertThreshold = alertThreshold;

            ApplicationOptions.InitialStartMode = (ApplicationOptions.StartMode)StartupMode.SelectedIndex;
            switch (StartupMode.SelectedIndex)
            {
                case (int)ApplicationOptions.StartMode.Blank:
                case (int)ApplicationOptions.StartMode.MultiInput:
                    var count = int.TryParse(InitialProbeCount.Text, out var parsedCount) ? parsedCount : 2;
                    count = count < 1 ? 1 : count > 20 ? 2 : count;
                    ApplicationOptions.InitialProbeCount = count;

                    var columnCount = int.TryParse(InitialColumnCount.Text, out var parsedColumnCount) ? parsedColumnCount : 2;
                    columnCount = columnCount < 1 ? 1 : columnCount > 10 ? 10 : columnCount;
                    ApplicationOptions.InitialColumnCount = columnCount;
                    break;
                case (int)ApplicationOptions.StartMode.Favorite:
                    ApplicationOptions.InitialFavorite = InitialFavorite.SelectedItem as string ?? string.Empty;
                    break;
            }

            return true;
        }

        private async Task<bool> SaveNotificationOptions()
        {
            if (IsAutoDismissEnabled.IsChecked == true)
            {
                if (int.TryParse(AutoDismissInterval.Text, out var result) && result > 0 && result < 100)
                {
                    ApplicationOptions.AutoDismissMilliseconds = result * 1000;
                    ApplicationOptions.IsAutoDismissEnabled = true;
                }
                else
                {
                    await ShowError(Strings.Msg_InvalidAutoDismiss, PopupAlertsTab, AutoDismissInterval);
                    return false;
                }
            }
            else
            {
                ApplicationOptions.IsAutoDismissEnabled = false;
            }

            if (PopupsMinimizedOption.IsChecked == true)
            {
                ApplicationOptions.PopupOption = ApplicationOptions.PopupNotificationOption.WhenMinimized;
            }
            else if (PopupsAlwaysOption.IsChecked == true)
            {
                ApplicationOptions.PopupOption = ApplicationOptions.PopupNotificationOption.Always;
            }
            else
            {
                ApplicationOptions.PopupOption = ApplicationOptions.PopupNotificationOption.Never;
            }

            return true;
        }

        private async Task<bool> SaveAdvancedOptions()
        {
            var regex = new Regex("^\\d+$");

            if (!regex.IsMatch(TTL.Text ?? string.Empty) || int.Parse(TTL.Text!) < 1 || int.Parse(TTL.Text!) > 255)
            {
                await ShowError(Strings.Msg_InvalidTtl, AdvancedTab, TTL);
                return false;
            }

            ApplicationOptions.TTL = int.Parse(TTL.Text!);

            if (PacketSizeOption.IsChecked == true)
            {
                if (!regex.IsMatch(PacketSize.Text ?? string.Empty) || int.Parse(PacketSize.Text!) < 0 || int.Parse(PacketSize.Text!) > 65500)
                {
                    await ShowError(Strings.Msg_InvalidPacketSize, AdvancedTab, PacketSize);
                    return false;
                }

                ApplicationOptions.Buffer = new byte[int.Parse(PacketSize.Text!)];
                ApplicationOptions.UseCustomBuffer = false;

                if (ApplicationOptions.Buffer.Length >= 33)
                {
                    Buffer.BlockCopy(Encoding.ASCII.GetBytes(Constants.DefaultIcmpData), 0, ApplicationOptions.Buffer, 0, 33);
                }
            }
            else
            {
                ApplicationOptions.Buffer = Encoding.ASCII.GetBytes(PacketData.Text ?? string.Empty);
                ApplicationOptions.UseCustomBuffer = true;
            }

            ApplicationOptions.DontFragment = DontFragment.IsChecked == true;
            ApplicationOptions.UpdatePingOptions();

            return true;
        }

        private async Task<bool> SaveEmailAlertOptions()
        {
            if (IsEmailAlertsEnabled.IsChecked == true)
            {
                var regex = new Regex("^\\d+$");

                if (string.IsNullOrEmpty(SmtpServer.Text))
                {
                    await ShowError(Strings.Msg_InvalidSmtpServer, EmailAlertsTab, SmtpServer);
                    return false;
                }
                else if (string.IsNullOrEmpty(SmtpPort.Text) || !regex.IsMatch(SmtpPort.Text))
                {
                    await ShowError(Strings.Msg_InvalidSmtpPort, EmailAlertsTab, SmtpPort);
                    return false;
                }
                else if (string.IsNullOrEmpty(EmailRecipientAddress.Text))
                {
                    await ShowError(Strings.Msg_InvalidRecipient, EmailAlertsTab, EmailRecipientAddress);
                    return false;
                }
                else if (string.IsNullOrEmpty(EmailFromAddress.Text))
                {
                    await ShowError(Strings.Msg_InvalidFrom, EmailAlertsTab, EmailFromAddress);
                    return false;
                }

                if (IsSmtpAuthenticationRequired.IsChecked == true)
                {
                    ApplicationOptions.IsEmailAuthenticationRequired = true;
                    if (string.IsNullOrEmpty(SmtpUsername.Text))
                    {
                        await ShowError(Strings.Msg_InvalidUsername, EmailAlertsTab, SmtpUsername);
                        return false;
                    }
                }
                else
                {
                    ApplicationOptions.IsEmailAuthenticationRequired = false;
                    SmtpUsername.Text = string.Empty;
                    SmtpPassword.Text = string.Empty;
                }

                ApplicationOptions.IsEmailAlertEnabled = true;
                ApplicationOptions.EmailServer = SmtpServer.Text;
                ApplicationOptions.EmailPort = SmtpPort.Text;
                ApplicationOptions.EmailUser = SmtpUsername.Text ?? string.Empty;
                ApplicationOptions.EmailPassword = SmtpPassword.Text ?? string.Empty;
                ApplicationOptions.EmailRecipient = EmailRecipientAddress.Text;
                ApplicationOptions.EmailFromAddress = EmailFromAddress.Text;
                ApplicationOptions.IsEmailSslEnabled = IsSmtpSslEnabled.IsChecked == true;

                return true;
            }
            else
            {
                ApplicationOptions.IsEmailAlertEnabled = false;
                return true;
            }
        }

        private static bool IsValidAudioPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                var fileName = Path.GetFileName(path);
                return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(path) && fileName.Length >= 1;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SaveAudioAlertOptions()
        {
            if (IsAudioDownAlertEnabled.IsChecked == true)
            {
                if (!IsValidAudioPath(AudioDownFilePath.Text))
                {
                    await ShowError(Strings.Msg_PathNotFound, AudioAlertTab, AudioDownFilePath);
                    return false;
                }
                ApplicationOptions.IsAudioDownAlertEnabled = true;
                ApplicationOptions.AudioDownFilePath = AudioDownFilePath.Text!;
            }
            else
            {
                ApplicationOptions.IsAudioDownAlertEnabled = false;
            }

            if (IsAudioUpAlertEnabled.IsChecked == true)
            {
                if (!IsValidAudioPath(AudioUpFilePath.Text))
                {
                    await ShowError(Strings.Msg_PathNotFound, AudioAlertTab, AudioUpFilePath);
                    return false;
                }
                ApplicationOptions.IsAudioUpAlertEnabled = true;
                ApplicationOptions.AudioUpFilePath = AudioUpFilePath.Text!;
            }
            else
            {
                ApplicationOptions.IsAudioUpAlertEnabled = false;
            }

            return true;
        }

        private static bool IsValidLogStatusPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                var fileName = Path.GetFileName(path);
                var directory = Path.GetDirectoryName(path);
                return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                    && directory != null && Directory.Exists(directory)
                    && fileName.Length >= 1;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SaveLogOutputOptions()
        {
            if (IsLogOutputEnabled.IsChecked == true)
            {
                if (!Directory.Exists(LogPath.Text))
                {
                    await ShowError(Strings.Msg_PathNotFound2, LogOutputTab, LogPath);
                    return false;
                }

                ApplicationOptions.IsLogOutputEnabled = true;
                ApplicationOptions.LogPath = LogPath.Text!;
            }
            else
            {
                ApplicationOptions.IsLogOutputEnabled = false;
            }

            if (IsLogStatusChangesEnabled.IsChecked == true)
            {
                if (!IsValidLogStatusPath(LogStatusChangesPath.Text))
                {
                    await ShowError(Strings.Msg_PathNotFound2, LogOutputTab, LogStatusChangesPath);
                    return false;
                }

                ApplicationOptions.IsLogStatusChangesEnabled = true;
                ApplicationOptions.LogStatusChangesPath = LogStatusChangesPath.Text!;
            }
            else
            {
                ApplicationOptions.IsLogStatusChangesEnabled = false;
            }

            return true;
        }

        private bool SaveDisplayOptions()
        {
            ApplicationOptions.IsAlwaysOnTopEnabled = IsAlwaysOnTopEnabled.IsChecked == true;
            ApplicationOptions.IsMinimizeToTrayEnabled = IsMinimizeToTrayEnabled.IsChecked == true;
            ApplicationOptions.IsExitToTrayEnabled = IsExitToTrayEnabled.IsChecked == true;

            // i18n (FASE 5): aplica a cultura imediatamente (afeta janelas
            // abertas DEPOIS deste ponto e strings montadas em runtime, como
            // as estatísticas do probe); os textos já construídos via x:Static
            // só mudam ao reiniciar — daí a nota de reinício na aba Display.
            // Persistência no vmPing.xml segue a regra das outras opções: só
            // com "Save as vmPing defaults" marcado (Configuration.Save no
            // OK_Click já inclui o nó Language).
            Localization.Apply(LanguageIndexToCode(LanguageCombo.SelectedIndex));

            return true;
        }

        private async Task<bool> SaveLayoutOptions()
        {
            foreach (var control in ColorsPanel.GetChildren())
            {
                if (control is TextBox box)
                {
                    if (!Util.IsValidHtmlColor(box.Text ?? string.Empty))
                    {
                        await ShowError(Strings.Msg_InvalidColor, LayoutTab, box);
                        box.SelectAll();
                        return false;
                    }
                }
            }

            ApplicationOptions.BackgroundColor_Probe_Inactive = BackgroundColor_Probe_Inactive.Text!;
            ApplicationOptions.BackgroundColor_Probe_Up = BackgroundColor_Probe_Up.Text!;
            ApplicationOptions.BackgroundColor_Probe_Down = BackgroundColor_Probe_Down.Text!;
            ApplicationOptions.BackgroundColor_Probe_Indeterminate = BackgroundColor_Probe_Indeterminate.Text!;
            ApplicationOptions.BackgroundColor_Probe_Error = BackgroundColor_Probe_Error.Text!;
            ApplicationOptions.ForegroundColor_Probe_Inactive = ForegroundColor_Probe_Inactive.Text!;
            ApplicationOptions.ForegroundColor_Probe_Up = ForegroundColor_Probe_Up.Text!;
            ApplicationOptions.ForegroundColor_Probe_Down = ForegroundColor_Probe_Down.Text!;
            ApplicationOptions.ForegroundColor_Probe_Indeterminate = ForegroundColor_Probe_Indeterminate.Text!;
            ApplicationOptions.ForegroundColor_Probe_Error = ForegroundColor_Probe_Error.Text!;
            ApplicationOptions.ForegroundColor_Stats_Inactive = ForegroundColor_Stats_Inactive.Text!;
            ApplicationOptions.ForegroundColor_Stats_Up = ForegroundColor_Stats_Up.Text!;
            ApplicationOptions.ForegroundColor_Stats_Down = ForegroundColor_Stats_Down.Text!;
            ApplicationOptions.ForegroundColor_Stats_Indeterminate = ForegroundColor_Stats_Indeterminate.Text!;
            ApplicationOptions.ForegroundColor_Stats_Error = ForegroundColor_Stats_Error.Text!;
            ApplicationOptions.ForegroundColor_Alias_Inactive = ForegroundColor_Alias_Inactive.Text!;
            ApplicationOptions.ForegroundColor_Alias_Up = ForegroundColor_Alias_Up.Text!;
            ApplicationOptions.ForegroundColor_Alias_Down = ForegroundColor_Alias_Down.Text!;
            ApplicationOptions.ForegroundColor_Alias_Indeterminate = ForegroundColor_Alias_Indeterminate.Text!;
            ApplicationOptions.ForegroundColor_Alias_Error = ForegroundColor_Alias_Error.Text!;

            return true;
        }

        private void EmailRecipientAddress_LostFocus(object? sender, RoutedEventArgs e)
        {
            var at = EmailRecipientAddress.Text?.IndexOf('@') ?? -1;
            if (string.IsNullOrEmpty(EmailFromAddress.Text) && at >= 0)
            {
                EmailFromAddress.Text = "vmPing" + EmailRecipientAddress.Text!.Substring(at);
            }
        }

        private void IsEmailAlertsEnabled_Click(object? sender, RoutedEventArgs e)
        {
            EmailOptionsPanel.IsVisible = IsEmailAlertsEnabled.IsChecked == true;
            if (IsEmailAlertsEnabled.IsChecked == true && string.IsNullOrEmpty(SmtpServer.Text))
            {
                SmtpServer.Focus();
            }
        }

        private void IsSmtpAuthenticationRequired_Click(object? sender, RoutedEventArgs e)
        {
            SmtpAuthPanel.IsVisible = IsSmtpAuthenticationRequired.IsChecked == true;
            if (IsSmtpAuthenticationRequired.IsChecked == true)
            {
                SmtpUsername.Focus();
            }
        }

        private async void TestEmail_Click(object? sender, RoutedEventArgs e)
        {
            TestEmailButton.IsEnabled = false;
            TestEmailButton.Content = "Sending...";

            var serverAddress = SmtpServer.Text ?? string.Empty;
            var serverPort = SmtpPort.Text ?? string.Empty;
            var isSslEnabled = IsSmtpSslEnabled.IsChecked == true;
            var isAuthRequired = IsSmtpAuthenticationRequired.IsChecked == true;
            var username = SmtpUsername.Text ?? string.Empty;
            var password = new SecureString();
            foreach (var c in SmtpPassword.Text ?? string.Empty)
            {
                password.AppendChar(c);
            }
            password.MakeReadOnly();
            var mailFrom = EmailFromAddress.Text ?? string.Empty;
            var mailRecipient = EmailRecipientAddress.Text ?? string.Empty;

            // [Certo] FASE 4 — simplificação: o original despachava manualmente de
            // volta pra UI thread via Application.Current.Dispatcher.BeginInvoke
            // de dentro do próprio Task.Run, porque o WPF de então não tinha
            // como fazer isso de forma mais direta ali. Aqui, `await Task.Run`
            // já retoma a continuação na UI thread automaticamente (o
            // SynchronizationContext do Avalonia é capturado antes do Task.Run),
            // então não precisa do despacho manual.
            try
            {
                await Task.Run(() => Util.SendTestEmail(
                    serverAddress, serverPort, isSslEnabled, isAuthRequired,
                    username, password, mailFrom, mailRecipient));

                if (IsVisible)
                {
                    await new DialogWindow(DialogWindow.DialogIcon.Info, "Email Test", "A test email was sent.", "OK", false)
                        .ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                if (IsVisible)
                {
                    await ShowError($"{Strings.Msg_TestFailed} {ex.Message}", EmailAlertsTab, TestEmailButton);
                }
            }

            TestEmailButton.IsEnabled = true;
            TestEmailButton.Content = "Test";
        }

        // [Certo] IStorageProvider.OpenFolderPickerAsync (API real, confirmada
        // contra código-fonte E contra o comportamento real agora). Bug real
        // encontrado em runtime: sem try/catch aqui, uma DBusException vinda do
        // xdg-desktop-portal (AccessDenied: "Portal operation not allowed:
        // Unable to open /proc/<pid>/root" — falha de política do
        // xdg-document-portal/AppArmor em algumas distros pra apps não
        // empacotados como Flatpak/Snap, fora do nosso controle) derrubava o
        // app inteiro, sem nenhuma mensagem — só um stack trace no terminal.
        // Isso não é específico dessa distro: qualquer ambiente sem portal
        // configurado (sessão sem D-Bus, servidor sem GUI completa, etc.) bate
        // no mesmo caminho. Envolvido em try/catch: se o seletor falhar por
        // qualquer motivo, mostra um erro e deixa o campo de texto como está
        // (o usuário sempre pode digitar o caminho manualmente, como já
        // confirmado que funciona).
        // [Certo] Estratégia final dos botões Browse, depois do bug do portal
        // se confirmar sem solução do nosso lado: tenta o seletor NATIVO
        // primeiro (melhor UX — marcadores, pastas recentes, integração com o
        // desktop); se ele falhar por qualquer motivo, cai automaticamente no
        // seletor próprio (UI/FileBrowserWindow), que só usa System.IO e
        // funciona em qualquer ambiente. Antes disso, a falha do portal
        // deixava o usuário sem nenhuma forma de navegar — só digitar o
        // caminho à mão.
        private async Task<string?> PickFolderAsync(string title, string? currentPath)
        {
            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider != null)
            {
                try
                {
                    var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = title,
                        AllowMultiple = false,
                    });
                    return folders.Count > 0 ? folders[0].Path.LocalPath : null;
                }
                catch
                {
                    // Portal indisponível/recusado — segue pro fallback abaixo.
                }
            }

            return await new FileBrowserWindow(foldersOnly: true, currentPath, extensions: null)
                .ShowDialog<string?>(this);
        }

        private async Task<string?> PickFileAsync(string title, string? currentPath, string[] extensions)
        {
            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider != null)
            {
                try
                {
                    var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = title,
                        AllowMultiple = false,
                        FileTypeFilter = new[]
                        {
                            new FilePickerFileType("Audio files")
                            {
                                Patterns = extensions.Select(x => "*" + x).ToArray(),
                            },
                            FilePickerFileTypes.All,
                        },
                    });
                    return files.Count > 0 ? files[0].Path.LocalPath : null;
                }
                catch
                {
                    // Portal indisponível/recusado — segue pro fallback abaixo.
                }
            }

            return await new FileBrowserWindow(foldersOnly: false, currentPath, extensions)
                .ShowDialog<string?>(this);
        }

        private async void BrowseLogPath_Click(object? sender, RoutedEventArgs e)
        {
            var chosen = await PickFolderAsync(Strings.Options_LogPath, LogPath.Text);
            if (!string.IsNullOrEmpty(chosen))
            {
                LogPath.Text = chosen;
            }
        }

        private async void BrowseLogStatusChangesPath_Click(object? sender, RoutedEventArgs e)
        {
            // Aqui o usuário escolhe a PASTA e o app acrescenta o nome do
            // arquivo — mesmo comportamento de antes.
            var currentDir = string.IsNullOrEmpty(LogStatusChangesPath.Text)
                ? null
                : Path.GetDirectoryName(LogStatusChangesPath.Text);

            var chosen = await PickFolderAsync(Strings.Options_LogStatusPath, currentDir);
            if (!string.IsNullOrEmpty(chosen))
            {
                LogStatusChangesPath.Text = Path.Combine(chosen, "vmping-status.txt");
            }
        }

        private void AudioDownBrowse_Click(object? sender, RoutedEventArgs e) => _ = AudioFileBrowse(AudioDownFilePath);

        private void AudioUpBrowse_Click(object? sender, RoutedEventArgs e) => _ = AudioFileBrowse(AudioUpFilePath);

        // [Certo] Extensões: o filtro original só listava *.wav, escondendo os
        // próprios arquivos padrão do app (.oga) do seletor.
        private static readonly string[] AudioExtensions = { ".wav", ".oga", ".ogg", ".mp3", ".flac" };

        private async Task AudioFileBrowse(TextBox tb)
        {
            var chosen = await PickFileAsync(Strings.Options_AudioPath, tb.Text, AudioExtensions);
            if (!string.IsNullOrEmpty(chosen))
            {
                tb.Text = chosen;
            }
        }

        private void AudioDownPlay_Click(object? sender, RoutedEventArgs e) => AudioFilePlay(AudioDownFilePath.Text);

        private void AudioUpPlay_Click(object? sender, RoutedEventArgs e) => AudioFilePlay(AudioUpFilePath.Text);

        private void AudioFilePlay(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                Probe.PlaySound(path);
            }
            catch
            {
                _ = ShowError(Strings.Msg_AudioPlayFailed, AudioAlertTab, AudioAlertTab);
            }
        }

        private void IsAudioDownAlertEnabled_Click(object? sender, RoutedEventArgs e)
        {
            AudioDownPanel.IsVisible = IsAudioDownAlertEnabled.IsChecked == true;
            if (string.IsNullOrEmpty(AudioDownFilePath.Text))
            {
                var defaultPath = Environment.ExpandEnvironmentVariables(Constants.DefaultAudioDownFilePath);
                if (File.Exists(defaultPath))
                {
                    AudioDownFilePath.Text = defaultPath;
                }
            }
        }

        private void IsAudioUpAlertEnabled_Click(object? sender, RoutedEventArgs e)
        {
            AudioUpPanel.IsVisible = IsAudioUpAlertEnabled.IsChecked == true;
            if (string.IsNullOrEmpty(AudioUpFilePath.Text))
            {
                var defaultPath = Environment.ExpandEnvironmentVariables(Constants.DefaultAudioUpFilePath);
                if (File.Exists(defaultPath))
                {
                    AudioUpFilePath.Text = defaultPath;
                }
            }
        }

        private void IsAutoDismissEnabled_Click(object? sender, RoutedEventArgs e)
        {
            AutoDismissIntervalPanel.IsVisible = IsAutoDismissEnabled.IsChecked == true;
        }

        private void IsLogOutputEnabled_Click(object? sender, RoutedEventArgs e)
        {
            LogOutputPanel.IsVisible = IsLogOutputEnabled.IsChecked == true;
        }

        private void IsLogStatusChangesEnabled_Click(object? sender, RoutedEventArgs e)
        {
            LogStatusChangesPanel.IsVisible = IsLogStatusChangesEnabled.IsChecked == true;
        }

        private void UpdateByteCount()
        {
            var regex = new Regex("^\\d+$");
            if (PacketSizeOption.IsChecked == true)
            {
                if (PacketSize != null && regex.IsMatch(PacketSize.Text ?? string.Empty))
                {
                    Bytes.Text = (int.Parse(PacketSize.Text!) + 28).ToString();
                }
                else
                {
                    Bytes.Text = "?";
                }
            }
            else
            {
                Bytes.Text = ((PacketData.Text ?? string.Empty).Length + 28).ToString();
            }
        }

        private void PacketData_TextChanged(object? sender, TextChangedEventArgs e) => UpdateByteCount();

        private void PacketSizeOption_Checked(object? sender, RoutedEventArgs e)
        {
            if (!_isReady)
            {
                return;
            }

            PacketSize.IsVisible = true;
            PacketData.IsVisible = false;
            UpdateByteCount();
        }

        private void UseCustomPacketOption_Checked(object? sender, RoutedEventArgs e)
        {
            if (!_isReady)
            {
                return;
            }

            PacketSize.IsVisible = false;
            PacketData.IsVisible = true;
            UpdateByteCount();
        }

        private void StartupMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_isReady)
            {
                return;
            }

            var isFavoriteStartup = StartupMode.SelectedIndex == (int)ApplicationOptions.StartMode.Favorite;
            StandardStartupPanel.IsVisible = !isFavoriteStartup;
            FavoriteStartupPanel.IsVisible = isFavoriteStartup;
        }

        private void RestoreDefaultColors_Click(object? sender, RoutedEventArgs e)
        {
            BackgroundColor_Probe_Inactive.Text = Constants.Color_Probe_Background_Inactive;
            BackgroundColor_Probe_Up.Text = Constants.Color_Probe_Background_Up;
            BackgroundColor_Probe_Down.Text = Constants.Color_Probe_Background_Down;
            BackgroundColor_Probe_Error.Text = Constants.Color_Probe_Background_Error;
            BackgroundColor_Probe_Indeterminate.Text = Constants.Color_Probe_Background_Indeterminate;
            ForegroundColor_Probe_Inactive.Text = Constants.Color_Probe_Foreground_Inactive;
            ForegroundColor_Probe_Up.Text = Constants.Color_Probe_Foreground_Up;
            ForegroundColor_Probe_Down.Text = Constants.Color_Probe_Foreground_Down;
            ForegroundColor_Probe_Error.Text = Constants.Color_Probe_Foreground_Error;
            ForegroundColor_Probe_Indeterminate.Text = Constants.Color_Probe_Foreground_Indeterminate;
            ForegroundColor_Stats_Inactive.Text = Constants.Color_Statistics_Foreground_Inactive;
            ForegroundColor_Stats_Up.Text = Constants.Color_Statistics_Foreground_Up;
            ForegroundColor_Stats_Down.Text = Constants.Color_Statistics_Foreground_Down;
            ForegroundColor_Stats_Error.Text = Constants.Color_Statistics_Foreground_Error;
            ForegroundColor_Stats_Indeterminate.Text = Constants.Color_Statistics_Foreground_Inactive;
            ForegroundColor_Alias_Inactive.Text = Constants.Color_Alias_Foreground_Inactive;
            ForegroundColor_Alias_Up.Text = Constants.Color_Alias_Foreground_Up;
            ForegroundColor_Alias_Down.Text = Constants.Color_Alias_Foreground_Down;
            ForegroundColor_Alias_Error.Text = Constants.Color_Alias_Foreground_Error;
            ForegroundColor_Alias_Indeterminate.Text = Constants.Color_Alias_Foreground_Indeterminate;
        }
    }
}
