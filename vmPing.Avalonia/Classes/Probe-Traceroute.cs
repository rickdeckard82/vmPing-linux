using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace vmPing.Classes
{
    public partial class Probe
    {
        // [Certo] Reescrito — mesma correção aplicada em UI/TraceRouteWindow.axaml.cs
        // (ver docs/PORTING_NOTES.md pro diagnóstico completo, confirmado contra o
        // código-fonte real do dotnet/runtime). Resumo: System.Net.NetworkInformation.
        // Ping com PingOptions.Ttl não funciona pra traceroute no Linux — o socket
        // ICMP raw é conectado (socket.Connect()) ao destino "to scope responses
        // only to the target address", e um socket conectado no Linux descarta
        // qualquer resposta ICMP Time Exceeded vinda de um roteador intermediário
        // (origem diferente do destino). Resultado: todo hop que não fosse o
        // próprio destino aparecia como TimedOut, mesmo com a rede respondendo
        // normalmente — não é bug do port, é limitação real da API .NET nesse
        // cenário. Trocado por chamar o utilitário `traceroute` do sistema
        // operacional (mesmo padrão já usado em Probe-Util.cs pro áudio) e
        // parsear a saída linha a linha.
        private static readonly Regex HopLine = new(
            @"^\s*(?<hop>\d+)\s+(?:(?<name>\S+)\s+\((?<ip>[\da-fA-F:.]+)\)(?:\s+(?<rtt>[\d.]+)\s*ms)?|\*)",
            RegexOptions.Compiled);

        private async void PerformTraceroute(CancellationToken cancellationToken)
        {
            IsActive = true;
            History = new ObservableCollection<string>();
            Status = ProbeStatus.Scanner;

            AddHistory($"[•] Tracing route to {Hostname}:");
            if (await IsHostInvalid(Hostname, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                StopProbe(ProbeStatus.Error);
                return;
            }
            AddHistory("");

            // [Certo] FASE 4/correção — pedido do usuário: colorir o probe de
            // verde quando o trace chega no destino, vermelho quando não chega
            // (ou dá erro), igual ao que já acontece com ping/tcp. Pra saber
            // se "chegou no destino", comparo o IP do último hop reportado
            // contra o IP de destino resolvido aqui (o traceroute não imprime
            // nenhum indicador explícito de sucesso/falha — só para de
            // imprimir linhas, seja porque chegou ou porque esgotou os hops).
            var destinationIp = await ResolveDestinationIp(Hostname);
            string? lastHopIp = null;

            Process? process = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "traceroute",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                // -q 1: uma sonda por hop. -w 2: timeout de 2s por sonda, igual
                // ao Timeout=2000 que o código anterior usava com Ping.Send.
                // Sem -n: deixa o traceroute resolver DNS reverso.
                startInfo.ArgumentList.Add("-q");
                startInfo.ArgumentList.Add("1");
                startInfo.ArgumentList.Add("-w");
                startInfo.ArgumentList.Add("2");
                startInfo.ArgumentList.Add(Hostname);

                process = Process.Start(startInfo);
                if (process == null)
                {
                    AddHistory("Unable to start traceroute.");
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Status = ProbeStatus.Down;
                    }
                    return;
                }

                using (cancellationToken.Register(() => TryKill(process)))
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        var hop = ParseHopLine(line);
                        if (hop == null)
                        {
                            continue;
                        }

                        AddHistory(hop.Value.Text);
                        if (hop.Value.Ip != null)
                        {
                            lastHopIp = hop.Value.Ip;
                        }
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    var reachedDestination = destinationIp != null && lastHopIp == destinationIp;
                    AddHistory($"{Environment.NewLine}★ Trace complete");
                    Status = reachedDestination ? ProbeStatus.Up : ProbeStatus.Down;
                }
            }
            catch (Exception ex)
            {
                // Caminho mais provável de cair aqui: o pacote `traceroute` não
                // está instalado (ver dependência em packaging/debian/control).
                AddHistory(ex.Message);
                if (!cancellationToken.IsCancellationRequested)
                {
                    Status = ProbeStatus.Down;
                }
            }
            finally
            {
                try { process?.Dispose(); } catch { /* ignora */ }
                IsActive = false;
            }
        }

        private static async Task<string?> ResolveDestinationIp(string host)
        {
            if (IPAddress.TryParse(host, out var parsed))
            {
                return parsed.ToString();
            }

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                return addresses.Length > 0 ? addresses[0].ToString() : null;
            }
            catch
            {
                // Já validado por IsHostInvalid antes de chegar aqui; se isso
                // falhar mesmo assim, só não temos como confirmar "chegou no
                // destino" — o trace continua rodando normalmente, só não
                // acende verde no final.
                return null;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { /* processo já pode ter saído sozinho */ }
        }

        // Formato de linha do `traceroute -q 1`:
        //   " 1  _gateway (172.16.0.65)  2.934 ms"
        //   " 8  dns.google (8.8.8.8)  7.549 ms"
        //   " 3  *"                                (timeout)
        private static (string Text, string? Ip)? ParseHopLine(string line)
        {
            var match = HopLine.Match(line);
            if (!match.Success)
            {
                return null;
            }

            var hop = match.Groups["hop"].Value;

            if (!match.Groups["ip"].Success)
            {
                return (string.Format("{0,2}   {1}", hop, "Timed out"), null);
            }

            var name = match.Groups["name"].Value;
            var ip = match.Groups["ip"].Value;
            var host = name == ip ? ip : $"{name} ({ip})";

            var text = match.Groups["rtt"].Success
                ? string.Format("{0,2}   {1,-32}   [{2} ms]", hop, host, match.Groups["rtt"].Value)
                : string.Format("{0,2}   {1}", hop, host);

            return (text, ip);
        }
    }
}
