using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 动画设置侧边面板
    /// </summary>
    public partial class AnimationSettingsPanel : System.Windows.Controls.UserControl
    {
        private DraggableTextBox _targetTextBox;
        private bool _animationEnabled = false;
        private double _animationOpacity = 0.0; // 透明度（0.0-1.0）
        private int _animationDuration = 300; // 毫秒

        // 透明度选项（0.0-1.0）
        private readonly double[] _opacityOptions = new double[]
        {
            0.0, 0.1, 0.2, 0.3, 0.4,
            0.5, 0.6, 0.7, 0.8, 0.9
        };

        // 动画时长选项（毫秒）
        private readonly int[] _animationDurations = new int[]
        {
            100, 200, 300, 400,
            500, 600, 800, 1000
        };

        public event EventHandler AnimationSettingsChanged;

        public AnimationSettingsPanel()
        {
            InitializeComponent();
            InitializeAnimationButtons();
            UpdateUI();
        }

        public void BindTarget(DraggableTextBox textBox)
        {
            _targetTextBox = textBox;

            // 从文本框读取当前动画设置
            if (_targetTextBox != null && _targetTextBox.Data != null)
            {
                // 这里可以从 Data 中读取动画设置，如果还没有这个属性，先使用默认值
                // _animationEnabled = _targetTextBox.Data.AnimationEnabled;
                // _animationType = _targetTextBox.Data.AnimationType;
                // _animationDuration = _targetTextBox.Data.AnimationDuration;

                UpdateUI();
            }
        }

        /// <summary>
        /// 初始化动画按钮
        /// </summary>
        private void InitializeAnimationButtons()
        {
            // 初始化透明度按钮
            OpacityGrid.Children.Clear();
            foreach (var opacity in _opacityOptions)
            {
                var btn = CreateOpacityButton(opacity);
                OpacityGrid.Children.Add(btn);
            }

            // 初始化动画时长按钮
            AnimationDurationGrid.Children.Clear();
            foreach (var duration in _animationDurations)
            {
                var btn = CreateDurationButton(duration);
                AnimationDurationGrid.Children.Add(btn);
            }
        }

        /// <summary>
        /// 创建透明度按钮
        /// </summary>
        private System.Windows.Controls.Button CreateOpacityButton(double opacity)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = opacity == 0.0 ? "0" : opacity.ToString("0.0"),
                Width = 58,
                Height = 36,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Colors.White),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)),
                BorderThickness = new Thickness(1),
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = opacity
            };

            btn.Click += OpacityButton_Click;

            // 默认选中 0.0（完全透明）
            if (Math.Abs(opacity - 0.0) < 0.001)
            {
                btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // #2196F3
                btn.Foreground = new SolidColorBrush(Colors.White);
            }

            return btn;
        }

        /// <summary>
        /// 创建动画时长按钮
        /// </summary>
        private System.Windows.Controls.Button CreateDurationButton(int duration)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = duration.ToString(),
                Width = 75,
                Height = 36,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Colors.White),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)),
                BorderThickness = new Thickness(1),
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = duration
            };

            btn.Click += DurationButton_Click;

            // 默认选中 300ms
            if (duration == 300)
            {
                btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // #2196F3
                btn.Foreground = new SolidColorBrush(Colors.White);
            }

            return btn;
        }

        /// <summary>
        /// 透明度按钮点击
        /// </summary>
        private void OpacityButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is double opacity)
            {
                _animationOpacity = opacity;
                UpdateOpacityButtonStates();
                OnAnimationSettingsChanged();
            }
            e.Handled = true;
        }

        /// <summary>
        /// 动画时长按钮点击
        /// </summary>
        private void DurationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is int duration)
            {
                _animationDuration = duration;
                UpdateDurationButtonStates();
                OnAnimationSettingsChanged();
            }
            e.Handled = true;
        }

        /// <summary>
        /// 动画开关复选框事件
        /// </summary>
        private void AnimationEnabledCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _animationEnabled = true;
            OnAnimationSettingsChanged();
        }

        private void AnimationEnabledCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _animationEnabled = false;
            OnAnimationSettingsChanged();
        }

        /// <summary>
        /// 更新透明度按钮选中状态
        /// </summary>
        private void UpdateOpacityButtonStates()
        {
            foreach (var child in OpacityGrid.Children)
            {
                if (child is System.Windows.Controls.Button b && b.Tag is double opacity)
                {
                    if (Math.Abs(opacity - _animationOpacity) < 0.001)
                    {
                        b.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // #2196F3
                        b.Foreground = new SolidColorBrush(Colors.White);
                    }
                    else
                    {
                        b.Background = new SolidColorBrush(Colors.White);
                        b.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51));
                    }
                }
            }
        }

        /// <summary>
        /// 更新动画时长按钮选中状态
        /// </summary>
        private void UpdateDurationButtonStates()
        {
            foreach (var child in AnimationDurationGrid.Children)
            {
                if (child is System.Windows.Controls.Button b && b.Tag is int duration)
                {
                    if (duration == _animationDuration)
                    {
                        b.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // #2196F3
                        b.Foreground = new SolidColorBrush(Colors.White);
                    }
                    else
                    {
                        b.Background = new SolidColorBrush(Colors.White);
                        b.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51));
                    }
                }
            }
        }

        /// <summary>
        /// 更新UI状态
        /// </summary>
        private void UpdateUI()
        {
            AnimationEnabledCheckBox.IsChecked = _animationEnabled;
            UpdateOpacityButtonStates();
            UpdateDurationButtonStates();
        }

        /// <summary>
        /// 触发动画设置改变事件
        /// </summary>
        private void OnAnimationSettingsChanged()
        {
            AnimationSettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 获取当前动画设置
        /// </summary>
        public (bool enabled, double opacity, int duration) GetAnimationSettings()
        {
            return (_animationEnabled, _animationOpacity, _animationDuration);
        }

        /// <summary>
        /// 设置动画设置（用于加载全局设置）
        /// </summary>
        public void SetAnimationSettings(bool enabled, double opacity, int duration)
        {
            _animationEnabled = enabled;
            _animationOpacity = opacity;
            _animationDuration = duration;
            UpdateUI();
        }
    }
}

