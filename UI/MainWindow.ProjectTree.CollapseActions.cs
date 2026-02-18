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
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                foreach (var item in treeItems)
                {
                    if (item.Type == TreeItemType.Folder && item.Id != exceptFolder.Id)
                    {
                        CollapseFolder(item);
                    }
                }
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

            folder.IsExpanded = false;

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
