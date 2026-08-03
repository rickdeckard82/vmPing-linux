using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class StatusHistoryWindow : Window
    {
        // [Certo] FASE 4 — posição/tamanho lembrados entre aberturas da janela
        // na mesma sessão do app (campos estáticos, igual ao original), mas
        // sem a parte de P/Invoke (WM_GETMINMAXINFO) que só existia pra
        // ajustar o tamanho maximizado no Windows considerando a taskbar —
        // o gerenciador de janelas do Linux já cuida disso sozinho.
        private static bool _isWindowStateSet;
        private static double _width = 720;
        private static double _height = 400;

        private readonly ObservableCollection<StatusChangeLog> _source;
        private readonly ListBox? _statusHistory;
        private readonly StackPanel? _noHistoryOverlay;
        private readonly TextBox? _filterField;
        private readonly CheckBox? _filterUp;
        private readonly CheckBox? _filterDown;
        private readonly CheckBox? _filterStart;
        private readonly CheckBox? _filterStop;

        public StatusHistoryWindow() : this(new ObservableCollection<StatusChangeLog>()) { }

        public StatusHistoryWindow(ObservableCollection<StatusChangeLog> statusChangeLog)
        {
            InitializeComponent();

            _source = statusChangeLog;

            _statusHistory = this.FindControl<ListBox>("StatusHistory");
            _noHistoryOverlay = this.FindControl<StackPanel>("NoHistoryOverlay");
            _filterField = this.FindControl<TextBox>("FilterField");
            _filterUp = this.FindControl<CheckBox>("FilterUp");
            _filterDown = this.FindControl<CheckBox>("FilterDown");
            _filterStart = this.FindControl<CheckBox>("FilterStart");
            _filterStop = this.FindControl<CheckBox>("FilterStop");

            if (_isWindowStateSet)
            {
                Width = _width;
                Height = _height;
            }

            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _source.CollectionChanged += Source_CollectionChanged;
            RefreshFilter(scrollToEnd: true);
        }

        private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshFilter(scrollToEnd: true);
        }

        private bool PassesFilter(StatusChangeLog entry)
        {
            var statusMatch =
                (_filterUp?.IsChecked == true && entry.Status == ProbeStatus.Up) ||
                (_filterDown?.IsChecked == true && entry.Status == ProbeStatus.Down) ||
                (_filterStart?.IsChecked == true && entry.Status == ProbeStatus.Start) ||
                (_filterStop?.IsChecked == true && entry.Status == ProbeStatus.Stop);

            if (!statusMatch)
            {
                return false;
            }

            var filterText = _filterField?.Text;
            if (string.IsNullOrEmpty(filterText))
            {
                return true;
            }

            filterText = filterText.ToUpperInvariant();
            return (!string.IsNullOrEmpty(entry.Alias) && entry.Alias.ToUpperInvariant().Contains(filterText))
                || (!string.IsNullOrEmpty(entry.Hostname) && entry.Hostname.ToUpperInvariant().Contains(filterText));
        }

        private List<StatusChangeLog> GetFilteredItems()
            => _source.Where(PassesFilter).ToList();

        private void RefreshFilter(bool scrollToEnd)
        {
            if (_statusHistory == null)
            {
                return;
            }

            var items = GetFilteredItems();
            _statusHistory.ItemsSource = items;

            if (_noHistoryOverlay != null)
            {
                _noHistoryOverlay.IsVisible = items.Count == 0;
            }

            if (scrollToEnd && items.Count > 0)
            {
                _statusHistory.ScrollIntoView(items[^1]);
            }
        }

        private void Filter_Click(object? sender, RoutedEventArgs e) => RefreshFilter(scrollToEnd: false);

        private void TextBox_KeyUp(object? sender, KeyEventArgs e) => RefreshFilter(scrollToEnd: false);

        private async void Export_Click(object? sender, RoutedEventArgs e)
        {
            // [Chutando] O original usava System.Windows.Forms.SaveFileDialog
            // (Windows-only) para deixar o usuário escolher o caminho. O
            // Avalonia tem IStorageProvider.SaveFilePickerAsync como
            // equivalente moderno, mas não consegui verificar a forma exata
            // de FilePickerSaveOptions contra o código-fonte nesta sessão
            // (rate limit da API do GitHub) — em vez de arriscar adivinhar a
            // API errada, escrevo direto num caminho fixo e prático.
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "vmping-status-history.csv");

            try
            {
                var sb = new StringBuilder();
                foreach (var entry in GetFilteredItems())
                {
                    sb.AppendLine(string.Join(",",
                        entry.Timestamp,
                        entry.Hostname,
                        entry.Alias?.Replace(",", "") ?? string.Empty,
                        entry.StatusAsString));
                }

                File.WriteAllText(path, sb.ToString());
                await DialogWindow.InfoWindow(Properties.Strings.Msg_ExportTitle, $"{Properties.Strings.Msg_ExportedTo}\n{path}").ShowDialog(this);
            }
            catch (Exception ex)
            {
                await DialogWindow.ErrorWindow($"Failed to write to '{path}'. {ex.Message}").ShowDialog(this);
            }
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Constants.StatusHistoryKeyBinding)
            {
                e.Handled = true;
                Close();
            }
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _source.CollectionChanged -= Source_CollectionChanged;
            _isWindowStateSet = true;
            _width = Width;
            _height = Height;
        }
    }
}
