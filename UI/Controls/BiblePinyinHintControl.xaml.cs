using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 圣经拼音快速定位提示框
    /// </summary>
    public partial class BiblePinyinHintControl : System.Windows.Controls.UserControl
    {
        public BiblePinyinHintControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 更新提示框内容
        /// </summary>
        public void UpdateHint(string displayText, List<BibleBookMatch> matches)
        {
            // 更新输入文本
            InputText.Text = displayText;

            // 清空匹配结果
            MatchResultsPanel.Children.Clear();

            // 如果有匹配的书卷，显示横向排列
            if (matches != null && matches.Count > 0)
            {
                MatchResultsPanel.Visibility = Visibility.Visible;

                // 最多显示前10个匹配结果
                var displayMatches = matches.Take(10).ToList();

                foreach (var match in displayMatches)
                {
                    var textBlock = new TextBlock
                    {
                        Text = match.BookName,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 16,
                        Margin = new Thickness(0, 0, 15, 5), // 右边距15，下边距5
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    MatchResultsPanel.Children.Add(textBlock);
                }
            }
            else
            {
                MatchResultsPanel.Visibility = Visibility.Collapsed;
            }

            // 显示提示框
            Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 隐藏提示框
        /// </summary>
        public void Hide()
        {
            Visibility = Visibility.Collapsed;
            MatchResultsPanel.Children.Clear();
        }
    }
}

