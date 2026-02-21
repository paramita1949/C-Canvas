using System;
using System.Collections.Generic;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ImageColorChanger.UI.Modules
{
    public sealed class BibleSearchResultPresenter : IBibleSearchResultPresenter
    {
        private readonly Popup _popup;
        private readonly WpfListBox _listBox;
        private readonly WpfTextBlock _statusText;
        private readonly WpfTextBox _searchBox;

        public event EventHandler<BibleSearchHit> HitSelected;

        public bool IsOpen => _popup?.IsOpen ?? false;

        public BibleSearchResultPresenter(
            Popup popup,
            WpfListBox listBox,
            WpfTextBlock statusText,
            WpfTextBox searchBox)
        {
            _popup = popup ?? throw new ArgumentNullException(nameof(popup));
            _listBox = listBox ?? throw new ArgumentNullException(nameof(listBox));
            _statusText = statusText ?? throw new ArgumentNullException(nameof(statusText));
            _searchBox = searchBox ?? throw new ArgumentNullException(nameof(searchBox));

            _listBox.MouseLeftButtonUp += ListBox_MouseLeftButtonUp;
        }

        public void ShowResults(IReadOnlyList<BibleSearchHit> hits)
        {
            hits ??= Array.Empty<BibleSearchHit>();

            _statusText.Text = hits.Count == 0
                ? "未找到相关经文"
                : $"找到 {hits.Count} 条";

            _listBox.ItemsSource = hits;
            _listBox.SelectedIndex = hits.Count > 0 ? 0 : -1;

            _listBox.MinWidth = Math.Max(420, _searchBox.ActualWidth);
            _popup.IsOpen = true;
        }

        public void ShowStatus(string message)
        {
            _statusText.Text = message ?? string.Empty;
            if (!_popup.IsOpen)
            {
                _popup.IsOpen = true;
            }
        }

        public void Hide()
        {
            _popup.IsOpen = false;
            _listBox.ItemsSource = null;
            _listBox.SelectedIndex = -1;
            _statusText.Text = string.Empty;
        }

        public bool HandleSearchBoxKeyDown(Key key)
        {
            if (!_popup.IsOpen)
            {
                return false;
            }

            switch (key)
            {
                case Key.Escape:
                    Hide();
                    return true;
                case Key.Down:
                    MoveSelection(1);
                    return true;
                case Key.Up:
                    MoveSelection(-1);
                    return true;
                case Key.Enter:
                    RaiseSelectedHit();
                    return true;
                default:
                    return false;
            }
        }

        public void Dispose()
        {
            _listBox.MouseLeftButtonUp -= ListBox_MouseLeftButtonUp;
        }

        private void ListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            RaiseSelectedHit();
        }

        private void MoveSelection(int delta)
        {
            if (_listBox.Items.Count == 0)
            {
                return;
            }

            int nextIndex = _listBox.SelectedIndex + delta;
            if (nextIndex < 0)
            {
                nextIndex = 0;
            }
            else if (nextIndex >= _listBox.Items.Count)
            {
                nextIndex = _listBox.Items.Count - 1;
            }

            _listBox.SelectedIndex = nextIndex;
            _listBox.ScrollIntoView(_listBox.SelectedItem);
        }

        private void RaiseSelectedHit()
        {
            if (_listBox.SelectedItem is BibleSearchHit hit)
            {
                HitSelected?.Invoke(this, hit);
            }
        }
    }
}
