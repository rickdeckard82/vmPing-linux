using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using vmPing.UI;

namespace vmPing.Classes
{
    public partial class Probe
    {
        public void StartStop()
        {
            if (string.IsNullOrWhiteSpace(Hostname))
            {
                return;
            }

            if (IsActive)
            {
                // Stopping probe.
                StopProbe(ProbeStatus.Inactive);
                return;
            }

            // Starting probe.
            CancelSource = new CancellationTokenSource();

            if (Hostname.StartsWith("#"))
            {
                Type = ProbeType.Comment;
                return;
            }

            if (Hostname.StartsWith("D/"))
            {
                Type = ProbeType.Dns;
                Hostname = Hostname.Substring(2);
                PerformDnsLookup(CancelSource.Token);
                return;
            }

            if (Hostname.StartsWith("T/"))
            {
                Type = ProbeType.Traceroute;
                Hostname = Hostname.Substring(2);
                PerformTraceroute(CancelSource.Token);
                return;
            }

            Type = ProbeType.Ping;

            Dispatcher.UIThread.Post(() =>
            {
                lock (mutex)
                {
                    StatusChangeLog.Add(new StatusChangeLog
                    {
                        Timestamp = DateTime.Now,
                        Hostname = Hostname,
                        Alias = Alias,
                        Status = ProbeStatus.Start
                    });
                }
            });

            if (IsTcpPing(Hostname))
            {
                Task.Run(() => PerformTcpProbe(CancelSource.Token), CancelSource.Token);
            }
            else
            {
                Task.Run(() => PerformIcmpProbe(CancelSource.Token), CancelSource.Token);
            }
        }

        private static bool IsTcpPing(string hostname)
        {
            return hostname.Count(f => f == ':') == 1 || hostname.Contains("]:");
        }

        private void InitializeProbe()
        {
            IsActive = true;
            Status = ProbeStatus.Inactive;
            Statistics.Reset();
            IndeterminateCount = 0;
            HighLatencyCount = 0;
            MinRtt = long.MaxValue;
            History = new ObservableCollection<string>();
        }

