using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 文本编辑器菜单编排控制器。
    /// 仅负责菜单结构和事件转发，不持有 MainWindow 状态。
    /// </summary>
    public sealed class TextEditorMenuController
    {
        public void ShowBackgroundImportMenu(
            UIElement placementTarget,
            Style menuStyle,
            Action importSingle,
            Func<Task> importMulti,
            Func<Task> importVideo)
        {
            if (placementTarget == null)
            {
                throw new ArgumentNullException(nameof(placementTarget));
            }

            if (importSingle == null)
            {
                throw new ArgumentNullException(nameof(importSingle));
            }

            if (importMulti == null)
            {
                throw new ArgumentNullException(nameof(importMulti));
            }

            if (importVideo == null)
            {
                throw new ArgumentNullException(nameof(importVideo));
            }

            var contextMenu = new ContextMenu
            {
                Style = menuStyle
            };

            var singleImageItem = new MenuItem { Header = "单背景图" };
            singleImageItem.Click += (_, __) => importSingle();
            contextMenu.Items.Add(singleImageItem);

            var multiImageItem = new MenuItem { Header = "多背景图" };
            multiImageItem.Click += async (_, __) => await importMulti();
            contextMenu.Items.Add(multiImageItem);

            var videoBackgroundItem = new MenuItem { Header = "视频背景" };
            videoBackgroundItem.Click += async (_, __) => await importVideo();
            contextMenu.Items.Add(videoBackgroundItem);

            contextMenu.PlacementTarget = placementTarget;
            contextMenu.IsOpen = true;
        }

        public void ShowSplitModeMenu(
            UIElement placementTarget,
            Style menuStyle,
            int currentSplitMode,
            Action<ViewSplitMode> setSplitMode)
        {
            if (placementTarget == null)
            {
                throw new ArgumentNullException(nameof(placementTarget));
            }

            if (setSplitMode == null)
            {
                throw new ArgumentNullException(nameof(setSplitMode));
            }

            var contextMenu = new ContextMenu
            {
                Style = menuStyle
            };

            AddSplitModeItem(contextMenu, "单画面", ViewSplitMode.Single, currentSplitMode, setSplitMode);
            AddSplitModeItem(contextMenu, "左右分割", ViewSplitMode.Horizontal, currentSplitMode, setSplitMode);
            AddSplitModeItem(contextMenu, "上下分割", ViewSplitMode.Vertical, currentSplitMode, setSplitMode);
            AddSplitModeItem(contextMenu, "三分割", ViewSplitMode.TripleSplit, currentSplitMode, setSplitMode);
            AddSplitModeItem(contextMenu, "四宫格", ViewSplitMode.Quad, currentSplitMode, setSplitMode);

            contextMenu.PlacementTarget = placementTarget;
            contextMenu.IsOpen = true;
        }

        private static void AddSplitModeItem(
            ContextMenu menu,
            string title,
            ViewSplitMode mode,
            int currentSplitMode,
            Action<ViewSplitMode> setSplitMode)
        {
            var item = new MenuItem
            {
                Header = currentSplitMode == (int)mode ? $"✓ {title}" : $"   {title}",
                Height = 36
            };
            item.Click += (_, __) => setSplitMode(mode);
            menu.Items.Add(item);
        }
    }
}
