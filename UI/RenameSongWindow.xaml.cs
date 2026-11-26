using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.UI
{
    public partial class RenameSongWindow : Window
    {
        private readonly Func<string, Task<string>> _rename;
        private readonly string _directory;
        private readonly string _extension;
        private bool _busy;
        public string RenamedPath { get; private set; }
        public RenameSongWindow(MediaFile song, Func<string, Task<string>> rename)
        {
            _rename = rename ?? throw new ArgumentNullException(nameof(rename));
            _directory = Path.GetDirectoryName(Path.GetFullPath(song.Path));
            _extension = Path.GetExtension(song.Path);
            MediaFileDialogTheme.EnsureResources();
            InitializeComponent();
            ExtensionLabel.Text = _extension;
            NameBox.Text = Path.GetFileNameWithoutExtension(song.Path);
            Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
            Closing += (_, e) => { if (_busy) e.Cancel = true; };
            PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && !_busy) { Close(); e.Handled = true; } };
        }
        private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (PreviewPath == null) return;
            string name = NameBox.Text.Trim();
            if (name.EndsWith(_extension, StringComparison.OrdinalIgnoreCase)) name = name[..^_extension.Length];
            PreviewPath.Text = _directory + Path.DirectorySeparatorChar + name + _extension;
            PreviewPath.ToolTip = PreviewPath.Text;
            ConfirmButton.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(name);
        }
        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || string.IsNullOrWhiteSpace(NameBox.Text)) return;
            _busy = true; NameBox.IsEnabled = false; ConfirmButton.IsEnabled = false; CancelButton.IsEnabled = false; ConfirmButton.Content = "正在更新…"; ErrorText.Visibility = Visibility.Collapsed;
            try { RenamedPath = await _rename(NameBox.Text); _busy = false; DialogResult = true; }
            catch (Exception ex) { ErrorText.Text = ex.Message; ErrorText.Visibility = Visibility.Visible; }
            finally { _busy = false; NameBox.IsEnabled = true; CancelButton.IsEnabled = true; ConfirmButton.Content = "确认重命名"; NameBox_TextChanged(null, null); }
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e) { if (!_busy) Close(); }
    }
}