        private void StopProbe(ProbeStatus status)
        {
            CancelSource.Cancel();
            Status = status;
            IsActive = false;

            if (status != ProbeStatus.Error)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    lock (mutex)
                    {
                        WriteFinalStatisticsToHistory();
                    }
                });
            }

            AddStatusHistory(ProbeStatus.Stop);
        }

        private void AddStatusHistory(ProbeStatus status, bool isHidden = false)
        {
            Dispatcher.UIThread.Post(() =>
            {
                lock (mutex)
                {
                    StatusChangeLog.Add(new StatusChangeLog
                    {
                        Timestamp = DateTime.Now,
                        Hostname = Hostname,
                        Alias = Alias,
                        Status = status,
                        HasStatusBeenCleared = isHidden
                    });
                }
            });
        }

        private async Task<bool> IsHostInvalid(string host, CancellationToken token)
        {
            try
            {
                switch (Uri.CheckHostName(host))
                {
                    case UriHostNameType.IPv4:
                    case UriHostNameType.IPv6:
                        // IP address was entered. No further action necessary.
                        break;
                    case UriHostNameType.Dns:
                        var ipAddresses = await Dns.GetHostAddressesAsync(host);
                        token.ThrowIfCancellationRequested();
                        if (ipAddresses.Length > 0)
                        {
                            await Dispatcher.UIThread.InvokeAsync(
                                new Action(() => AddHistory($"    ({ipAddresses[0]})")));
                        }
                        break;
                    default:
                        throw new Exception();
                }
                return false;
            }
            catch
            {
                if (!token.IsCancellationRequested)
                {
                    await Dispatcher.UIThread.InvokeAsync(
                        new Action(() => AddHistory($"{Environment.NewLine}Unable to resolve hostname")));
                }
                return true;
            }
        }

        private void WriteToLog(string message)
        {
            if (!ApplicationOptions.IsLogOutputEnabled || string.IsNullOrEmpty(ApplicationOptions.LogPath))
            {
                return;
            }

            string logPath = Path.Combine(ApplicationOptions.LogPath, $"{Util.GetSafeFilename(Hostname)}.txt");

            try
            {
                File.AppendAllText(logPath, message.Insert(1, $"{DateTime.Now.ToShortDateString()} ") + Environment.NewLine);
            }
            catch (Exception ex)
            {
                ApplicationOptions.IsLogOutputEnabled = false;
                ShowError($"{Properties.Strings.Msg_LogWriteFailed} {ex.Message}");
            }
        }

        private void WriteToStatusChangesLog(StatusChangeLog status)
        {
            if (!ApplicationOptions.IsLogStatusChangesEnabled || string.IsNullOrEmpty(ApplicationOptions.LogStatusChangesPath))
            {
                return;
            }

            try
            {
                File.AppendAllText(ApplicationOptions.LogStatusChangesPath,
                    $"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}\t{status.Hostname}\t{status.Alias}\t{status.StatusAsString}");
            }
            catch (Exception ex)
            {
                ApplicationOptions.IsLogStatusChangesEnabled = false;
                ShowError($"{Properties.Strings.Msg_LogWriteFailed} {ex.Message}");
            }
        }

        private void DisplayStatistics()
        {
            // i18n (FASE 5): chaves do resx que o original já tinha.
            StatisticsText =
                $"{Properties.Strings.Probe_Stat_Sent} {Statistics.Sent} " +
                $"{Properties.Strings.Probe_Stat_Received} {Statistics.Received} " +
                $"{Properties.Strings.Probe_Stat_Lost} {Statistics.Lost}";
        }

        private void OnStatusChange(ProbeStatus newStatus, string alertType)
        {
            Status = newStatus;
            TriggerStatusChange(new StatusChangeLog
            {
                Timestamp = DateTime.Now,
                Hostname = Hostname,
                Alias = Alias,
                Status = newStatus
            });

            if (ApplicationOptions.IsEmailAlertEnabled)
            {
                Util.SendEmail(alertType, Hostname, Alias);
            }
        }

        // [Provável] Application.Current.Windows / .MainWindow (WPF) não existem no
        // Avalonia — a coleção de janelas abertas e a MainWindow ficam no lifetime
        // desktop (IClassicDesktopStyleApplicationLifetime), acessível via
        // Application.Current.ApplicationLifetime.
        private static IClassicDesktopStyleApplicationLifetime? GetDesktopLifetime()
            => Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

        private void TriggerStatusChange(StatusChangeLog status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var lifetime = GetDesktopLifetime();
                bool shouldPopup = ApplicationOptions.PopupOption == ApplicationOptions.PopupNotificationOption.Always
                    || (ApplicationOptions.PopupOption == ApplicationOptions.PopupNotificationOption.WhenMinimized
                    && lifetime?.MainWindow?.WindowState == WindowState.Minimized);

                var openWindows = lifetime?.Windows ?? (System.Collections.Generic.IReadOnlyList<Window>)Array.Empty<Window>();

                lock (mutex)
                {
                    if (shouldPopup && !openWindows.OfType<PopupNotificationWindow>().Any())
                    {
                        foreach (var entry in StatusChangeLog)
                        {
                            entry.HasStatusBeenCleared = true;
                        }
                    }

                    StatusChangeLog.Add(status);
                }

                if (shouldPopup && !openWindows.OfType<PopupNotificationWindow>().Any())
                {
                    new PopupNotificationWindow(StatusChangeLog).Show();
                }
            });

            if (ApplicationOptions.IsLogStatusChangesEnabled)
            {
                lock (mutex)
                {
                    WriteToStatusChangesLog(status);
                }
            }

            if ((ApplicationOptions.IsAudioDownAlertEnabled) && (status.Status == ProbeStatus.Down))
            {
                try
                {
                    PlaySound(ApplicationOptions.AudioDownFilePath);
                }
                catch (Exception ex)
                {
                    ApplicationOptions.IsAudioDownAlertEnabled = false;
                    ShowError($"{Properties.Strings.Msg_AudioFailed} {ex.Message}");
                }
            }
            else if ((ApplicationOptions.IsAudioUpAlertEnabled) && (status.Status == ProbeStatus.Up))
            {
                try
                {
                    PlaySound(ApplicationOptions.AudioUpFilePath);
                }
                catch (Exception ex)
                {
                    ApplicationOptions.IsAudioUpAlertEnabled = false;
                    ShowError($"{Properties.Strings.Msg_AudioFailed} {ex.Message}");
                }
            }
        }

        // [Certo] System.Media.SoundPlayer é Windows-only (usa winmm.dll) e não
        // compila fora do Windows. Decisão tomada na fase de licenciamento: em vez
        // de uma lib gerenciada (ex: LibVLCSharp, que é LGPL e conflita com
        // publish self-contained/single-file), toca o som chamando um player de
        // linha de comando já presente na maioria das distros Linux com áudio
        // (pulseaudio-utils ou alsa-utils). Fire-and-forget, igual ao
        // comportamento assíncrono do SoundPlayer.Play() original.
        // [Certo] FASE 4 — trocado de `private` para `internal`: UI/OptionsWindow
        // reaproveita este método pros botões "Test" de áudio (tocar o som
        // sem precisar disparar um alerta de verdade), em vez de duplicar a
        // lógica de shell-out. Mesmo assembly, sem mudança de comportamento.
        internal static void PlaySound(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Audio file path is empty.");
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Audio file not found.", path);
            }

            bool isWav = string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);
            var player = ResolveSoundPlayerCommand(path)
                ?? throw new InvalidOperationException(isWav
                    ? "Nenhum player de áudio encontrado (paplay/aplay/ffplay). Instale o pacote 'pulseaudio-utils' ou 'alsa-utils'."
                    : "Nenhum player de áudio compatível com este formato encontrado (paplay/ffplay). " +
                      "'aplay' não decodifica arquivos comprimidos (Ogg/MP3/...). Instale 'pulseaudio-utils' (paplay) ou 'ffmpeg' (ffplay).");

            // [Segurança] ArgumentList em vez da string Arguments: cada argumento
            // é passado ao execve() como elemento separado, sem parsing de
            // quoting. A versão anterior montava `$"{args} \"{path}\""` — um
            // caminho contendo aspas quebraria o quoting e injetaria argumentos
            // extras no player (ffplay/paplay aceitam opções que leem e gravam
            // arquivos). O caminho vem do vmPing.xml, que em "modo portátil"
            // pode ficar num diretório gravável por terceiros; e o binário
            // carrega CAP_NET_RAW, então qualquer influência sobre o que ele
            // executa merece tratamento estrito. Também vale como defesa contra
            // caminhos com espaço, que a versão anterior só tratava por sorte.
            var psi = new ProcessStartInfo(player.Command)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in player.Args)
            {
                psi.ArgumentList.Add(arg);
            }
            psi.ArgumentList.Add(path);

            Process.Start(psi);
        }

        // Args é array (não string) porque cada elemento vira um argumento
        // separado no ArgumentList — ver comentário de segurança em PlaySound.
        private readonly record struct SoundPlayerCommand(string Command, string[] Args);

        // [Certo] Bug real reportado pelo usuário: "os sons fizeram um barulho
        // horrível" ao testar os alertas sonoros padrão (Constants.DefaultAudio*
        // apontam pra .oga, Ogg Vorbis). Causa: `aplay` (ALSA, alsa-utils) só
        // decodifica WAV/raw PCM — não entende Ogg/MP3/etc. Quando dado um
        // arquivo que não é WAV e não tem o cabeçalho RIFF que ele procura,
        // `aplay` não recusa, ele toca os bytes crus do container Ogg como se
        // fossem PCM (8-bit/8kHz por padrão) — daí o ruído horrível, é o
        // clássico "tocar .mp3 no aplay" da vida real. `paplay` (PulseAudio/
        // PipeWire-pulse) usa libsndfile, que decodifica Ogg Vorbis corretamente
        // na maioria das distros modernas; `ffplay` decodifica qualquer coisa
        // via ffmpeg. A lista de candidatos antes tentava paplay->aplay->ffplay
        // sem olhar pra extensão do arquivo — se paplay não estivesse instalado
        // (bem possível dependendo da distro/ambiente), caía direto no aplay
        // pra um arquivo .oga, com esse resultado. Corrigido: pra qualquer
        // extensão que não seja .wav, `aplay` é excluído da lista de
        // candidatos (só entra como opção pra .wav, onde ele decodifica certo).
        private static SoundPlayerCommand? ResolveSoundPlayerCommand(string path)
        {
            bool isWav = string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);

            var ffplayArgs = new[] { "-nodisp", "-autoexit", "-loglevel", "quiet" };
            var noArgs = Array.Empty<string>();

            var candidates = isWav
                ? new (string cmd, string[] args)[]
                {
                    ("paplay", noArgs),
                    ("aplay", noArgs),
                    ("ffplay", ffplayArgs),
                }
                : new (string cmd, string[] args)[]
                {
                    ("paplay", noArgs),
                    ("ffplay", ffplayArgs),
                    // aplay deliberadamente de fora aqui: não decodifica formatos
                    // comprimidos (Ogg/MP3/...), só toca ruído no lugar.
                };

            foreach (var (cmd, args) in candidates)
            {
                if (IsCommandAvailable(cmd))
                {
                    return new SoundPlayerCommand(cmd, args);
                }
            }

            return null;
        }

        private static bool IsCommandAvailable(string command)
        {
            try
            {
                // `command` só recebe constantes internas, mas usa ArgumentList
                // por consistência: nenhum ProcessStartInfo neste projeto monta
                // argumentos por concatenação de string.
                var psi = new ProcessStartInfo("which")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add(command);

                using var which = Process.Start(psi);
                which?.WaitForExit(1000);
                return which?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        // [Certo] Consolidado com Classes/Util.ShowError, que agora usa
        // UI/DialogWindow.ErrorWindow(...).Show() — o original já usava um
        // MessageBox/dialog de erro simples em ambos os lugares; não há
        // necessidade de duas implementações.
        private void ShowError(string message)
        {
            Util.ShowError(message);
        }
    }
}
