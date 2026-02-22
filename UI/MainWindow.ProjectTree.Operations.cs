using System.Windows;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：通用操作辅助
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 获取播放模式对应的图标
        /// </summary>
        private (string iconKind, string iconColor) GetPlayModeIcon(string playMode)
        {
            return playMode switch
            {
                "sequential" => ("FolderArrowUpDownOutline", ICON_COLOR_SEQUENTIAL),
                "random" => ("FolderArrowRightOutline", ICON_COLOR_RANDOM),
                "loop_all" => ("FolderSyncOutline", ICON_COLOR_LOOP),
                "loop_one" => ("FolderPlayOutline", ICON_COLOR_LOOP),
                _ => ("FolderArrowRightOutline", ICON_COLOR_RANDOM)
            };
        }
    }
}
