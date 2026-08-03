using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using vmPing.UI;

namespace vmPing.Classes
{
    class CommandLine
    {
        private const int MinInterval = 1;
        private const int MaxInterval = 86400;
        private const int MinTimeout = 1;
        private const int MaxTimeout = 60;
        private const long MaxHostFileSize = 10 * 1024;

        // [Certo] FASE 4: virou async e passa a receber a janela "owner" (a
        // MainWindow que está chamando isto a partir de Window_Loaded, já
        // visível nesse ponto). Isso resolve o débito da Fase 2: agora
        // --help/-h/-? e erros de parsing mostram UsageWindow/DialogWindow de
        // verdade (Window.ShowDialog(owner), que no Avalonia é Task-based) em
        // vez de só escrever no stderr. O chamador (MainWindow.Window_Loaded)
        // também precisou virar async void para poder dar await aqui.
        public static async Task<List<string>> ParseArguments(Window owner)
        {
            var args = Environment.GetCommandLineArgs();
            var errors = new StringBuilder();
            var hostnames = new List<string>();

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "/i":
                    case "-i":
                        if (i + 1 < args.Length &&
                            int.TryParse(args[i + 1], out int interval) &&
                            interval >= MinInterval && interval <= MaxInterval)
                        {
                            ApplicationOptions.PingInterval = interval * 1000;
                            i++; // Skip over next arg.
                        }
                        else
                        {
                            errors.AppendLine($"-i: Ping interval must be between {MinInterval} and {MaxInterval}.");
                        }
                        break;

                    case "/w":
                    case "-w":
                        if (args.Length > i + 1 &&
                            int.TryParse(args[i + 1], out int timeout) &&
                            timeout >= MinTimeout && timeout <= MaxTimeout)
                        {
                            ApplicationOptions.PingTimeout = timeout * 1000;
                            i++; // Skip over next arg.
                        }
                        else
                        {
                            errors.AppendLine($"-w: Ping timeout must be between {MinTimeout} and {MaxTimeout}.");
                        }
                        break;

                    case "/minimized":
                    case "-minimized":
                        owner.WindowState = WindowState.Minimized;
                        break;

                    case "/?":
                    case "-?":
                    case "-h":
                    case "--help":
                        await ShowHelpDialog(owner);
                        Shutdown();
                        return hostnames;

                    default:
                        // If an argument isn't one of the above options, check to see if it's a file path.
                        // If so, open and read hosts from the file. If not, use the argument as a hostname.
                        if (File.Exists(args[i]))
                        {
                            var fileHosts = await ReadHostsFromFile(owner, args[i]);
                            if (fileHosts == null)
                            {
                                // Fatal error already shown to the user (file too large / unreadable);
                                // ReadHostsFromFile already triggered shutdown.
                                return hostnames;
                            }
                            hostnames.AddRange(fileHosts);
                        }
                        else
                        {
                            hostnames.Add(args[i]);
                        }
                        break;
                }
            }

            // Display error message if any problems were encountered while parsing the arguments.
            if (errors.Length > 0)
            {
                await ShowErrorDialog(owner, errors.ToString().TrimEnd(Environment.NewLine.ToCharArray()));
                Shutdown();
            }

            return hostnames;
        }

        private static async Task<List<string>?> ReadHostsFromFile(Window owner, string path)
        {
            try
            {
                // Check file size.
                long length = new FileInfo(path).Length;
                if (length > MaxHostFileSize)
                {
                    await ShowErrorDialog(owner, $"\"{path}\" is too large. The maximum file size is {MaxHostFileSize / 1024} KB.");
                    Shutdown();
                    return null;
                }

                // Read, validate, and trim each line from the specified file.
                // Valid lines must not be empty and must begin with a letter, digit, or '[' character (for IPv6).
                var validLines = File.ReadAllLines(path)
                    .Where(line =>
                        !string.IsNullOrWhiteSpace(line) &&
                        (char.IsLetterOrDigit(line[0]) || line[0] == '['))
                    .Select(line => line.Trim())
                    .ToList();

                return validLines;
            }
            catch (Exception ex)
            {
                await ShowErrorDialog(owner, $"Unable to parse \"{path}\": {ex.Message}");
                Shutdown();
                return null;
            }
        }

        private static void Shutdown()
            => (Avalonia.Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();

        private static async Task ShowHelpDialog(Window owner)
        {
            var wnd = new UsageWindow();
            await wnd.ShowDialog(owner);
        }

        private static async Task ShowErrorDialog(Window owner, string message)
        {
            await DialogWindow.ErrorWindow(message).ShowDialog(owner);
        }
    }
}
