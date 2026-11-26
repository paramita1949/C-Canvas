using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Database.Models;
using WpfKey = System.Windows.Input.Key;

namespace ImageColorChanger.UI
{
    public partial class MoveSongWindow : Window
    {
        public sealed class FolderChoice
        {
            public int Id { get; init; }
            public string Name { get; init; }
            public string Path { get; init; }
        }
        private readonly List<FolderChoice> _folders;
        private readonly Func<int, string, bool, Task<string>> _move;
        private readonly string _extension;
        private bool _busy;
        public string MovedPath { get; private set; }

        public MoveSongWindow(MediaFile song, IEnumerable<Folder> folders, Func<int, string, bool, Task<string>> move,
            IEnumerable<string> onlineDirectories = null)
        {
            _move = move ?? throw new ArgumentNullException(nameof(move));
            string current = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(song.Path));
            _extension = System.IO.Path.GetExtension(song.Path);
            var excluded = (onlineDirectories ?? Array.Empty<string>()).Select(NormalizeDirectory).Where(p => p != null).ToArray();
            _folders = folders.Where(f => Directory.Exists(f.Path)).Where(f =>
            {
                string path = NormalizeDirectory(f.Path);
                return path != null && !string.Equals(path, NormalizeDirectory(current), StringComparison.OrdinalIgnoreCase) &&
                    !excluded.Any(root => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith(System.IO.Path.EndsInDirectorySeparator(root) ? root : root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            }).Select(f => new FolderChoice { Id = f.Id, Name = f.Name, Path = f.Path }).ToList();
            MediaFileDialogTheme.EnsureResources();
            InitializeComponent();
            MoveNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(song.Path);
            MoveExtension.Text = _extension;
            FolderList.ItemsSource = _folders;
            EmptyState.Visibility = _folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Loaded += (_, _) => FolderList.Focus();
            Closing += (_, e) => { if (_busy) e.Cancel = true; };
            PreviewKeyDown += (_, e) => { if (e.Key == WpfKey.Escape && !_busy) { Close(); e.Handled = true; } };
        }
        private static string NormalizeDirectory(string path)
        {
            try { return System.IO.Path.IsPathFullyQualified(path) ? System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)) : null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }
        private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MoveButton == null) return;
            UpdateConfirmation();
        }
        private void MoveNameBox_TextChanged(object sender, TextChangedEventArgs e) { if (MoveButton != null) UpdateConfirmation(); }
        private void UpdateConfirmation()
        {
            var selected = FolderList.SelectedItem as FolderChoice;
            string name = MoveNameBox.Text.Trim();
            if (name.EndsWith(_extension, StringComparison.OrdinalIgnoreCase)) name = name[..^_extension.Length];
            MoveButton.IsEnabled = !_busy && selected != null && !string.IsNullOrWhiteSpace(name);
        }
        private async void MoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || !MoveButton.IsEnabled || FolderList.SelectedItem is not FolderChoice folder) return;
            _busy = true; MoveButton.IsEnabled = false; CancelButton.IsEnabled = false; FolderList.IsEnabled = false; MoveNameBox.IsEnabled = false;
            ErrorText.Visibility = Visibility.Collapsed; MoveProgress.Visibility = Visibility.Visible; MoveButton.Content = "正在移动…";
            try
            {
                MovedPath = await _move(folder.Id, MoveNameBox.Text, false);
                _busy = false;
                DialogResult = true;
            }
            catch (Exception ex) { ErrorText.Text = ex.Message; ErrorText.Visibility = Visibility.Visible; }
            finally
            {
                _busy = false; CancelButton.IsEnabled = true; FolderList.IsEnabled = true; MoveNameBox.IsEnabled = true;
                MoveProgress.Visibility = Visibility.Collapsed; MoveButton.Content = "移动";
                FolderList_SelectionChanged(null, null);
            }
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e) { if (!_busy) Close(); }
    }
}
