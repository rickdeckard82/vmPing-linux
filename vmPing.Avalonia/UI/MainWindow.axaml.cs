using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using vmPing.Classes;

namespace vmPing.UI
{
    // FASE 3 — port funcional-primeiro de UI/MainWindow.xaml.cs (847 linhas
    // originais). Ver comentário de cabeçalho em MainWindow.axaml para o que
    // foi simplificado. Toda lógica de negócio (probes, favoritos, aliases,
    // bandeja, atalhos) foi portada; estilo visual fica para a Fase 5.
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<Probe> _probeCollection = new();
        private Dictionary<string, string> _aliases = new();
        private TrayIcon? _trayIcon;

        // Controles nomeados, resolvidos uma vez após InitializeComponent.
        private Slider? _columnCount;
        private TextBlock? _startStopMenuHeader;
        private MenuItem? _popupNever;
        private MenuItem? _popupWhenMinimized;
        private MenuItem? _popupAlways;
        private MenuItem? _favoritesMenu;
        private MenuItem? _aliasesMenu;
        private ItemsControl? _probeItemsControl;

        public MainWindow()
        {
            InitializeComponent();
            ResolveNamedControls();
            SetupTrayIcon();
            InitializeApplication();

            this.PropertyChanged += MainWindow_PropertyChanged;
        }

        private void ResolveNamedControls()
        {
            _columnCount = this.FindControl<Slider>("ColumnCount");
            _startStopMenuHeader = this.FindControl<TextBlock>("StartStopMenuHeader");
            _popupNever = this.FindControl<MenuItem>("PopupNever");
            _popupWhenMinimized = this.FindControl<MenuItem>("PopupWhenMinimized");
            _popupAlways = this.FindControl<MenuItem>("PopupAlways");
            _favoritesMenu = this.FindControl<MenuItem>("FavoritesMenu");
            _aliasesMenu = this.FindControl<MenuItem>("AliasesMenu");
            _probeItemsControl = this.FindControl<ItemsControl>("ProbeItemsControl");
        }

