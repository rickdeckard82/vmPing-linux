using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace vmPing.Classes
{
    public partial class Probe
    {
        // [Certo] Application.Current.Dispatcher (WPF) -> Avalonia.Threading.Dispatcher.UIThread.
        // BeginInvoke awaitável (linha original "await ...BeginInvoke(...)") vira
        // InvokeAsync; a chamada "fire-and-forget" (sem await) em DisplayIcmpReply
        // vira Dispatcher.UIThread.Post, preservando o comportamento original de
        // não bloquear o probe loop esperando a UI atualizar.
        private async void PerformIcmpProbe(CancellationToken cancellationToken)
        {
            InitializeProbe();

            await Dispatcher.UIThread.InvokeAsync(
                new Action(() => AddHistory($"*** {Properties.Strings.Probe_StartPing} {Hostname}:")));

            if (await IsHostInvalid(Hostname, cancellationToken))
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    StopProbe(ProbeStatus.Error);
                }
                return;
            }

            using (var ping = new Ping())
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Send ping.
                        Statistics.Sent++;
                        var reply = await ping.SendPingAsync(
                            hostNameOrAddress: Hostname,
                            timeout: ApplicationOptions.PingTimeout,
                            buffer: ApplicationOptions.Buffer,
                            options: ApplicationOptions.PingOptions);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        // Reply received.
                        if (reply.Status == IPStatus.Success)
                        {
                            Statistics.Received++;
                            IndeterminateCount = 0;

                            // If this is a new probe, record the initial 'up' state to the status history.
                            if (Status == ProbeStatus.Inactive)
                            {
                                AddStatusHistory(ProbeStatus.Up, true);
                                Status = ProbeStatus.Up;
                            }

                            // Check if status changed from down to up.
                            if (Status == ProbeStatus.Down)
                            {
                                OnStatusChange(ProbeStatus.Up, "up");
                            }

                            // Update minimum RTT.
                            if (reply.RoundtripTime < MinRtt)
                            {
                                MinRtt = reply.RoundtripTime;
                            }

                            // Check latency.
                            if ((ApplicationOptions.LatencyDetectionMode == ApplicationOptions.LatencyMode.Fixed &&
                                reply.RoundtripTime >= ApplicationOptions.HighLatencyMilliseconds) ||
                                (ApplicationOptions.LatencyDetectionMode == ApplicationOptions.LatencyMode.Auto &&
                                reply.RoundtripTime >= MinRtt + ApplicationOptions.HighLatencyMilliseconds))
                            {
                                // Latency is high.
                                if (HighLatencyCount < ApplicationOptions.HighLatencyAlertTiggerCount)
                                {
                                    HighLatencyCount++;
                                }

                                if (Status == ProbeStatus.Up)
                                {
                                    Status = ProbeStatus.Indeterminate;
                                }

                                if (Status != ProbeStatus.LatencyHigh)
                                {
                                    if (HighLatencyCount >= ApplicationOptions.HighLatencyAlertTiggerCount)
                                    {
                                        OnStatusChange(ProbeStatus.LatencyHigh, "high latency");
                                    }
                                }
                            }
                            else
                            {
                                // Latency is normal.
                                if (HighLatencyCount > 0)
                                {
                                    HighLatencyCount--;
                                }

                                if (Status == ProbeStatus.LatencyHigh)
                                {
                                    if (HighLatencyCount <= 0)
                                    {
                                        OnStatusChange(ProbeStatus.LatencyNormal, "normal latency");
                                        Status = ProbeStatus.Up;
                                    }
                                }
                                else
                                {
                                    Status = ProbeStatus.Up;
                                }
                            }
                        }
                        // No reply received.
                        else
                        {
                            Statistics.Lost++;
                            IndeterminateCount++;

                            if (Status == ProbeStatus.Up || Status == ProbeStatus.LatencyHigh)
                            {
                                Status = ProbeStatus.Indeterminate;
                            }
                            else if (Status == ProbeStatus.Inactive)
                            {
                                // Because this is a new probe, ignore the indeterminate count
                                // and immediately mark the host as down.
                                // Also, record the initial 'down' state to the status history.
                                AddStatusHistory(ProbeStatus.Down, true);
                                Status = ProbeStatus.Down;
                            }

                            // Check for status change.
                            if (Status == ProbeStatus.Indeterminate &&
                                IndeterminateCount >= ApplicationOptions.AlertThreshold)
                            {
                                Status = ProbeStatus.Down;
                                OnStatusChange(ProbeStatus.Down, "down");
                            }
                        }

                        if (!cancellationToken.IsCancellationRequested)
                        {
                            // Update output.
                            DisplayStatistics();
                            DisplayIcmpReply(reply);

                            // Pause between probes.
                            await IcmpWait(reply.Status);
                        }
                    }

                    catch (Exception ex)
                    {
                        Statistics.Lost++;

                        // Check for status change.
                        if (Status == ProbeStatus.Inactive)
                        {
                            Status = ProbeStatus.Down;
                        }

                        if (Status != ProbeStatus.Down)
                        {
                            Status = ProbeStatus.Down;
                            OnStatusChange(ProbeStatus.Down, "error");
                        }

                        // Update output.
                        DisplayStatistics();
                        DisplayIcmpReply(null, ex);

                        // Pause between probes.
                        await Task.Delay(ApplicationOptions.PingInterval);
                    }
                }
            }
        }

        private async Task IcmpWait(IPStatus ipStatus)
        {
            if (ipStatus == IPStatus.TimedOut)
            {
                // Ping timed out. If the ping interval is greater than the timeout,
                // then sleep for [INTERVAL - TIMEOUT]
                // Otherwise, sleep for a fixed amount of 1 second
                if (ApplicationOptions.PingInterval > ApplicationOptions.PingTimeout)
                {
                    await Task.Delay(ApplicationOptions.PingInterval - ApplicationOptions.PingTimeout);
                }
                else
                {
                    await Task.Delay(1000);
                }
            }
            else
            {
                // For any other type of ping response, sleep for the global ping interval amount
                // before sending another ping.
                await Task.Delay(ApplicationOptions.PingInterval);
            }
        }

        private void DisplayIcmpReply(PingReply pingReply, Exception? ex = null)
        {
            if (pingReply == null && ex == null)
            {
                return;
            }

            // Build output string based on the ping reply details.
            var sb = new StringBuilder($"[{DateTime.Now.ToLongTimeString()}]  ");

            if (pingReply != null)
            {
                // i18n (FASE 5): strings hardcoded trocadas pelas chaves do resx
                // que o original já tinha (Probe_Reply, Probe_Timeout, etc.) —
                // o port tinha hardcodado em vez de usá-las.
                switch (pingReply.Status)
                {
                    case IPStatus.Success:
                        sb.Append(Properties.Strings.Probe_Reply);
                        sb.Append(' ');
                        sb.Append(pingReply.Address.ToString());
                        sb.Append(pingReply.RoundtripTime < 1
                            ? $"  [<1{Properties.Strings.Milliseconds_Symbol}]"
                            : $"  [{pingReply.RoundtripTime} {Properties.Strings.Milliseconds_Symbol}]");
                        break;
                    case IPStatus.DestinationHostUnreachable:
                        sb.Append(Properties.Strings.Probe_HostUnreachable);
                        break;
                    case IPStatus.DestinationNetworkUnreachable:
                        sb.Append(Properties.Strings.Probe_NetUnreachable);
                        break;
                    case IPStatus.DestinationUnreachable:
                        sb.Append(Properties.Strings.Probe_Unreachable);
                        break;
                    case IPStatus.TimedOut:
                        sb.Append(Properties.Strings.Probe_Timeout);
                        break;
                    default:
                        sb.Append(pingReply.Status.ToString());
                        break;
                }
            }
            else
            {
                sb.Append(ex.InnerException is SocketException
                    ? Properties.Strings.Probe_UnableToResolve
                    : ex.Message);
            }

            var output = sb.ToString();

            // Add response to the output window..
            Dispatcher.UIThread.Post(() => AddHistory(output));

            // If enabled, log output.
            WriteToLog(output);
        }
    }
}
