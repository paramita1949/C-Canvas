using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private readonly ObservableCollection<ProjectTreeItem> _onlineLibraryTreeItems = new();
        private ListCollectionView _localLibraryView;
        private bool _onlineLibraryPaneSettingsLoaded;
        private bool _onlineLibraryPaneCollapsed = true;
        private double _onlineLibraryPaneHeight = 160;

        private void InitializeOnlineLibraryPane()
        {
            // 两棵树共享原有节点/模板/点击入口，只分开滚动，不创建第二套文件模型。
            _localLibraryView = new ListCollectionView(_filteredProjectTreeItems);
            _localLibraryView.Filter = item => _currentViewMode != NavigationViewMode.Files ||
                item is not ProjectTreeItem node || !node.IsOnlineScoreLibraryRoot;
            ProjectTree.ItemsSource = _localLibraryView;
            OnlineLibraryTree.ItemsSource = _onlineLibraryTreeItems;
            ProjectTree.SelectedItemChanged += LibraryTree_SelectedItemChanged;
            OnlineLibraryTree.SelectedItemChanged += LibraryTree_SelectedItemChanged;
            ProjectTree.IsVisibleChanged += (_, _) => UpdateOnlineLibraryPane();
            RefreshOnlineLibraryRoots();
        }

        private void RefreshOnlineLibraryRoots()
        {
            if (_currentViewMode == NavigationViewMode.Files)
            {
                _onlineLibraryTreeItems.Clear();
                foreach (var item in _filteredProjectTreeItems.Where(i => i.IsOnlineScoreLibraryRoot))
                {
                    item.ShowOnlineScoreSeparator = false; // 固定分区标题代替随节点滚动的分隔线。
                    _onlineLibraryTreeItems.Add(item);
                }
            }
            _localLibraryView?.Refresh();
            UpdateOnlineLibraryPane();
        }

        private IEnumerable<ProjectTreeItem> GetVisibleLibraryRoots() =>
            _filteredProjectTreeItems.Concat(_onlineLibraryTreeItems).Distinct();

        private static TreeViewItem FindLibraryItemContainer(ItemsControl parent, ProjectTreeItem item)
        {
            if (parent == null) return null;
            parent.UpdateLayout();
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem found) return found;
            foreach (var child in parent.Items)
                if (parent.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem container &&
                    FindLibraryItemContainer(container, item) is TreeViewItem nested) return nested;
            return null;
        }

        private void LibraryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not ProjectTreeItem selected) return;
            var other = ReferenceEquals(sender, OnlineLibraryTree)
                ? ProjectTree.Items.Cast<ProjectTreeItem>() : _onlineLibraryTreeItems;
            ClearProjectTreeSelectionRecursive(other);
            selected.IsSelected = true;
        }

        private void EnsureOnlineLibraryPaneSettingsLoaded()
        {
            if (_onlineLibraryPaneSettingsLoaded || _mainWindowServices == null) return;
            _onlineLibraryPaneSettingsLoaded = true;
            _onlineLibraryPaneCollapsed = !bool.TryParse(DatabaseManagerService.GetUISetting(
                "OnlineScores.PaneCollapsed.v1", "True"), out var collapsed) || collapsed;
            if (double.TryParse(DatabaseManagerService.GetUISetting("OnlineScores.PaneHeight.v1", "160"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var height) && double.IsFinite(height))
                _onlineLibraryPaneHeight = Math.Clamp(height, 82, 1200);
        }

        private void UpdateOnlineLibraryPane()
        {
            UpdateProjectSearchFolderTagsVisibility();
            if (OnlineLibraryPane == null || OnlineLibraryPaneRow == null) return;
            EnsureOnlineLibraryPaneSettingsLoaded();
            // 空库也保留入口与分区标题；删空歌本不等于关闭在线歌谱功能。
            bool visible = !_isBibleMode && _currentViewMode == NavigationViewMode.Files && !IsOnlineScoreMode;
            OnlineLibraryPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            OnlineLibrarySplitter.Visibility = visible && !_onlineLibraryPaneCollapsed ? Visibility.Visible : Visibility.Collapsed;
            OnlineLibrarySplitterRow.Height = new GridLength(visible && !_onlineLibraryPaneCollapsed ? 5 : 0);
            OnlineLibraryTree.Visibility = _onlineLibraryPaneCollapsed ? Visibility.Collapsed : Visibility.Visible;
            OnlineLibraryCollapseGlyph.Text = _onlineLibraryPaneCollapsed ? "⌃" : "⌄";
            OnlineLibraryHeader.ToolTip = _onlineLibraryPaneCollapsed ? "展开在线歌谱" : "向下收起在线歌谱";
            double available = LocalLibraryLayout.ActualHeight;
            double maximum = Math.Max(34, available > 0 ? available - Math.Min(64, available * .35) - 5 : 400);
            OnlineLibraryPaneRow.MinHeight = visible ? (_onlineLibraryPaneCollapsed ? 34 : Math.Min(82, maximum)) : 0;
            OnlineLibraryPaneRow.MaxHeight = visible ? (_onlineLibraryPaneCollapsed ? 34 : maximum) : double.PositiveInfinity;
            OnlineLibraryPaneRow.Height = new GridLength(!visible ? 0 : _onlineLibraryPaneCollapsed ? 34 : Math.Clamp(_onlineLibraryPaneHeight, Math.Min(82, maximum), maximum));
        }

        private void OnlineLibraryHeader_Click(object sender, RoutedEventArgs e)
        {
            // 高度只由用户拖动更新；折叠不能把窗口临时限高或隐藏时的0px覆盖为偏好。
            _onlineLibraryPaneCollapsed = !_onlineLibraryPaneCollapsed;
            UpdateOnlineLibraryPane();
            SaveOnlineLibraryPaneSettings();
        }

        private void OnlineLibrarySplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_onlineLibraryPaneCollapsed || e.Canceled) return;
            // 预览拖动结束已提交行高度，但 ActualHeight 可能仍是上一帧；先完成布局再保存本次高度。
            LocalLibraryLayout.UpdateLayout();
            _onlineLibraryPaneHeight = OnlineLibraryPane.ActualHeight;
            SaveOnlineLibraryPaneSettings();
        }

        private void LocalLibraryLayout_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateOnlineLibraryPane();

        private void SaveOnlineLibraryPaneSettings()
        {
            try
            {
                DatabaseManagerService?.SaveUISetting("OnlineScores.PaneCollapsed.v1", _onlineLibraryPaneCollapsed.ToString());
                DatabaseManagerService?.SaveUISetting("OnlineScores.PaneHeight.v1", _onlineLibraryPaneHeight.ToString(CultureInfo.InvariantCulture));
            }
            catch { ShowStatus("分区布局已调整，但暂未保存；本次仍可正常使用。"); }
        }
    }
}
