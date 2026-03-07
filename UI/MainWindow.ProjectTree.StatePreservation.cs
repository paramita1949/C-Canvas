using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：通用状态保留刷新
    /// </summary>
    public partial class MainWindow
    {
        private void ReloadProjectsPreservingTreeState(TreeItemType? preferredType = null, int preferredId = 0)
        {
            var state = CaptureProjectTreeViewState();
            LoadProjects();
            RestoreProjectTreeViewState(state, preferredType, preferredId);
        }

        private ProjectTreeViewState CaptureProjectTreeViewState()
        {
            var state = new ProjectTreeViewState();
            CaptureProjectTreeViewStateRecursive(_projectTreeItems, state);
            return state;
        }

        private void CaptureProjectTreeViewStateRecursive(
            IEnumerable<ProjectTreeItem> items,
            ProjectTreeViewState state)
        {
            foreach (var item in items)
            {
                if (item.IsExpanded && item.Children != null && item.Children.Count > 0)
                {
                    state.ExpandedKeys.Add(BuildTreeStateKey(item));
                }

                if (string.IsNullOrEmpty(state.SelectedKey) && item.IsSelected)
                {
                    state.SelectedKey = BuildTreeStateKey(item);
                }

                if (item.Children != null && item.Children.Count > 0)
                {
                    CaptureProjectTreeViewStateRecursive(item.Children, state);
                }
            }
        }

        private void RestoreProjectTreeViewState(ProjectTreeViewState state, TreeItemType? preferredType, int preferredId)
        {
            if (state == null)
            {
                return;
            }

            RestoreProjectTreeExpansionRecursive(_projectTreeItems, state.ExpandedKeys);
            ClearProjectTreeSelectionRecursive(_projectTreeItems);

            string targetKey = preferredType.HasValue && preferredId > 0
                ? BuildTreeStateKey(preferredType.Value, preferredId)
                : state.SelectedKey;

            if (string.IsNullOrWhiteSpace(targetKey))
            {
                return;
            }

            _ = TrySelectProjectTreeItemByKeyRecursive(_projectTreeItems, targetKey);
        }

        private void RestoreProjectTreeExpansionRecursive(
            IEnumerable<ProjectTreeItem> items,
            HashSet<string> expandedKeys)
        {
            foreach (var item in items)
            {
                item.IsExpanded = expandedKeys.Contains(BuildTreeStateKey(item));
                if (item.Children != null && item.Children.Count > 0)
                {
                    RestoreProjectTreeExpansionRecursive(item.Children, expandedKeys);
                }
            }
        }

        private void ClearProjectTreeSelectionRecursive(IEnumerable<ProjectTreeItem> items)
        {
            foreach (var item in items)
            {
                item.IsSelected = false;
                if (item.Children != null && item.Children.Count > 0)
                {
                    ClearProjectTreeSelectionRecursive(item.Children);
                }
            }
        }

        private bool TrySelectProjectTreeItemRecursive(
            IEnumerable<ProjectTreeItem> items,
            TreeItemType targetType,
            int targetId)
        {
            foreach (var item in items)
            {
                if (item.Type == targetType && item.Id == targetId)
                {
                    item.IsSelected = true;
                    return true;
                }

                if (item.Children == null || item.Children.Count == 0)
                {
                    continue;
                }

                if (TrySelectProjectTreeItemRecursive(item.Children, targetType, targetId))
                {
                    item.IsExpanded = true;
                    return true;
                }
            }

            return false;
        }

        private bool TrySelectProjectTreeItemByKeyRecursive(
            IEnumerable<ProjectTreeItem> items,
            string targetKey)
        {
            foreach (var item in items)
            {
                if (BuildTreeStateKey(item) == targetKey)
                {
                    item.IsSelected = true;
                    return true;
                }

                if (item.Children == null || item.Children.Count == 0)
                {
                    continue;
                }

                if (TrySelectProjectTreeItemByKeyRecursive(item.Children, targetKey))
                {
                    item.IsExpanded = true;
                    return true;
                }
            }

            return false;
        }

        private static string BuildTreeStateKey(ProjectTreeItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(item.StateKey)
                ? BuildTreeStateKey(item.Type, item.Id)
                : item.StateKey;
        }

        private static string BuildTreeStateKey(TreeItemType type, int id) => $"{(int)type}:{id}";

        private static bool TryParseTreeStateKey(string key, out TreeItemType type, out int id)
        {
            type = TreeItemType.Project;
            id = 0;

            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string[] parts = key.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out int typeValue) || !System.Enum.IsDefined(typeof(TreeItemType), typeValue))
            {
                return false;
            }

            if (!int.TryParse(parts[1], out int parsedId))
            {
                return false;
            }

            type = (TreeItemType)typeValue;
            id = parsedId;
            return true;
        }

        private sealed class ProjectTreeViewState
        {
            public HashSet<string> ExpandedKeys { get; } = new();
            public string SelectedKey { get; set; } = string.Empty;
        }
    }
}
