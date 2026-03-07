using System;
using System.Linq;
using System.Windows;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：折叠操作
    /// </summary>
    public partial class MainWindow : Window
    {
        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogCollapseDebug(string phase, ProjectTreeItem item, string extra = "")
        {
            // 暂时关闭项目树折叠调试日志，避免调试输出噪音。
        }

        /// <summary>
        /// 折叠所有文件夹节点
        /// </summary>
        private void CollapseAllFolders()
        {
            try
            {
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                foreach (var item in treeItems)
                {
                    if (item.Type == TreeItemType.Folder)
                    {
                        LogCollapseDebug("CollapseAllFolders.Target", item);
                        CollapseFolder(item);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 折叠除指定文件夹外的所有其他文件夹
        /// </summary>
        private void CollapseOtherFolders(ProjectTreeItem exceptFolder)
        {
            try
            {
                LogCollapseDebug("CollapseOtherFolders.Begin", exceptFolder);
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                foreach (var item in treeItems)
                {
                    if (item.Type == TreeItemType.Folder && item.Id != exceptFolder.Id)
                    {
                        LogCollapseDebug("CollapseOtherFolders.Collapsing", item, $"exceptId={exceptFolder?.Id}");
                        CollapseFolder(item);
                    }
                }
                LogCollapseDebug("CollapseOtherFolders.End", exceptFolder);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 递归折叠文件夹及其子文件夹
        /// </summary>
        private void CollapseFolder(ProjectTreeItem folder)
        {
            if (folder == null)
            {
                return;
            }

            bool before = folder.IsExpanded;
            folder.IsExpanded = false;
            LogCollapseDebug("CollapseFolder.SetFalse", folder, $"before={before}");

            if (folder.Children == null)
            {
                return;
            }

            foreach (var child in folder.Children)
            {
                if (child.Type == TreeItemType.Folder)
                {
                    CollapseFolder(child);
                }
            }
        }
    }
}
