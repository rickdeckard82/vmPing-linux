using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class PopupNotificationWindow : Window
    {
        private readonly ObservableCollection<StatusChangeLog> _source;
        private readonly ListBox? _statusHistoryList;
        private readonly DispatcherTimer _autoDismissTimer = new();

        public PopupNotificationWindow() : this(new ObservableCollection<StatusChangeLog>()) { }

        public PopupNotificationWindow(ObservableCollection<StatusChangeLog> statusChangeLog)
        {
            InitializeComponent();

            _source = statusChangeLog;
            _statusHistoryList = this.FindControl<ListBox>("StatusHistoryList");

            _source.CollectionChanged += Source_CollectionChanged;
            Refresh();

            _autoDismissTimer.Tick += AutoDismissTimer_Tick;
            if (ApplicationOptions.IsAutoDismissEnabled)
            {
                _autoDismissTimer.Interval = TimeSpan.FromMilliseconds(ApplicationOptions.AutoDismissMilliseconds);
                _autoDismissTimer.Start();
            }

            // FASE 5 (rodada 3) — fade-in: Opacity começa em 0 (XAML) e vira 1
            // aqui; a DoubleTransition declarada no XAML anima a passagem.
            // Mesmo se a transição não rodar neste compositor, esta linha
            // garante que o popup fica visível (só perde o efeito).
            Opened += (_, _) => Opacity = 1;
        }

        private static bool PassesFilter(StatusChangeLog entry)
            => !entry.HasStatusBeenCleared && entry.Status != ProbeStatus.Start && entry.Status != ProbeStatus.Stop;

        private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (ApplicationOptions.IsAutoDismissEnabled)
            {
                _autoDismissTimer.Stop();
                _autoDismissTimer.Interval = TimeSpan.FromMilliseconds(ApplicationOptions.AutoDismissMilliseconds);
                _autoDismissTimer.Start();
            }

            Refresh();
        }

        private void Refresh()
        {
            if (_statusHistoryList == null)
            {
                return;
            }

            var items = _source.Where(PassesFilter).ToList();
            _statusHistoryList.ItemsSource = items;

            ScaleWindowSize(items.Count);

            if (items.Count > 0)
            {
                _statusHistoryList.ScrollIntoView(items[^1]);
            }
        }

        private void ScaleWindowSize(int itemCount)
        {
            Height = itemCount switch
            {
                <= 1 => 95,
                2 => 110,
                3 => 126,
                4 => 147,
                _ => 172,
            };

            PositionWindow();
        }

        private void PositionWindow()
        {
            // [Provável] Screens.Primary?.WorkingArea não verificado contra o
            // código-fonte nesta sessão — ver comentário no .axaml.
            var workArea = Screens.Primary?.WorkingArea;
            if (workArea == null)
            {
                return;
            }

            Position = new PixelPoint(
                workArea.Value.Right - (int)Width,
                workArea.Value.Bottom - (int)Height);
        }

        private void AutoDismissTimer_Tick(object? sender, EventArgs e)
        {
            if (ApplicationOptions.IsAutoDismissEnabled)
            {
                Close();
            }
        }

        private void Window_SizeChanged(object? sender, SizeChangedEventArgs e) => PositionWindow();

        private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
            {
                return;
            }

            if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } mainWindow)
            {
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                }
                mainWindow.Activate();
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();

        private void Maximize_Click(object? sender, RoutedEventArgs e)
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

            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoDismissTimer.Stop();
            _source.CollectionChanged -= Source_CollectionChanged;
            base.OnClosed(e);
        }
    }
}
