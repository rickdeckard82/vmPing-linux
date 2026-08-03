using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using vmPing.Classes;
using vmPing.Properties;

namespace vmPing.UI
{
    public partial class NewFavoriteWindow : Window
    {
        private List<string> _hostList;
        private int _columnCount;
        private readonly bool _isExisting;
        private readonly string _originalTitle;

        private readonly TextBlock? _header;
        private readonly TextBox? _myTitle;
        private readonly TextBox? _myColumnCount;
        private readonly TextBox? _myHosts;

        public NewFavoriteWindow() : this(new List<string>(), 2) { }

        public NewFavoriteWindow(List<string> hostList, int columnCount, bool isEditExisting = false, string title = "")
        {
            InitializeComponent();

            _hostList = hostList;
            _columnCount = columnCount;
            _isExisting = isEditExisting;
            _originalTitle = title;

            _header = this.FindControl<TextBlock>("Header");
            _myTitle = this.FindControl<TextBox>("MyTitle");
            _myColumnCount = this.FindControl<TextBox>("MyColumnCount");
            _myHosts = this.FindControl<TextBox>("MyHosts");

            if (_myHosts != null) _myHosts.Text = string.Join(Environment.NewLine, hostList).Trim();
            if (_myColumnCount != null) _myColumnCount.Text = columnCount.ToString();
            if (_myTitle != null) _myTitle.Text = title;

            if (isEditExisting)
            {
                Title = "Edit Favorite";
                if (_header != null) _header.Text = "Edit an existing favorite";
            }
        }

        private async void Save_Click(object? sender, RoutedEventArgs e)
        {
            var titleText = _myTitle?.Text?.Trim() ?? string.Empty;
            var columnText = _myColumnCount?.Text?.Trim() ?? string.Empty;
            var hostsText = _myHosts?.Text ?? string.Empty;

            // Validate column count.
            if (!int.TryParse(columnText, out _columnCount) || _columnCount < 1 || _columnCount > 10)
            {
                await DialogWindow.ErrorWindow(Properties.Strings.Msg_InvalidColumns).ShowDialog(this);
                _myColumnCount?.Focus();
                _myColumnCount?.SelectAll();
                return;
            }

            // Validate favorite name.
            if (Favorite.IsTitleInvalid(titleText))
            {
                await DialogWindow.ErrorWindow(Strings.NewFavorite_Error_InvalidName).ShowDialog(this);
                _myTitle?.Focus();
                _myTitle?.SelectAll();
                return;
            }

            // Split hosts, trim each, ensure at least one was entered.
            _hostList = hostsText.Trim().Split(new[] { ',', '\n' }).Select(host => host.Trim()).ToList();
            if (_hostList.All(string.IsNullOrWhiteSpace))
            {
                await DialogWindow.ErrorWindow(Properties.Strings.Msg_NoHosts).ShowDialog(this);
                _myHosts?.Focus();
                _myHosts?.SelectAll();
                return;
            }

            // If creating a new favorite, or renaming an existing one to a title that already
            // exists elsewhere, warn before overwriting. Editing without a title change proceeds
            // straight to save (matches original vmPing behavior).
            var titleExistsElsewhere = !_isExisting
                ? Favorite.TitleExists(titleText)
                : !string.Equals(_originalTitle, titleText) && Favorite.TitleExists(titleText);

            if (titleExistsElsewhere)
            {
                var confirmed = await DialogWindow
                    .WarningWindow($"{titleText} {Strings.NewFavorite_Warn_AlreadyExists}", Strings.DialogButton_Overwrite)
                    .ShowDialog<bool>(this);
                if (!confirmed)
                {
                    return;
                }
            }

            SaveFavorite(titleText);
        }

        private void SaveFavorite(string title)
        {
            if (_isExisting && !title.Equals(_originalTitle))
            {
                Favorite.Rename(originalTitle: _originalTitle, newTitle: title);
            }

            Favorite.Save(title, _hostList, _columnCount);
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
    }
}
