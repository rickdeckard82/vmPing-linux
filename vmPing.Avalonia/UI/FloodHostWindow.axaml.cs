using System;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class FloodHostWindow : Window
    {
        // [Certo] FASE 4 — mesma lógica do original: BackgroundWorker enviando
        // ping em loop apertado (sem Task.Delay entre pacotes, só o timeout
        // curto de 100ms do próprio Ping.Send e o custo real da rede). Igual
        // a TraceRouteWindow, BackgroundWorker já é cross-platform desde o
        // .NET Core 3.0 — não precisou de reescrita pra async/Task.
        private readonly FloodHostNode _floodHost = new();

        private readonly TextBox? _hostname;
        private readonly Border? _informationOverlay;

        public FloodHostWindow()
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            DataContext = _floodHost;

            _hostname = this.FindControl<TextBox>("Hostname");
            _informationOverlay = this.FindControl<Border>("InformationOverlay");

            Opened += (_, _) => _hostname?.Focus();
        }

        private void FloodHost_Click(object? sender, RoutedEventArgs e)
        {
            if (_informationOverlay != null)
            {
                _informationOverlay.IsVisible = false;
            }

            ToggleFlood(_floodHost);
        }

        // [Certo] Rodada de correção via build real — CS0102: o Button
        // Name="FloodHost" no XAML gera um campo `FloodHost` no partial class
        // (Avalonia.Generators.NameGenerator), colidindo com um método de
        // mesmo nome. Renomeado o método; o Name do botão ficou como estava
        // (mais descritivo no XAML).
        public void ToggleFlood(FloodHostNode node)
        {
            if (!node.IsActive)
            {
                if (string.IsNullOrEmpty(_hostname?.Text))
                {
                    return;
                }

                node.BgWorker?.CancelAsync();

                node.DestinationAddress = _hostname.Text;
                node.PacketsSent = 0;
                node.PacketsReceived = 0;
                node.PacketsLost = 0;
                node.StartTime = DateTime.Now;
                node.IsActive = true;

                node.BgWorker = new BackgroundWorker();
                node.ResetEvent = new AutoResetEvent(false);
                node.BgWorker.DoWork += BackgroundThread_FloodHost;
                node.BgWorker.WorkerSupportsCancellation = true;
                node.BgWorker.RunWorkerAsync(node);
            }
            else
            {
                node.BgWorker?.CancelAsync();
                node.ResetEvent?.WaitOne();
                node.IsActive = false;
            }
        }

        public void BackgroundThread_FloodHost(object? sender, DoWorkEventArgs e)
        {
            var bgWorker = sender as BackgroundWorker;
            var node = e.Argument as FloodHostNode;

            if (node == null)
            {
                return;
            }

            var pingBuffer = Encoding.ASCII.GetBytes(Constants.DefaultIcmpData);
            var pingOptions = new PingOptions(Constants.DefaultTTL, true);

            while (bgWorker != null && !bgWorker.CancellationPending && node.IsActive)
            {
                using (var ping = new Ping())
                {
                    try
                    {
                        ++node.PacketsSent;
                        if (ping.Send(node.DestinationAddress, 100, pingBuffer, pingOptions).Status == IPStatus.Success)
                        {
                            ++node.PacketsReceived;
                        }
                        else
                        {
                            ++node.PacketsLost;
                        }

                        node.ResetEvent?.Set();
                    }
                    catch
                    {
                        e.Cancel = true;
                        node.ResetEvent?.Set();
                        node.IsActive = false;
                        return;
                    }
                }
            }

            node.ResetEvent?.Set();
        }
    }
}
