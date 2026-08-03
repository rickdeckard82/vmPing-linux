using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using vmPing.Classes;

namespace vmPing.UI
{
    // FASE 5 — janela nova (nslookup/dig), ver comentário no .axaml.
    // Uma instância por ferramenta; o menu abre new DnsLookupWindow("dig")
    // ou ("nslookup"). Diferente da TraceRouteWindow (que lê a saída linha a
    // linha porque o traceroute demora e o usuário quer ver o progresso),
    // aqui a consulta termina em poucos segundos — lê stdout/stderr até o
    // fim de uma vez, sem streaming. stdout e stderr são lidos em paralelo
    // (Task.WhenAll) pra não deadlockar caso um dos pipes encha enquanto o
    // outro é drenado.
    public partial class DnsLookupWindow : Window
    {
        private readonly string _tool;
        private readonly TextBox? _hostname;
        private readonly TextBox? _output;
        private readonly Button? _run;
        private readonly ComboBox? _recordType;
        private readonly TextBox? _server;
        private readonly CheckBox? _flagShort;
        private readonly CheckBox? _flagAnswer;
        private Process? _process;

        private static readonly string[] RecordTypes =
        {
            // O primeiro item é substituído pelo rótulo localizado "(padrão)"
            // no construtor; os demais são tipos de registro literais.
            "", "A", "AAAA", "MX", "NS", "TXT", "CNAME", "SOA", "PTR", "SRV", "ANY",
        };

        // Construtor sem parâmetro exigido pelo XAML loader; o real é o de baixo.
        public DnsLookupWindow() : this("nslookup") { }

        public DnsLookupWindow(string tool)
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _tool = tool;
            Title = tool;

            _hostname = this.FindControl<TextBox>("Hostname");
            _output = this.FindControl<TextBox>("Output");
            _run = this.FindControl<Button>("Run");
            _recordType = this.FindControl<ComboBox>("RecordType");
            _server = this.FindControl<TextBox>("Server");
            _flagShort = this.FindControl<CheckBox>("FlagShort");
            _flagAnswer = this.FindControl<CheckBox>("FlagAnswer");

            var header = this.FindControl<TextBlock>("Header");
            if (header != null)
            {
                header.Text = tool;
            }

            if (_recordType != null)
            {
                var items = (string[])RecordTypes.Clone();
                items[0] = Properties.Strings.DnsLookup_TypeDefault;
                _recordType.ItemsSource = items;
                _recordType.SelectedIndex = 0;
            }

            // +short/+answer são sintaxe exclusiva do dig; no nslookup os
            // checkboxes somem em vez de fingir que funcionam. O seletor de
            // tipo também sai do nslookup a pedido do usuário (2026-08-02) —
            // lá fica só a consulta padrão; o campo de servidor continua.
            if (_tool != "dig")
            {
                if (_flagShort != null) { _flagShort.IsVisible = false; }
                if (_flagAnswer != null) { _flagAnswer.IsVisible = false; }
                if (_recordType != null) { _recordType.IsVisible = false; }

                var typeLabel = this.FindControl<TextBlock>("RecordTypeLabel");
                if (typeLabel != null) { typeLabel.IsVisible = false; }
            }

            Opened += (_, _) => _hostname?.Focus();
            Closed += (_, _) => KillProcess();
        }

        // Monta a lista de argumentos conforme a ferramenta.
        //   dig:      [@servidor] host [TIPO] [+short] [+noall +answer]
        //   nslookup: [-type=TIPO] host [servidor]
        // "+answer" sozinho no dig não muda nada visível (a resposta já
        // aparece por padrão) — o uso consagrado é "+noall +answer" (só a
        // seção de resposta), então o checkbox mapeia pra esse par.
        private void BuildArguments(ProcessStartInfo startInfo, string target)
        {
            var server = _server?.Text?.Trim().TrimStart('@');
            var type = _recordType?.SelectedIndex > 0
                ? RecordTypes[_recordType.SelectedIndex]
                : null;

            if (_tool == "dig")
            {
                if (!string.IsNullOrWhiteSpace(server))
                {
                    startInfo.ArgumentList.Add($"@{server}");
                }
                startInfo.ArgumentList.Add(target);
                if (type != null)
                {
                    startInfo.ArgumentList.Add(type);
                }
                if (_flagShort?.IsChecked == true)
                {
                    startInfo.ArgumentList.Add("+short");
                }
                if (_flagAnswer?.IsChecked == true)
                {
                    startInfo.ArgumentList.Add("+noall");
                    startInfo.ArgumentList.Add("+answer");
                }
            }
            else
            {
                if (type != null)
                {
                    startInfo.ArgumentList.Add($"-type={type}");
                }
                startInfo.ArgumentList.Add(target);
                if (!string.IsNullOrWhiteSpace(server))
                {
                    startInfo.ArgumentList.Add(server);
                }
            }
        }

        private async void Run_Click(object? sender, RoutedEventArgs e)
        {
            var target = _hostname?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            KillProcess();
            if (_run != null)
            {
                _run.IsEnabled = false;
            }
            if (_output != null)
            {
                _output.Text = string.Empty;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _tool,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                BuildArguments(startInfo, target);

                _process = Process.Start(startInfo);
                if (_process == null)
                {
                    if (_output != null)
                    {
                        _output.Text = $"Unable to start {_tool}.";
                    }
                    return;
                }

                var stdoutTask = _process.StandardOutput.ReadToEndAsync();
                var stderrTask = _process.StandardError.ReadToEndAsync();
                await Task.WhenAll(stdoutTask, stderrTask);
                await _process.WaitForExitAsync();

                if (_output != null)
                {
                    var text = stdoutTask.Result;
                    if (!string.IsNullOrWhiteSpace(stderrTask.Result))
                    {
                        text += (text.Length > 0 ? Environment.NewLine : string.Empty) + stderrTask.Result;
                    }
                    _output.Text = text;
                }
            }
            catch (Exception ex)
            {
                // Caminho mais provável: utilitário não instalado
                // (Win32Exception "No such file or directory") — ver a
                // dependência bind9-dnsutils em packaging/debian/control.
                if (_output != null)
                {
                    _output.Text = ex.Message;
                }
            }
            finally
            {
                try { _process?.Dispose(); } catch { /* ignora */ }
                _process = null;
                if (_run != null)
                {
                    _run.IsEnabled = true;
                }
                _hostname?.Focus();
            }
        }

        private void KillProcess()
        {
            try
            {
                if (_process is { HasExited: false })
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch { /* processo já pode ter saído sozinho */ }

            try { _process?.Dispose(); } catch { /* ignora */ }
            _process = null;
        }
    }
}
