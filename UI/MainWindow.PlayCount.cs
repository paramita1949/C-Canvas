using System;
using System.Windows;
using System.Windows.Input;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 播放次数交互
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 播放次数按钮点击事件：循环切换 1→2→...→10→∞→1
        /// </summary>
        private void BtnPlayCount_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackViewModel == null) return;

            if (_playbackViewModel.PlayCount == -1)
            {
                _playbackViewModel.PlayCount = 1;
            }
            else if (_playbackViewModel.PlayCount >= 10)
            {
                _playbackViewModel.PlayCount = -1;
            }
            else
            {
                _playbackViewModel.PlayCount++;
            }
        }

        /// <summary>
        /// 播放次数按钮滚轮事件：限制在1-10次和无限循环之间
        /// </summary>
        private void BtnPlayCount_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_playbackViewModel == null) return;

            int delta = e.Delta > 0 ? 1 : -1;

            if (_playbackViewModel.PlayCount == -1)
            {
                _playbackViewModel.PlayCount = delta > 0 ? 1 : 10;
            }
            else
            {
                int newCount = _playbackViewModel.PlayCount + delta;
                if (newCount < 1 || newCount > 10)
                {
                    _playbackViewModel.PlayCount = -1;
                }
                else
                {
                    _playbackViewModel.PlayCount = newCount;
                }
            }

            e.Handled = true;
        }

        /// <summary>
        /// 播放次数按钮双击事件：直接在按钮上编辑数字
        /// </summary>
        private void BtnPlayCount_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_playbackViewModel == null) return;

            var textBox = new System.Windows.Controls.TextBox
            {
                Text = _playbackViewModel.PlayCount == -1 ? "∞" : _playbackViewModel.PlayCount.ToString(),
                FontSize = 14,
                Padding = new Thickness(5),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.DodgerBlue,
                BorderThickness = new Thickness(2)
            };

            BtnPlayCount.Content = textBox;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                textBox.Focus();
                textBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);

            textBox.LostFocus += (s, args) =>
            {
                RestoreButton();
            };

            textBox.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    string input = textBox.Text.Trim();

                    if (input == "∞" || input == "-1")
                    {
                        _playbackViewModel.PlayCount = -1;
                        RestoreButton();
                        args.Handled = true;
                        return;
                    }

                    if (int.TryParse(input, out int count))
                    {
                        if (count >= 1 && count <= 10)
                        {
                            _playbackViewModel.PlayCount = count;
                            RestoreButton();
                        }
                        else
                        {
                            ShowStatus("播放次数必须在 1-10 之间或 ∞");
                            RestoreButton();
                        }
                    }
                    else
                    {
                        ShowStatus("请输入有效的数字或 ∞");
                        RestoreButton();
                    }
                    args.Handled = true;
                }
                else if (args.Key == Key.Escape)
                {
                    RestoreButton();
                    args.Handled = true;
                }
            };

            void RestoreButton()
            {
                string text = _playbackViewModel.PlayCount == -1 ? "∞" : _playbackViewModel.PlayCount.ToString();
                SetPlayCountButtonContent(text);
                BtnPlayCount.Focus();
            }

            e.Handled = true;
        }
    }
}

