using System;
using System.Windows;
using ImageColorChanger.Core;
using SkiaSharp;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 设置管理

        /// <summary>
        /// 加载用户设置 - 从 config.json
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                // 从 ConfigManager 加载原图显示模式
                _originalDisplayMode = _configManager.OriginalDisplayMode;
                int topScale = Math.Max(60, Math.Min(100, _configManager.OriginalTopScalePercent));
                _originalTopScalePercent = 60 + (int)Math.Round((topScale - 60) / 5.0) * 5;
                _imageProcessor.OriginalDisplayModeValue = _originalDisplayMode;
                _imageProcessor.OriginalTopScalePercent = _originalTopScalePercent;

                // 加载分割图片显示模式偏好（默认置顶）
                _splitImageDisplayModePreference = SplitImageDisplayModePreference.ResolveInitialPreference(
                    _configManager.SplitImageDisplayMode);
                _splitImageDisplayMode = _splitImageDisplayModePreference;
                
                // 加载缩放比例
                _currentZoom = _configManager.ZoomRatio;
                _originalModeZoomRatio = Math.Max(MinZoom, Math.Min(MaxZoom, _configManager.OriginalModeZoomRatio));
                
                // 加载目标颜色
                _currentTargetColor = new SKColor(
                    _configManager.TargetColorR,
                    _configManager.TargetColorG,
                    _configManager.TargetColorB
                );
                _currentTargetColorName = _configManager.TargetColorName ?? "淡黄";
                
                // 加载导航栏宽度
                if (NavigationPanelColumn != null)
                {
                    NavigationPanelColumn.Width = new GridLength(_configManager.NavigationPanelWidth);
                }
                
                // 加载圣经历史记录区域高度（在Window_Loaded中恢复）
                
                // 加载顶部菜单和编辑器工具栏字号（已解耦）
                ApplyTopMenuFontSize(_configManager.TopMenuFontSize);
                ApplyEditorToolbarFontSize(_configManager.EditorToolbarFontSize);
                
                // 加载投影动画设置
                LoadProjectionAnimationSettings();
                LoadBiblePopupAnimationSettings();
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" 加载设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载投影动画设置
        /// </summary>
        private void LoadProjectionAnimationSettings()
        {
            try
            {
                _projectionAnimationEnabled = _configManager.ProjectionAnimationEnabled;
                _projectionAnimationOpacity = _configManager.ProjectionAnimationOpacity;
                _projectionAnimationDuration = _configManager.ProjectionAnimationDuration;
            }
            catch (Exception)
            {
                // 使用默认值
                _projectionAnimationEnabled = true;   //  默认启用
                _projectionAnimationOpacity = 0.1;    //  默认透明度 0.1
                _projectionAnimationDuration = 800;    //  默认动画时长 800ms
            }
        }

        /// <summary>
        /// 加载圣经弹窗动画设置
        /// </summary>
        private void LoadBiblePopupAnimationSettings()
        {
            try
            {
                _biblePopupAnimationEnabled = _configManager.BiblePopupAnimationEnabled;
                _biblePopupAnimationOpacity = Math.Clamp(_configManager.BiblePopupAnimationOpacity, 0.0, 1.0);
                _biblePopupAnimationDuration = Math.Clamp(_configManager.BiblePopupAnimationDuration, 100, 3000);
                _biblePopupAnimationType = _configManager.BiblePopupAnimationType;
            }
            catch (Exception)
            {
                _biblePopupAnimationEnabled = true;
                _biblePopupAnimationOpacity = 0.1;
                _biblePopupAnimationDuration = 800;
                _biblePopupAnimationType = "TopReveal";
            }
        }

        /// <summary>
        /// 保存用户设置 - 到 config.json
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                // 保存原图显示模式到 ConfigManager
                _configManager.OriginalDisplayMode = _originalDisplayMode;
                _configManager.OriginalTopScalePercent = _originalTopScalePercent;

                // 保存分割图片显示模式偏好到 ConfigManager
                _configManager.SplitImageDisplayMode = _splitImageDisplayModePreference;
                
                // 保存缩放比例
                _configManager.ZoomRatio = _currentZoom;
                _configManager.OriginalModeZoomRatio = _originalModeZoomRatio;
                
                // 使用 ConfigManager 的统一方法保存目标颜色
                _configManager.SetCurrentColor(_currentTargetColor.Red, _currentTargetColor.Green, _currentTargetColor.Blue, _currentTargetColorName);
                
                // 保存字号设置（在 ApplyTopMenuFontSize / ApplyEditorToolbarFontSize 中已保存到 _configManager）
                _configManager.SaveConfig();
                
                // System.Diagnostics.Debug.WriteLine($" 已保存设置到 config.json (颜色: {_currentTargetColorName})");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" 保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置文件夹字号
        /// </summary>
        private void SetFolderFontSize(double size)
        {
            _configManager.FolderFontSize = size;
            OnPropertyChanged(nameof(FolderFontSize));
            ShowStatus($"文件夹字号已设置为: {size}");
        }

        /// <summary>
        /// 设置文件字号
        /// </summary>
        private void SetFileFontSize(double size)
        {
            _configManager.FileFontSize = size;
            OnPropertyChanged(nameof(FileFontSize));
            ShowStatus($"文件字号已设置为: {size}");
        }

        /// <summary>
        /// 设置文件夹标签字号（搜索结果显示）
        /// </summary>
        private void SetFolderTagFontSize(double size)
        {
            _configManager.FolderTagFontSize = size;
            OnPropertyChanged(nameof(FolderTagFontSize));
            ShowStatus($"文件夹标签字号已设置为: {size}");
        }
        
        /// <summary>
        /// 应用顶部菜单字号（只影响顶部菜单）
        /// </summary>
        private void ApplyTopMenuFontSize(double fontSize)
        {
            // 限制范围：12-40
            fontSize = Math.Max(12, Math.Min(40, fontSize));

            double displayFontSize = fontSize * 0.8;
            var screenWidth = MainWindowRoot?.ActualWidth > 0 ? MainWindowRoot.ActualWidth : SystemParameters.PrimaryScreenWidth;
            var dpiScale = screenWidth / 1920.0;
            double adaptiveFontSize = CalculateAdaptiveFontSize(displayFontSize, screenWidth, dpiScale);
            var buttonParams = CalculateButtonParameters(fontSize, adaptiveFontSize, screenWidth);

            foreach (var btn in GetTopMenuButtons())
            {
                if (btn == null)
                {
                    continue;
                }

                btn.FontSize = adaptiveFontSize;
                btn.Height = buttonParams.Height;
                btn.Padding = buttonParams.Padding;
                btn.Margin = buttonParams.Margin;
                btn.VerticalAlignment = VerticalAlignment.Center;
            }

            if (CountdownBorder != null && CountdownText != null)
            {
                CountdownBorder.Height = buttonParams.Height;
                CountdownText.FontSize = Math.Max(14, adaptiveFontSize);
            }

            AdjustMenuBarHeight(fontSize);
            _configManager.TopMenuFontSize = fontSize;
        }

        /// <summary>
        /// 应用编辑器工具栏字号（只影响文本编辑器工具栏）
        /// </summary>
        private void ApplyEditorToolbarFontSize(double fontSize)
        {
            fontSize = Math.Max(12, Math.Min(40, fontSize));

            double displayFontSize = fontSize * 0.8;
            var screenWidth = MainWindowRoot?.ActualWidth > 0 ? MainWindowRoot.ActualWidth : SystemParameters.PrimaryScreenWidth;
            var dpiScale = screenWidth / 1920.0;
            double adaptiveFontSize = CalculateAdaptiveFontSize(displayFontSize, screenWidth, dpiScale);
            var buttonParams = CalculateButtonParameters(fontSize, adaptiveFontSize, screenWidth);

            double textEditorScale = 0.75;
            foreach (var btn in GetTextEditorToolbarButtons())
            {
                if (btn == null)
                {
                    continue;
                }

                btn.FontSize = adaptiveFontSize * textEditorScale;
                btn.Height = buttonParams.Height * textEditorScale;

                if (btn == BtnIncreaseFontSize || btn == BtnDecreaseFontSize || btn == BtnBold || btn == BtnTextColor)
                {
                    btn.Margin = new Thickness(0);
                    btn.Padding = new Thickness(2);
                }
                else
                {
                    btn.Padding = new Thickness(
                        buttonParams.Padding.Left * textEditorScale,
                        buttonParams.Padding.Top * textEditorScale,
                        buttonParams.Padding.Right * textEditorScale,
                        buttonParams.Padding.Bottom * textEditorScale);
                    btn.Margin = new Thickness(
                        buttonParams.Margin.Left * textEditorScale,
                        buttonParams.Margin.Top * textEditorScale,
                        buttonParams.Margin.Right * textEditorScale,
                        buttonParams.Margin.Bottom * textEditorScale);
                }

                btn.VerticalAlignment = VerticalAlignment.Center;
            }

            _configManager.EditorToolbarFontSize = fontSize;
        }

        /// <summary>
        /// 兼容旧入口：菜单字号仅调整顶部菜单。
        /// </summary>
        private void ApplyMenuFontSize(double fontSize)
        {
            ApplyTopMenuFontSize(fontSize);
        }

        private System.Collections.Generic.List<System.Windows.Controls.Button> GetTopMenuButtons()
        {
            var buttons = new System.Collections.Generic.List<System.Windows.Controls.Button>();
            void CollectButtons(System.Windows.Controls.Panel panel)
            {
                if (panel == null)
                {
                    return;
                }

                foreach (var child in panel.Children)
                {
                    if (child is System.Windows.Controls.Button button)
                    {
                        buttons.Add(button);
                    }
                }
            }

            CollectButtons(TopMenuOverflowPanel);

            return buttons;
        }

        private System.Collections.Generic.IEnumerable<System.Windows.Controls.Button> GetTextEditorToolbarButtons()
        {
            yield return BtnAddText;
            yield return BtnBackgroundImage;
            yield return BtnBackgroundColor;
            yield return BtnSplitView;
            yield return BtnSplitStretchMode;
            yield return BtnSlideOutputMode;
            yield return BtnDecreaseFontSize;
            yield return BtnIncreaseFontSize;
            yield return BtnBold;
            yield return BtnTextColor;
            yield return BtnSaveTextProject;
            yield return BtnLockProjection;
            yield return BtnUpdateProjection;
            yield return BtnCloseTextEditorInPanel;
        }
        
        /// <summary>
        /// 计算自适应字号 - 按照Python版本逻辑
        /// </summary>
        private double CalculateAdaptiveFontSize(double baseFontSize, double screenWidth, double dpiScale)
        {
            // 按照Python版本：默认18号字体，字体粗细根据字号决定
            // Python版本：font_size=22, menu_font_size=18
            double adaptiveSize = baseFontSize;
            
            // 根据屏幕宽度调整（Python版本没有这个逻辑，保持简单）
            if (screenWidth < 1366) // 小屏幕（笔记本）
            {
                // 小屏幕：字号相对较大，确保可读性
                adaptiveSize = baseFontSize * 1.05;
            }
            else if (screenWidth > 2560) // 大屏幕（2K/4K）
            {
                // 大屏幕：字号相对较小，避免过大
                adaptiveSize = baseFontSize * 0.95;
            }
            
            // DPI缩放调整
            adaptiveSize *= dpiScale;
            
            // 确保字号在合理范围内（Python版本范围更大）
            adaptiveSize = Math.Max(12, Math.Min(30, adaptiveSize));
            
            return adaptiveSize;
        }
        
        /// <summary>
        /// 计算按钮参数 - 按照Python版本字号设计
        /// </summary>
        private (double Height, Thickness Padding, Thickness Margin) CalculateButtonParameters(double baseFontSize, double displayFontSize, double screenWidth)
        {
            // 按照Python版本的设计：font_size=22（主字号）, menu_font_size=18（显示字号）
            // 按钮尺寸计算基于主字号：padding_x = font_size * 0.3, padding_y = font_size * 0.2
            // 按钮高度必须足够大，确保文字不被遮挡，但Padding要小以保持紧凑
            double paddingY = 6; // 垂直内边距固定为3px，保持紧凑
            double height = displayFontSize + (paddingY * 2) + 12; // 高度 = 显示字号 + 上下内边距 + 额外12像素（增加高度）
            double paddingX = baseFontSize * 0.3; // 水平内边距 = 主字号的30%（Python版本）
            double marginX = baseFontSize * 0.35; // 按钮间距 = 主字号的35%（随字号明显放大）
            
            // 根据屏幕宽度微调
            if (screenWidth < 1366) // 小屏幕更紧凑
            {
                height *= 0.95;
                paddingX *= 0.9;
                marginX *= 0.8;
            }
            else if (screenWidth > 2560) // 大屏幕稍微宽松
            {
                height *= 1.05;
                paddingX *= 1.1;
                marginX *= 1.2;
            }
            
            // 确保最小尺寸
            height = Math.Max(height, 28);
            paddingX = Math.Max(paddingX, 4);
            marginX = Math.Max(marginX, 1);
            
            return (height, new Thickness(paddingX, paddingY, paddingX, paddingY), new Thickness(marginX, 0, marginX, 0));
        }
        
        /// <summary>
        /// 调整菜单栏高度 - 固定基础高度 + 字号比例放大
        /// </summary>
        private void AdjustMenuBarHeight(double baseFontSize)
        {
            // 菜单栏固定基础高度（22号字的标准高度）
            double baseHeight = 55; // 基础高度70px（22号字时的高度）
            
            // 根据字号比例放大
            double fontScale = baseFontSize / 22.0; // 以22号字为基准（默认字号）
            double menuBarHeight = baseHeight * fontScale;
            
            // 确保最小高度
            menuBarHeight = Math.Max(menuBarHeight, 50);
            
            // 更新菜单栏的RowDefinition高度
            if (MenuBarRow != null)
            {
                MenuBarRow.Height = new GridLength(menuBarHeight);
            }
            
            #if DEBUG
            // System.Diagnostics.Debug.WriteLine($"调整菜单栏高度: {menuBarHeight:F1} (字号: {baseFontSize}, 缩放比例: {fontScale:F2})");
            #endif
        }
        
        /// <summary>
        /// 设置菜单字号
        /// </summary>
        private void SetMenuFontSize(double size)
        {
            ApplyMenuFontSize(size);
            ShowStatus($"菜单字号已设置为: {size}");
        }
        
        /// <summary>
        /// 初始化自适应字体系统
        /// </summary>
        private void InitializeAdaptiveFontSystem()
        {
            try
            {
                double topMenuFontSize = _configManager.TopMenuFontSize;
                double editorToolbarFontSize = _configManager.EditorToolbarFontSize;

                ApplyTopMenuFontSize(topMenuFontSize);
                ApplyEditorToolbarFontSize(editorToolbarFontSize);
                
                #if DEBUG
                // System.Diagnostics.Debug.WriteLine($"自适应字体系统初始化完成，顶部菜单字号: {topMenuFontSize}, 编辑器字号: {editorToolbarFontSize}");
                #endif
            }
            catch
            {
                #if DEBUG
                // System.Diagnostics.Debug.WriteLine($" 自适应字体系统初始化失败");
                #endif
            }
        }

        #endregion
    }
}




