using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 间距设置侧边面板
    /// </summary>
    public partial class SpacingSettingsPanel : System.Windows.Controls.UserControl
    {
        private DraggableTextBox _targetTextBox;
        private double _lineSpacing = 1.2;
        private double _letterSpacing = 0.0;

        // 行间距选项 (4x4 = 16个)
        private readonly double[] _lineSpacingOptions = new double[]
        {
            1.0, 1.1, 1.2, 1.3,
            1.4, 1.5, 1.6, 1.7,
            1.8, 1.9, 2.0, 2.1,
            2.2, 2.3, 2.4, 2.5
        };

        // 字间距选项 (4x5 = 20个)
        private readonly double[] _letterSpacingOptions = new double[]
        {
            0.00, 0.01, 0.02, 0.04,
            0.06, 0.08, 0.10, 0.12,
            0.14, 0.16, 0.18, 0.20,
            0.22, 0.24, 0.26, 0.28,
            0.30, 0.32, 0.34, 0.36
        };

        public SpacingSettingsPanel()
        {
            InitializeComponent();
            InitializeSpacingButtons();
        }

        public void BindTarget(DraggableTextBox textBox)
        {
            _targetTextBox = textBox;

            // 从文本框读取当前间距设置
            if (_targetTextBox != null && _targetTextBox.Data != null)
            {
                _lineSpacing = _targetTextBox.Data.LineSpacing;
                _letterSpacing = _targetTextBox.Data.LetterSpacing;

                // 更新按钮选中状态
                UpdateLineSpacingButtonStates();
                UpdateLetterSpacingButtonStates();
            }
        }

        /// <summary>
        /// 初始化间距按钮
        /// </summary>
        private void InitializeSpacingButtons()
        {
            // 初始化行间距按钮
            LineSpacingGrid.Children.Clear();
            foreach (var spacing in _lineSpacingOptions)
            {
                var btn = CreateSpacingButton(spacing.ToString("0.0"), spacing, true);
                LineSpacingGrid.Children.Add(btn);
            }

            // 初始化字间距按钮
            LetterSpacingGrid.Children.Clear();
            foreach (var spacing in _letterSpacingOptions)
            {
                var btn = CreateSpacingButton(spacing.ToString("0.00"), spacing, false);
                LetterSpacingGrid.Children.Add(btn);
            }
        }

        /// <summary>
        /// 创建间距按钮
        /// </summary>
        private System.Windows.Controls.Button CreateSpacingButton(string label, double value, bool isLineSpacing)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = label,
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
                Tag = value
            };

            if (isLineSpacing)
            {
                btn.Click += LineSpacingButton_Click;
                // 默认选中 1.2
                if (Math.Abs(value - 1.2) < 0.01)
                {
                    btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // #2196F3
                    btn.Foreground = new SolidColorBrush(Colors.White);
                }
            }
            else
            {
                btn.Click += LetterSpacingButton_Click;
                // 默认选中 0.00
                if (Math.Abs(value - 0.0) < 0.001)
                {
                    btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // #2196F3
                    btn.Foreground = new SolidColorBrush(Colors.White);
                }
            }

            return btn;
        }

        /// <summary>
        /// 行间距按钮点击
        /// </summary>
        private void LineSpacingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is double value)
            {
                _lineSpacing = value;
                UpdateLineSpacingButtonStates();
                ApplySpacingStyle();
            }
        }

        /// <summary>
        /// 字间距按钮点击
        /// </summary>
        private void LetterSpacingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is double value)
            {
                _letterSpacing = value;
                UpdateLetterSpacingButtonStates();
                ApplySpacingStyle();
            }
        }

        /// <summary>
        /// 更新行间距按钮选中状态
        /// </summary>
        private void UpdateLineSpacingButtonStates()
        {
            foreach (var child in LineSpacingGrid.Children)
            {
                if (child is System.Windows.Controls.Button b && b.Tag is double value)
                {
                    if (Math.Abs(value - _lineSpacing) < 0.01)
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
        /// 更新字间距按钮选中状态
        /// </summary>
        private void UpdateLetterSpacingButtonStates()
        {
            foreach (var child in LetterSpacingGrid.Children)
            {
                if (child is System.Windows.Controls.Button b && b.Tag is double value)
                {
                    if (Math.Abs(value - _letterSpacing) < 0.001)
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
        /// 应用完整间距样式到文本框（支持选中文本）
        /// </summary>
        private void ApplySpacingStyle()
        {
            if (_targetTextBox == null)
                return;

            // 🆕 间距样式暂时只应用到整个文本框（行间距和字间距是全局属性）
            _targetTextBox.ApplyStyle(
                lineSpacing: _lineSpacing,
                letterSpacing: _letterSpacing
            );
        }

        private void ApplyLineSpacing()
        {
            ApplySpacingStyle();
        }

        private void ApplyLetterSpacing()
        {
            ApplySpacingStyle();
        }
    }
}

