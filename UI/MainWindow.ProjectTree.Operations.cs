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
                "sequential" => ("SortAscending", ICON_COLOR_SEQUENTIAL),
                "random" => ("Shuffle", ICON_COLOR_RANDOM),
                "loop_all" => ("Repeat", ICON_COLOR_LOOP),
                _ => ("Shuffle", ICON_COLOR_RANDOM)
            };
        }
    }
}
