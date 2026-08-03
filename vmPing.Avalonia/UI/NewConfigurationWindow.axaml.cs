using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class NewConfigurationWindow : Window
    {
        private readonly TextBlock? _filePath;
        private readonly CheckBox? _portableMode;

        public NewConfigurationWindow()
        {
            InitializeComponent();

            _filePath = this.FindControl<TextBlock>("FilePath");
            _portableMode = this.FindControl<CheckBox>("PortableMode");

            if (_filePath != null)
            {
                _filePath.Text = Configuration.FilePath;
            }
        }

        private void Save_Click(object? sender, RoutedEventArgs e)
        {
            if (_portableMode?.IsChecked == true)
            {
                Configuration.FilePath = GetPortableFilePath();
            }

            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void PortableMode_Click(object? sender, RoutedEventArgs e)
        {
            if (_filePath == null)
            {
                return;
            }

            _filePath.Text = _portableMode?.IsChecked == true
                ? GetPortableFilePath()
                : Configuration.FilePath;
        }

        private static string GetPortableFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vmPing.xml");
        }
    }
}
