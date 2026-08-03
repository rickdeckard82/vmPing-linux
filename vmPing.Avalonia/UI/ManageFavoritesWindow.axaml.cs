using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using vmPing.Classes;
using vmPing.Properties;

namespace vmPing.UI
{
    public partial class ManageFavoritesWindow : Window
    {
        private Favorite? _selectedFavorite;

        private readonly ListBox? _favorites;
        private readonly ListBox? _contents;
        private readonly Border? _contentsSection;
        private readonly TextBlock? _favoriteTitle;

        public ManageFavoritesWindow()
        {
            InitializeComponent();

            _favorites = this.FindControl<ListBox>("Favorites");
            _contents = this.FindControl<ListBox>("Contents");
            _contentsSection = this.FindControl<Border>("ContentsSection");
            _favoriteTitle = this.FindControl<TextBlock>("FavoriteTitle");

            RefreshFavoriteList();
        }

        private void RefreshFavoriteList()
        {
            if (_favorites != null)
            {
                _favorites.ItemsSource = Favorite.GetTitles();
            }
            HideContentsSection();
        }

        private void HideContentsSection()
        {
            if (_contentsSection != null) _contentsSection.IsVisible = false;
            if (_contents != null) _contents.ItemsSource = null;
        }

        private void Favorites_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var selectedTitle = _favorites?.SelectedItem as string;
            if (selectedTitle == null)
            {
                HideContentsSection();
                return;
            }

            _selectedFavorite = Favorite.Load(selectedTitle);

            if (_contentsSection != null) _contentsSection.IsVisible = true;
            if (_contents != null) _contents.ItemsSource = _selectedFavorite.Hostnames;
            if (_favoriteTitle != null) _favoriteTitle.Text = selectedTitle;
        }

        private async void Remove_Click(object? sender, RoutedEventArgs e)
        {
            var selectedTitle = _favorites?.SelectedItem as string;
            if (selectedTitle == null)
            {
                return;
            }

            var confirmed = await DialogWindow
                .WarningWindow(
                    $"{Strings.ManageFavorites_Warn_DeleteA} {selectedTitle} {Strings.ManageFavorites_Warn_DeleteB}",
                    Strings.DialogButton_Remove)
                .ShowDialog<bool>(this);

            if (confirmed)
            {
                Favorite.Delete(selectedTitle);
                RefreshFavoriteList();
            }
        }

        private async void Edit_Click(object? sender, RoutedEventArgs e)
        {
            var selectedTitle = _favorites?.SelectedItem as string;
            if (selectedTitle == null || _selectedFavorite == null)
            {
                return;
            }

            var wnd = new NewFavoriteWindow(
                hostList: _selectedFavorite.Hostnames,
                columnCount: _selectedFavorite.ColumnCount,
                isEditExisting: true,
                title: selectedTitle);

            if (await wnd.ShowDialog<bool>(this))
            {
                RefreshFavoriteList();
            }
        }

        private async void New_Click(object? sender, RoutedEventArgs e)
        {
            var wnd = new NewFavoriteWindow(new List<string>(), 2);
            if (await wnd.ShowDialog<bool>(this))
            {
                RefreshFavoriteList();
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
