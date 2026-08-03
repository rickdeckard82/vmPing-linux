using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace vmPing.UI
{
    public partial class MultiInputWindow : Window
    {
        private readonly TextBox? _addressesBox;

        public List<string> Addresses { get; private set; } = new List<string>();

        public MultiInputWindow() : this(null) { }

        public MultiInputWindow(List<string>? addresses)
        {
            InitializeComponent();

            _addressesBox = this.FindControl<TextBox>("AddressesBox");

            if (_addressesBox != null && addresses != null && addresses.Count > 0
                && !addresses.All(string.IsNullOrWhiteSpace))
            {
                _addressesBox.Text = string.Join(Environment.NewLine, addresses);
                _addressesBox.SelectAll();
            }
        }

        private void OK_Click(object? sender, RoutedEventArgs e)
        {
            // Split and trim multi-address text. Split occurs on both newlines and commas.
            Addresses = (_addressesBox?.Text ?? string.Empty)
                .Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
    }
}