        private void InitializeApplication()
        {
            LoadFavorites();
            LoadAliases();
            Configuration.Load();
            RefreshGuiState();

            if (_probeItemsControl != null)
            {
                _probeItemsControl.ItemsSource = _probeCollection;

                // FASE 5 (rodada 2) — drag-and-drop pra reordenar probes.
                // [Certo] DragDrop.DropEvent/DragOverEvent são eventos roteados
                // (bubbling): um único AddHandler no ItemsControl cobre todos os
                // itens do template, dispensando ligar handler item a item (o
                // Avalonia não suporta ligar evento attached por atributo XAML,
                // diferente do WPF onde o original usava Drop="Probe_Drop" direto
                // no elemento). AllowDrop fica no Border raiz de cada item (XAML).
                _probeItemsControl.AddHandler(DragDrop.DragOverEvent, Probe_DragOver);
                _probeItemsControl.AddHandler(DragDrop.DropEvent, Probe_Drop);
            }

            Probe.ActiveCountChanged += (_, _) => Dispatcher.UIThread.Post(RefreshStartStopHeader);
            RefreshStartStopHeader();

            // Atalhos de teclado que não têm MenuItem clicável equivalente já
            // cobrindo o mesmo Gesture (os outros ficam só no InputGesture do
            // próprio MenuItem, que o Avalonia já trata automaticamente).
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.F5),
                Command = new RelayCommand(StartStopAll)
            });
        }

        // [Certo] WPF Window_Loaded == Avalonia Loaded (mesmo timing: layout
        // pronto, ainda não necessariamente exibido na tela). Virou "async
        // void" na Fase 4 porque CommandLine.ParseArguments agora pode abrir
        // UsageWindow/DialogWindow de verdade (ShowDialog Task-based) para
        // --help e erros de argumentos, em vez do fallback em stderr da Fase 2.
        private async void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            if (_columnCount != null)
            {
                _columnCount.Value = ApplicationOptions.InitialColumnCount > 0
                    ? ApplicationOptions.InitialColumnCount
                    : 2;
            }

            List<string> cliHosts = await CommandLine.ParseArguments(this);

            if (cliHosts.Count > 0)
            {
                AddProbe(cliHosts.Count);
                for (int i = 0; i < cliHosts.Count; ++i)
                {
                    _probeCollection[i].Hostname = cliHosts[i];
                    _probeCollection[i].Alias = LookupAlias(_probeCollection[i].Hostname);
                    _probeCollection[i].StartStop();
                }
            }
            else
            {
                AddProbe(ApplicationOptions.InitialProbeCount > 0 ? ApplicationOptions.InitialProbeCount : 2);

                switch (ApplicationOptions.InitialStartMode)
                {
                    case ApplicationOptions.StartMode.MultiInput:
                        RefreshColumnCount();
                        _ = MultiInputWindowCore(null, null);
                        break;
                    case ApplicationOptions.StartMode.Favorite:
                        if (!string.IsNullOrWhiteSpace(ApplicationOptions.InitialFavorite))
                        {
                            LoadFavorite(ApplicationOptions.InitialFavorite!);
                        }
                        break;
                }
            }

            RefreshColumnCount();
        }

        // [Provável] Equivalente ao WPF ContentRendered (foco inicial após o
        // primeiro layout completo). Avalonia não tem ContentRendered; Opened é
        // a aproximação mais próxima disponível.
        private void Window_Opened(object? sender, EventArgs e)
        {
            if (_probeCollection.Count > 0)
            {
                FocusHostnameAt(0);
            }
        }

        private string? LookupAlias(string? hostname)
        {
            if (string.IsNullOrEmpty(hostname))
            {
                return null;
            }
            return _aliases.TryGetValue(hostname.ToLowerInvariant(), out var alias) ? alias : null;
        }

        private void RefreshGuiState()
        {
            if (_popupAlways != null) _popupAlways.IsChecked = false;
            if (_popupNever != null) _popupNever.IsChecked = false;
            if (_popupWhenMinimized != null) _popupWhenMinimized.IsChecked = false;

            switch (ApplicationOptions.PopupOption)
            {
                case ApplicationOptions.PopupNotificationOption.Always:
                    if (_popupAlways != null) _popupAlways.IsChecked = true;
                    break;
                case ApplicationOptions.PopupNotificationOption.Never:
                    if (_popupNever != null) _popupNever.IsChecked = true;
                    break;
                case ApplicationOptions.PopupNotificationOption.WhenMinimized:
                    if (_popupWhenMinimized != null) _popupWhenMinimized.IsChecked = true;
                    break;
            }

            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            // [Certo] FASE 4 — StatusHistoryWindow/HelpWindow/IsolatedPingWindow já
            // replicam Topmost = ApplicationOptions.IsAlwaysOnTopEnabled em seus
            // próprios construtores desde que deixaram de ser stub.
        }

        private void RefreshColumnCount()
        {
            if (_columnCount == null) return;
            _columnCount.Tag = _columnCount.Value > _probeCollection.Count
                ? _probeCollection.Count
                : (int)_columnCount.Value;
        }

        private void RefreshStartStopHeader()
        {
            if (_startStopMenuHeader != null)
            {
                _startStopMenuHeader.Text = Probe.ActiveCount > 0
                    ? Properties.Strings.Toolbar_StopAll
                    : Properties.Strings.Toolbar_StartAll;
            }
        }

        public void AddProbe(int numberOfProbes = 1)
        {
            for (; numberOfProbes > 0; --numberOfProbes)
            {
                _probeCollection.Add(new Probe());
            }
        }

        public void ProbeStartStop_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Probe probe })
            {
                probe.StartStop();
            }
        }

        private void ColumnCount_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            RefreshColumnCount();
        }

        private void Hostname_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox { DataContext: Probe probe })
            {
                return;
            }

            probe.StartStop();

            var index = _probeCollection.IndexOf(probe);
            if (index >= 0 && index < _probeCollection.Count - 1)
            {
                FocusHostnameAt(index + 1);
            }
        }

        // [Provável] Substitui o ItemContainerGenerator.ContainerFromIndex do WPF.
        // ItemsControl.ContainerFromIndex existe no Avalonia 11.x; combinado com
        // o extension method GetChildren (Classes/ApplicationOptions.cs, Fase 2)
        // para achar a TextBox "Hostname" dentro do container.
        private void FocusHostnameAt(int index)
        {
            if (_probeItemsControl == null) return;

            Dispatcher.UIThread.Post(() =>
            {
                var container = _probeItemsControl.ContainerFromIndex(index);
                if (container == null) return;

                var textBox = container.GetChildren(recurse: true)
                    .OfType<TextBox>()
                    .FirstOrDefault(tb => tb.Name == "Hostname");
                textBox?.Focus();
            }, DispatcherPriority.Background);
        }

        private void RemoveProbe_Click(object? sender, RoutedEventArgs e)
        {
            if (_probeCollection.Count <= 1) return;
            if (sender is not Button { DataContext: Probe probe }) return;

            if (probe.IsActive)
            {
                probe.StartStop();
            }
            _probeCollection.Remove(probe);
            RefreshColumnCount();
        }

        // [Certo] Renomeado para MultiInputWindowCore para não colidir com o
        // handler de Click abaixo: `RoutedEventArgs` e `RoutedEventArgs?` são o
        // mesmo tipo em tempo de execução (anotação de nulidade não conta para
        // overload resolution), então "MultiInputWindowExecute(object?,
        // RoutedEventArgs?)" e "MultiInputWindowExecute(object?, RoutedEventArgs)"
        // seriam a mesma assinatura — erro de compilação (membro duplicado).
        private async System.Threading.Tasks.Task MultiInputWindowCore(object? sender, RoutedEventArgs? e)
        {
            var addresses = _probeCollection
                .Select(p => p.Hostname)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => h!.Trim())
                .ToList();

            var wnd = new MultiInputWindow(addresses);
            var result = await wnd.ShowDialog<bool>(this);
            if (!result) return;

            RemoveAllProbes();

            if (wnd.Addresses.Count < 1)
            {
                AddProbe();
            }
            else
            {
                AddProbe(wnd.Addresses.Count);
                for (int i = 0; i < wnd.Addresses.Count; ++i)
                {
                    _probeCollection[i].Hostname = wnd.Addresses[i];
                    _probeCollection[i].Alias = LookupAlias(_probeCollection[i].Hostname);
                    _probeCollection[i].StartStop();
                }
            }

            if (_columnCount != null)
            {
                double count = _columnCount.Value;
                _columnCount.Value = 1;
                _columnCount.Value = count;
            }
        }

        private async void MultiInputWindowExecute(object? sender, RoutedEventArgs e) =>
            await MultiInputWindowCore(sender, e);

        private void StartStopAll()
        {
            bool anyActive = Probe.ActiveCount > 0;
            foreach (var probe in _probeCollection)
            {
                if (anyActive && probe.IsActive)
                {
                    probe.StartStop();
                }
                else if (!anyActive && !probe.IsActive)
                {
                    probe.StartStop();
                }
            }
        }

        private void StartStopExecute(object? sender, RoutedEventArgs e) => StartStopAll();

        private void HelpExecute(object? sender, RoutedEventArgs e)
        {
            if (HelpWindow._OpenWindow == null)
            {
                new HelpWindow().Show();
            }
            else
            {
                HelpWindow._OpenWindow.Activate();
            }
        }

        private void NewInstanceExecute(object? sender, RoutedEventArgs e)
        {
            try
            {
                var p = new System.Diagnostics.Process();
                p.StartInfo.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location;
                p.Start();
            }
            catch (Exception ex)
            {
                Util.ShowError($"{Properties.Strings.Error_FailedToLaunch} {ex.Message}");
            }
        }

        private void TracerouteExecute(object? sender, RoutedEventArgs e) => new TraceRouteWindow().Show();

        private void NslookupExecute(object? sender, RoutedEventArgs e) => new DnsLookupWindow("nslookup").Show();

        private void DigExecute(object? sender, RoutedEventArgs e) => new DnsLookupWindow("dig").Show();

        private void FloodHostExecute(object? sender, RoutedEventArgs e) => new FloodHostWindow().Show();

        private void AddProbeExecute(object? sender, RoutedEventArgs e)
        {
            _probeCollection.Add(new Probe());
            RefreshColumnCount();
        }

        private async void OptionsExecute(object? sender, RoutedEventArgs e)
        {
            var optionsWnd = new OptionsWindow();
            var result = await optionsWnd.ShowDialog<bool>(this);
            if (result)
            {
                RefreshGuiState();
                RefreshProbeColors();
            }
        }

        // [Certo] Cópia fiel do original: `_ProbeCollection[i].Status = _ProbeCollection[i].Status;`.
        // Como o setter de Status (Classes/Probe.cs) só dispara PropertyChanged
        // quando o valor muda (`if (value != status)`), isto é um no-op tanto
        // aqui quanto no WPF original — não inventei um "fix" porque isso
        // mudaria o comportamento observável em relação ao app original. Hoje
        // é irrelevante de qualquer forma: OptionsWindow ainda é stub (Fase 4)
        // e nunca retorna true, então este método não é chamado na prática.
        private void RefreshProbeColors()
        {
            foreach (var probe in _probeCollection)
            {
                probe.Status = probe.Status;
            }
        }

        private void RemoveAllProbes()
        {
            foreach (var probe in _probeCollection)
            {
                if (probe.IsActive)
                {
                    probe.StartStop();
                }
            }
            _probeCollection.Clear();
            Probe.ActiveCount = 0;
        }

        private void LoadFavorites()
        {
            if (_favoritesMenu == null) return;

            while (_favoritesMenu.Items.Count > 3)
            {
                _favoritesMenu.Items.RemoveAt(_favoritesMenu.Items.Count - 1);
            }

            foreach (var fav in Favorite.GetTitles())
            {
                var menuItem = new MenuItem { Header = fav };
                menuItem.Click += (_, _) => LoadFavorite(fav);
                _favoritesMenu.Items.Add(menuItem);
            }
        }

        private void LoadFavorite(string favoriteTitle)
        {
            RemoveAllProbes();

            var favorite = Favorite.Load(favoriteTitle);
            if (favorite.Hostnames.Count < 1)
            {
                AddProbe();
            }
            else
            {
                AddProbe(favorite.Hostnames.Count);
                for (int i = 0; i < favorite.Hostnames.Count; ++i)
                {
                    _probeCollection[i].Hostname = favorite.Hostnames[i];
                    _probeCollection[i].Alias = LookupAlias(_probeCollection[i].Hostname);
                    _probeCollection[i].StartStop();
                }
            }

            if (_columnCount != null)
            {
                _columnCount.Value = 1;
                _columnCount.Value = favorite.ColumnCount;
            }
            Title = $"{favoriteTitle} - vmPing";
        }

        private void LoadAliases()
        {
            _aliases = Alias.GetAll();

            if (_aliasesMenu != null)
            {
                while (_aliasesMenu.Items.Count > 2)
                {
                    _aliasesMenu.Items.RemoveAt(_aliasesMenu.Items.Count - 1);
                }

                foreach (var alias in _aliases.OrderBy(a => a.Value))
                {
                    var menuItem = new MenuItem { Header = alias.Value };
                    var hostname = alias.Key;
                    menuItem.Click += (_, _) => AssignAliasToEmptyOrNewProbe(hostname);
                    _aliasesMenu.Items.Add(menuItem);
                }
            }

            foreach (var probe in _probeCollection)
            {
                probe.Alias = LookupAlias(probe.Hostname) ?? string.Empty;
            }
        }

        private void AssignAliasToEmptyOrNewProbe(string hostname)
        {
            foreach (var probe in _probeCollection)
            {
                if (string.IsNullOrWhiteSpace(probe.Hostname))
                {
                    probe.Hostname = hostname;
                    probe.StartStop();
                    return;
                }
            }

            AddProbe();
            _probeCollection[^1].Hostname = hostname;
            _probeCollection[^1].StartStop();
        }

        private async void CreateFavorite_Click(object? sender, RoutedEventArgs e)
        {
            const string favTitle = " - vmPing";
            var newFavoriteWindow = new NewFavoriteWindow(
                hostList: _probeCollection.Select(x => x.Hostname).ToList(),
                columnCount: _columnCount != null ? (int)_columnCount.Value : 2,
                title: Title != null && Title.EndsWith(favTitle) ? Title[..^favTitle.Length] : string.Empty);

            var result = await newFavoriteWindow.ShowDialog<bool>(this);
            if (result)
            {
                LoadFavorites();
            }
        }

        private async void ManageFavorites_Click(object? sender, RoutedEventArgs e)
        {
            var wnd = new ManageFavoritesWindow();
            await wnd.ShowDialog(this);
            LoadFavorites();
        }

        private async void ManageAliases_Click(object? sender, RoutedEventArgs e)
        {
            var wnd = new ManageAliasesWindow();
            await wnd.ShowDialog(this);
            LoadAliases();
        }

        private void PopupAlways_Click(object? sender, RoutedEventArgs e)
        {
            SetPopupOption(ApplicationOptions.PopupNotificationOption.Always);
        }

        private void PopupNever_Click(object? sender, RoutedEventArgs e)
        {
            SetPopupOption(ApplicationOptions.PopupNotificationOption.Never);
        }

        private void PopupWhenMinimized_Click(object? sender, RoutedEventArgs e)
        {
            SetPopupOption(ApplicationOptions.PopupNotificationOption.WhenMinimized);
        }

        private void SetPopupOption(ApplicationOptions.PopupNotificationOption option)
        {
            if (_popupAlways != null) _popupAlways.IsChecked = option == ApplicationOptions.PopupNotificationOption.Always;
            if (_popupNever != null) _popupNever.IsChecked = option == ApplicationOptions.PopupNotificationOption.Never;
            if (_popupWhenMinimized != null) _popupWhenMinimized.IsChecked = option == ApplicationOptions.PopupNotificationOption.WhenMinimized;
            ApplicationOptions.PopupOption = option;
        }

        private void IsolatedView_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: Probe probe }) return;

            if (probe.IsolatedWindow == null)
            {
                new IsolatedPingWindow(probe).Show();
            }
            else
            {
                probe.IsolatedWindow.Activate();
            }
        }

        // FASE 5 (rodada 2) — drag-and-drop pra reordenar probes, portado de
        // ProbeTitle_PreviewMouseMove/Probe_Drop do original. Diferenças:
        //   - DragDrop.DoDragDrop do Avalonia é assíncrono (Task) e recebe o
        //     PointerEventArgs que disparou o gesto, não o DependencyObject.
        //   - O formato do DataObject é uma string própria ("vmping/probe") em
        //     vez do "Source" genérico do original — só trafega dentro do
        //     processo, então o nome é livre.
        //   - Achar o probe alvo no Drop: e.Source é o elemento mais fundo sob
        //     o cursor; como todo elemento do template herda o DataContext do
        //     item, basta ler DataContext dele (o original fazia o mesmo com
        //     sender as Label/DockPanel).
        private const string ProbeDragFormat = "vmping/probe";

        private async void ProbeTitle_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (sender is not Control { DataContext: Probe probe } control)
            {
                return;
            }

            // Mesmo gatilho do original: botão esquerdo pressionado durante o
            // movimento inicia o arrasto imediatamente, sem threshold.
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var data = new DataObject();
            data.Set(ProbeDragFormat, probe);
            e.Handled = true;
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }

        private void Probe_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.Data.Contains(ProbeDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Probe_Drop(object? sender, DragEventArgs e)
        {
            if (e.Data.Get(ProbeDragFormat) is not Probe source)
            {
                return;
            }

            var target = (e.Source as Control)?.DataContext as Probe;
            if (target == null || ReferenceEquals(target, source))
            {
                return;
            }

            int oldIndex = _probeCollection.IndexOf(source);
            int newIndex = _probeCollection.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
            {
                return;
            }

            // Igual ao original: remove e reinsere na posição do alvo.
            _probeCollection.RemoveAt(oldIndex);
            _probeCollection.Insert(newIndex, source);
            e.Handled = true;
        }

        private async void EditAlias_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: Probe probe }) return;
            if (string.IsNullOrEmpty(probe.Hostname)) return;

            probe.Alias = LookupAlias(probe.Hostname) ?? string.Empty;

            var wnd = new EditAliasWindow(probe);
            var result = await wnd.ShowDialog<bool>(this);
            if (result)
            {
                LoadAliases();
            }
            Focus();
        }

        private void StatusHistoryExecute(object? sender, RoutedEventArgs e)
        {
            if (Probe.StatusHistoryWindow == null)
            {
                var wnd = new StatusHistoryWindow(Probe.StatusChangeLog);
                Probe.StatusHistoryWindow = wnd;
                wnd.Closed += (_, _) => Probe.StatusHistoryWindow = null;
                wnd.Show();
            }
            else
            {
                Probe.StatusHistoryWindow.Activate();
            }
        }

        private void Hostname_Loaded(object? sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _probeCollection.Count - 1; ++i)
            {
                if (string.IsNullOrEmpty(_probeCollection[i].Hostname))
                {
                    return;
                }
            }
            (sender as TextBox)?.Focus();
        }

        // [Certo] Substitui Controls/AutoScrollListBox.cs (Fase 3 simplificada —
        // ver cabeçalho do .axaml). Em vez de um AdornerLayer customizado
        // (API interna do WPF sem equivalente 1:1 no Avalonia), aproveita que
        // Classes/Probe.cs já dispara PropertyChanged(HistoryAsString) toda vez
        // que o histórico muda, e usa isso como gatilho para rolar a ListBox até
        // o fim. Perde o indicador visual de "há conteúdo novo abaixo" do
        // original — puramente cosmético, considerar para a Fase 5.
        private void History_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not ListBox listBox || listBox.DataContext is not Probe probe) return;

            probe.PropertyChanged += Probe_PropertyChangedForAutoScroll;
            if (listBox.ItemCount > 0)
            {
                listBox.ScrollIntoView(listBox.ItemCount - 1);
            }

            void Probe_PropertyChangedForAutoScroll(object? s, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName != nameof(Probe.HistoryAsString)) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (listBox.ItemCount > 0)
                    {
                        listBox.ScrollIntoView(listBox.ItemCount - 1);
                    }
                });
            }

            listBox.Tag = (System.ComponentModel.PropertyChangedEventHandler)Probe_PropertyChangedForAutoScroll;
        }

        private void History_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is ListBox { DataContext: Probe probe } listBox &&
                listBox.Tag is System.ComponentModel.PropertyChangedEventHandler handler)
            {
                probe.PropertyChanged -= handler;
            }
        }

        // --- Bandeja do sistema -------------------------------------------------
        // [Provável] Reaproveita o padrão da Fase 1 (TrayIcon + NativeMenu do
        // Avalonia), agora com o menu de contexto completo (Options/Status
        // History/Exit) igual ao original. Diferente do WPF, o Avalonia não
        // precisa do hack de reflection para exibir o menu no botão direito —
        // TrayIcon.Menu já é mostrado automaticamente pelo SO.
        private void SetupTrayIcon()
        {
            try
            {
                _trayIcon = new TrayIcon
                {
                    Icon = new WindowIcon(new Bitmap(AssetLoader.Open(
                        new Uri("avares://vmping/Assets/vmPing-16.png")))),
                    ToolTipText = "vmPing",
                    IsVisible = false,
                };

                var menu = new NativeMenu();

                var optionsItem = new NativeMenuItem(Properties.Strings.Menu_Options);
                optionsItem.Click += (_, _) => OptionsExecute(null, new RoutedEventArgs());
                var statusHistoryItem = new NativeMenuItem(Properties.Strings.Menu_StatusHistory);
                statusHistoryItem.Click += (_, _) => StatusHistoryExecute(null, new RoutedEventArgs());
                var exitItem = new NativeMenuItem(Properties.Strings.Tray_Exit);
                exitItem.Click += (_, _) =>
                    (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();

                menu.Items.Add(optionsItem);
                menu.Items.Add(statusHistoryItem);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(exitItem);

                _trayIcon.Menu = menu;
                _trayIcon.Clicked += (_, _) => RestoreFromTray();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Tray icon indisponível: {ex.Message}");
            }
        }

        private void HideToTray()
        {
            Hide();
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = true;
            }
        }

        private void RestoreFromTray()
        {
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
            }
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty &&
                WindowState == WindowState.Minimized &&
                ApplicationOptions.IsMinimizeToTrayEnabled)
            {
                HideToTray();
            }
        }

        private void Window_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (ApplicationOptions.IsExitToTrayEnabled)
            {
                HideToTray();
                e.Cancel = true;
            }
            else
            {
                _trayIcon?.Dispose();
            }
        }

        // TODO Fase 5: reordenar probes por drag-and-drop (ProbeTitle_PreviewMouseMove /
        // History_PreviewDragOver / Probe_Drop no original). Avalonia.Input.DragDrop
        // é assíncrono (DoDragDrop retorna Task<DragDropEffects>), então portar isso
        // exige reestruturar os handlers como async — não crítico para a Fase 3.
    }
}
