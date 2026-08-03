using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using vmPing.Classes;

namespace vmPing.UI
{
    // Seletor de arquivos/pastas próprio — ver o porquê no cabeçalho do .axaml.
    // Só usa System.IO: nenhuma dependência de D-Bus, portal ou toolkit nativo,
    // que é exatamente o ponto (o nativo é que está quebrado no ambiente alvo).
    public partial class FileBrowserWindow : Window
    {
        private readonly bool _foldersOnly;
        private readonly string[] _extensions;
        private readonly TextBox? _pathBox;
        private readonly ListBox? _entries;
        private readonly TextBlock? _selectionInfo;

        private string _currentDir = "/";

        // Item da lista: guarda o caminho real e se é diretório, para não ter
        // que reconstruir/adivinhar a partir do texto exibido.
        private sealed class Entry
        {
            public required string Display { get; init; }
            public required string FullPath { get; init; }
            public required bool IsDirectory { get; init; }
            public override string ToString() => Display;
        }

        public FileBrowserWindow() : this(foldersOnly: false, initialPath: null, extensions: null) { }

        public FileBrowserWindow(bool foldersOnly, string? initialPath, string[]? extensions)
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _foldersOnly = foldersOnly;
            _extensions = extensions ?? Array.Empty<string>();

            _pathBox = this.FindControl<TextBox>("PathBox");
            _entries = this.FindControl<ListBox>("Entries");
            _selectionInfo = this.FindControl<TextBlock>("SelectionInfo");

            Title = foldersOnly
                ? Properties.Strings.FileBrowser_TitleFolder
                : Properties.Strings.FileBrowser_Title;

            Navigate(ResolveStartDirectory(initialPath));
        }

        // Ponto de partida: a pasta do caminho já preenchido, se existir; senão
        // a pasta pessoal; e em último caso a raiz (sempre existe).
        private static string ResolveStartDirectory(string? initialPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(initialPath))
                {
                    if (Directory.Exists(initialPath))
                    {
                        return initialPath;
                    }
                    var parent = Path.GetDirectoryName(initialPath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        return parent;
                    }
                }

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
                {
                    return home;
                }
            }
            catch
            {
                // Qualquer falha de acesso cai na raiz abaixo.
            }

            return "/";
        }

        private void Navigate(string dir)
        {
            List<Entry> items = new();
            try
            {
                // Diretórios primeiro, depois arquivos — ambos em ordem
                // alfabética sem diferenciar maiúsculas, como em qualquer
                // gerenciador de arquivos.
                foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (Path.GetFileName(d).StartsWith('.')) { continue; }
                    items.Add(new Entry
                    {
                        Display = "[ " + Path.GetFileName(d) + " ]",
                        FullPath = d,
                        IsDirectory = true,
                    });
                }

                if (!_foldersOnly)
                {
                    foreach (var f in Directory.GetFiles(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        var name = Path.GetFileName(f);
                        if (name.StartsWith('.')) { continue; }
                        if (_extensions.Length > 0 &&
                            !_extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        items.Add(new Entry { Display = name, FullPath = f, IsDirectory = false });
                    }
                }

                _currentDir = dir;
            }
            catch (Exception ex)
            {
                // Pasta sem permissão de leitura: mostra o motivo na própria
                // lista e mantém o diretório anterior, em vez de fechar ou
                // engolir o erro.
                items.Add(new Entry
                {
                    Display = $"({ex.Message})",
                    FullPath = _currentDir,
                    IsDirectory = false,
                });
            }

            if (_pathBox != null) { _pathBox.Text = _currentDir; }
            if (_entries != null) { _entries.ItemsSource = items; }
            UpdateSelectionInfo();
        }

        private void UpdateSelectionInfo()
        {
            if (_selectionInfo == null) { return; }

            if (_foldersOnly)
            {
                _selectionInfo.Text = _currentDir;
                return;
            }

            _selectionInfo.Text = _entries?.SelectedItem is Entry { IsDirectory: false } e
                ? e.FullPath
                : string.Empty;
        }

        private void Entries_SelectionChanged(object? sender, SelectionChangedEventArgs e)
            => UpdateSelectionInfo();

        private void Entries_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_entries?.SelectedItem is not Entry entry) { return; }

            if (entry.IsDirectory)
            {
                Navigate(entry.FullPath);
            }
            else if (!_foldersOnly)
            {
                Close(entry.FullPath);   // duplo clique num arquivo = escolher
            }
        }

        private void Up_Click(object? sender, RoutedEventArgs e)
        {
            var parent = Path.GetDirectoryName(_currentDir);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                Navigate(parent);
            }
        }

        private void Go_Click(object? sender, RoutedEventArgs e) => NavigateToTypedPath();

        private void PathBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;   // impede que o Enter acione o botão OK
                NavigateToTypedPath();
            }
        }

        private void NavigateToTypedPath()
        {
            var typed = _pathBox?.Text?.Trim();
            if (!string.IsNullOrEmpty(typed) && Directory.Exists(typed))
            {
                Navigate(typed);
            }
        }

        private void Ok_Click(object? sender, RoutedEventArgs e)
        {
            if (_foldersOnly)
            {
                Close(_currentDir);
                return;
            }

            if (_entries?.SelectedItem is Entry { IsDirectory: false } entry)
            {
                Close(entry.FullPath);
            }
            // Sem arquivo selecionado: não fecha — evita devolver caminho vazio
            // e obrigar quem chamou a tratar mais um caso.
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
    }
}
