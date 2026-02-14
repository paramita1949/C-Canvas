using System;
using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树选中与展开辅助
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 在项目树中选中指定ID的节点
        /// </summary>
        private void SelectTreeItemById(int itemId)
        {
            try
            {
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                SelectTreeItemRecursive(treeItems, itemId);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 递归查找并选中树节点
        /// </summary>
        private bool SelectTreeItemRecursive(IEnumerable<ProjectTreeItem> items, int targetId)
        {
            foreach (var item in items)
            {
                if (item.Id == targetId && item.Type == TreeItemType.File)
                {
                    item.IsSelected = true;
                    ExpandParentNodes(item);
                    return true;
                }

                if (item.Children != null && item.Children.Count > 0)
                {
                    if (SelectTreeItemRecursive(item.Children, targetId))
                    {
                        item.IsExpanded = true;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 展开父节点
        /// </summary>
        private void ExpandParentNodes(ProjectTreeItem item)
        {
            var allItems = ProjectTree.Items.Cast<ProjectTreeItem>();
            ExpandParentNodesRecursive(allItems, item);
        }

        /// <summary>
        /// 递归展开父节点
        /// </summary>
        private bool ExpandParentNodesRecursive(IEnumerable<ProjectTreeItem> items, ProjectTreeItem target)
        {
            foreach (var item in items)
            {
                if (item == target)
                {
                    return true;
                }

                if (item.Children != null && item.Children.Count > 0)
                {
                    if (ExpandParentNodesRecursive(item.Children, target))
                    {
                        item.IsExpanded = true;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
