using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using vmPing.Classes;
using vmPing.Properties;

namespace vmPing.UI
{
    public partial class ManageAliasesWindow : Window
    {
        private readonly ListBox? _aliases;

        public ManageAliasesWindow()
        {
            InitializeComponent();

            _aliases = this.FindControl<ListBox>("Aliases");
            RefreshAliasList();
        }

        private void RefreshAliasList()
        {
            if (_aliases == null)
            {
                return;
            }

            var aliases = Alias.GetAll()
                .OrderBy(alias => alias.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _aliases.ItemsSource = aliases;
        }

        private KeyValuePair<string, string>? GetSelectedAlias()
        {
            if (_aliases?.SelectedItem is KeyValuePair<string, string> selected)
            {
                return selected;
            }
            return null;
        }

        private async void Remove_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAlias();
            if (selected == null)
            {
                return;
            }

            var confirmed = await DialogWindow
                .WarningWindow(
                    $"{Strings.ManageAliases_Warn_DeleteA} {selected.Value.Value} {Strings.ManageAliases_Warn_DeleteB}",
                    Strings.DialogButton_Remove)
                .ShowDialog<bool>(this);

            if (confirmed)
            {
                Alias.Delete(selected.Value.Key);
                RefreshAliasList();
            }
        }

        private async void Edit_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAlias();
            if (selected == null)
            {
                return;
            }

            var wnd = new EditAliasWindow(selected.Value.Key, selected.Value.Value);
            if (await wnd.ShowDialog<bool>(this))
            {
                RefreshAliasList();
            }
        }

        private async void New_Click(object? sender, RoutedEventArgs e)
        {
            var wnd = new NewAliasWindow();
            if (await wnd.ShowDialog<bool>(this))
            {
                RefreshAliasList();
            }
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}
