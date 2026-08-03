using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class IsolatedPingWindow : Window
    {
        public IsolatedPingWindow()
        {
            InitializeComponent();
        }

        public IsolatedPingWindow(Probe pingItem) : this()
        {
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            pingItem.IsolatedWindow = this;
            DataContext = pingItem;
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            if (DataContext is Probe probe)
            {
                probe.IsolatedWindow = null;
            }
            DataContext = null;
        }
    }
}
