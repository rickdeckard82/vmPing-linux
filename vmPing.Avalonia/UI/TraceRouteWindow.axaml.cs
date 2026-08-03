using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class TraceRouteWindow : Window
    {
        // [Certo] FASE 4/correção — reescrito pra chamar o utilitário
        // `traceroute` do sistema em vez de System.Net.NetworkInformation.Ping
        // com PingOptions.Ttl, que não funciona pra esse cenário no Linux
        // (ver comentário detalhado no .axaml). Classes/NetworkRoute.cs
        // continua em uso só pelos campos IsActive/networkRoute — os campos
        // BgWorker/ResetEvent/DestinationIp/PingTimeout/Timer, que existiam
        // pra sustentar o BackgroundWorker+Ping antigo, ficaram sem uso aqui
        // (mantidos na classe porque não têm outro dono nem custo real).
        internal NetworkRoute Route { get; set; } = new NetworkRoute();

        private static readonly Regex HopLine = new(
            @"^\s*(?<hop>\d+)\s+(?:(?<name>\S+)\s+\((?<ip>[\da-fA-F:.]+)\)|\*)",
            RegexOptions.Compiled);

        private readonly TextBox? _hostname;
        private readonly ListBox? _traceData;
        private readonly TextBlock? _traceStatus;
        private Process? _process;

        public TraceRouteWindow()
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _hostname = this.FindControl<TextBox>("Hostname");
            _traceData = this.FindControl<ListBox>("TraceData");
            _traceStatus = this.FindControl<TextBlock>("TraceStatus");

            DataContext = Route;
            if (_traceData != null)
            {
                _traceData.ItemsSource = Route.networkRoute;
            }

            Opened += (_, _) => _hostname?.Focus();
            Closed += (_, _) => StopProcess(killOnly: true);
        }

        private void Trace_Click(object? sender, RoutedEventArgs e)
        {
            if (!Route.IsActive)
            {
                StartTrace();
            }
            else
            {
                StopProcess(killOnly: false);
            }
        }

        private void StartTrace()
        {
            if (string.IsNullOrEmpty(_hostname?.Text))
            {
                return;
            }

            StopProcess(killOnly: true);

            if (_traceStatus != null)
            {
                _traceStatus.Text = "Tracing route...";
                _traceStatus.IsVisible = true;
            }

            Route.networkRoute.Clear();
            Route.IsActive = true;

            // -q 1: uma sonda por hop (menos ruído/mais rápido, e o usuário
            // pediu menos informação por hop). -w 2: timeout de 2s por sonda,
            // igual ao que já usávamos com Ping.Send. Sem -n: deixa o
            // traceroute fazer resolução reversa de DNS, que é literalmente o
            // que foi pedido.
            var startInfo = new ProcessStartInfo
            {
                FileName = "traceroute",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-q");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-w");
            startInfo.ArgumentList.Add("2");
            startInfo.ArgumentList.Add(_hostname.Text);

            try
            {
                _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _process.OutputDataReceived += Process_OutputDataReceived;
                _process.Exited += Process_Exited;
                _process.Start();
                _process.BeginOutputReadLine();
            }
            catch (Exception ex)
            {
                // [Certo] Caminho mais provável de cair aqui: o pacote
                // `traceroute` não está instalado no sistema (Win32Exception
                // "No such file or directory"). Reportado na barra de status
                // em vez de falhar silenciosamente — ver também a dependência
                // adicionada em packaging/debian/control.
                Route.IsActive = false;
                if (_traceStatus != null)
                {
                    _traceStatus.Text = $"• Error: {ex.Message}";
                }
                _process = null;
            }
        }

        private void Process_OutputDataReceived(object? sender, DataReceivedEventArgs e)
        {
            var line = e.Data;
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            Dispatcher.UIThread.Post(() => AddHopFromLine(line));
        }

        private void AddHopFromLine(string line)
        {
            var match = HopLine.Match(line);
            if (!match.Success)
            {
                return;
            }

            var node = new NetworkRouteNode
            {
                HopId = int.Parse(match.Groups["hop"].Value),
            };

            if (match.Groups["ip"].Success)
            {
                var name = match.Groups["name"].Value;
                var ip = match.Groups["ip"].Value;
                node.HostAddress = name == ip ? ip : $"{name} ({ip})";
                node.ReplyStatus = IPStatus.TtlExpired;
            }
            else
            {
                node.HostAddress = "Timed out";
                node.ReplyStatus = IPStatus.TimedOut;
            }

            Route.networkRoute.Add(node);
            _traceData?.ScrollIntoView(node);
        }

        private void Process_Exited(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Route.IsActive = false;
                if (_traceStatus != null)
                {
                    _traceStatus.Text = "★ Trace complete";
                }
                _hostname?.Focus();
            });
        }

        // killOnly=true: encerra o processo (se houver) sem mexer em
        // IsActive/texto de status — usado ao iniciar um novo trace ou ao
        // fechar a janela. killOnly=false: caminho do botão "Stop" clicado
        // pelo usuário, também atualiza IsActive/status.
        private void StopProcess(bool killOnly)
        {
            var process = _process;
            _process = null;

            if (process != null)
            {
                try
                {
                    process.OutputDataReceived -= Process_OutputDataReceived;
                    process.Exited -= Process_Exited;
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Processo já pode ter saído sozinho entre o HasExited e o Kill.
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (!killOnly)
            {
                Route.IsActive = false;
                if (_traceStatus != null)
                {
                    _traceStatus.Text = "• Trace cancelled";
                }
                _hostname?.Focus();
            }
        }
    }
}
