using System;
using System.Windows;
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
                _imageProcessor.OriginalDisplayModeValue = _originalDisplayMode;
                
                // 加载缩放比例
                _currentZoom = _configManager.ZoomRatio;
                
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
                
                // 加载菜单栏字号
                ApplyMenuFontSize(_configManager.MenuFontSize);
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载设置失败: {ex.Message}");
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
                
                // 保存缩放比例
                _configManager.ZoomRatio = _currentZoom;
                
                // 使用 ConfigManager 的统一方法保存目标颜色
                _configManager.SetCurrentColor(_currentTargetColor.Red, _currentTargetColor.Green, _currentTargetColor.Blue, _currentTargetColorName);
                
                // 保存菜单栏字号（在ApplyMenuFontSize中已保存到_configManager）
                _configManager.SaveConfig();
                
                // System.Diagnostics.Debug.WriteLine($"✅ 已保存设置到 config.json (颜色: {_currentTargetColorName})");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置文件夹字号
        /// </summary>
        private void SetFolderFontSize(double size)
        {
            _configManager.FolderFontSize = size;
            OnPropertyChanged(nameof(FolderFontSize));
            ShowStatus($"✅ 文件夹字号已设置为: {size}");
        }

        /// <summary>
        /// 设置文件字号
        /// </summary>
        private void SetFileFontSize(double size)
        {
            _configManager.FileFontSize = size;
            OnPropertyChanged(nameof(FileFontSize));
            ShowStatus($"✅ 文件字号已设置为: {size}");
        }

        /// <summary>
        /// 设置文件夹标签字号（搜索结果显示）
        /// </summary>
        private void SetFolderTagFontSize(double size)
        {
            _configManager.FolderTagFontSize = size;
            OnPropertyChanged(nameof(FolderTagFontSize));
            ShowStatus($"✅ 文件夹标签字号已设置为: {size}");
        }
        
        /// <summary>
        /// 应用菜单栏字号设置 - 按照Python版本字号设计
        /// </summary>
        private void ApplyMenuFontSize(double fontSize)
        {
            // 限制范围：18-40，按照Python版本的字号范围
            fontSize = Math.Max(18, Math.Min(40, fontSize));
            
            // Python版本逻辑：主字号22，菜单字号=主字号*0.8=17.6≈18
            // 这里fontSize是主字号，实际显示字号是fontSize*0.8
            double displayFontSize = fontSize * 0.8;
            
            // 获取屏幕信息进行自适应调整
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var dpiScale = SystemParameters.PrimaryScreenWidth / 1920.0; // 以1920为基准
            
            // 根据屏幕尺寸调整字号
            double adaptiveFontSize = CalculateAdaptiveFontSize(displayFontSize, screenWidth, dpiScale);
            
            // 计算按钮尺寸参数（使用主字号计算，按Python版本逻辑）
            var buttonParams = CalculateButtonParameters(fontSize, adaptiveFontSize, screenWidth);
            
            // 更新所有菜单按钮
            var menuButtons = new[]
            {
                BtnImport, BtnProjection, BtnSync, BtnReset, BtnOriginal, BtnZoomReset, BtnColorEffect,
                BtnAddKeyframe, BtnClearKeyframes, BtnPrevKeyframe, BtnNextKeyframe,
                BtnPlay, BtnPlayCount, BtnRecord, BtnScript, BtnClearTiming, BtnPauseResume, BtnContact,
                // 富文本编辑器相关按钮
                BtnAddText, BtnBackgroundImage, BtnBackgroundColor, BtnSplitView, BtnSplitStretchMode,
                BtnDecreaseFontSize, BtnIncreaseFontSize, BtnBold, BtnTextColor, BtnSaveTextProject,
                BtnUpdateProjection, BtnCloseTextEditorInPanel
            };
            
            foreach (var btn in menuButtons)
            {
                if (btn != null)
                {
                    // 应用自适应字体大小
                    btn.FontSize = adaptiveFontSize;
                    
                    // 应用自适应按钮尺寸
                    btn.Height = buttonParams.Height;
                    btn.Padding = buttonParams.Padding;
                    btn.Margin = buttonParams.Margin;
                    
                    // 确保按钮垂直居中对齐
                    btn.VerticalAlignment = VerticalAlignment.Center;
                    
                    #if DEBUG
                    // System.Diagnostics.Debug.WriteLine($"🔘 按钮 {btn.Content}: 高度={btn.Height:F1}, Padding={buttonParams.Padding}, Background={btn.Background}, VerticalAlignment={btn.VerticalAlignment}");
                    #endif
                }
            }
            
            // 调整倒计时Border的高度和字号，使其与按钮对齐
            if (CountdownBorder != null && CountdownText != null)
            {
                CountdownBorder.Height = buttonParams.Height;
                // 倒计时字号与按钮字号相同，确保清晰可读
                CountdownText.FontSize = Math.Max(14, adaptiveFontSize);
                
                #if DEBUG
                // System.Diagnostics.Debug.WriteLine($"⏱️ 倒计时Border: 高度={CountdownBorder.Height:F1}, 字号={CountdownText.FontSize:F1}, VerticalAlignment={CountdownBorder.VerticalAlignment}");
                #endif
            }
            
            // 调整菜单栏高度（传入主字号，根据比例放大）
            AdjustMenuBarHeight(fontSize);
            
            // 保存到配置
            _configManager.MenuFontSize = fontSize;
            
            #if DEBUG
            // System.Diagnostics.Debug.WriteLine($"✅ 应用Python风格字号: 主字号={fontSize}, 显示字号={displayFontSize:F1}, 自适应={adaptiveFontSize:F1}, 屏幕宽度={screenWidth}, DPI缩放={dpiScale:F2}");
            #endif
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
            // System.Diagnostics.Debug.WriteLine($"📏 调整菜单栏高度: {menuBarHeight:F1} (字号: {baseFontSize}, 缩放比例: {fontScale:F2})");
            #endif
        }
        
        /// <summary>
        /// 设置菜单字号
        /// </summary>
        private void SetMenuFontSize(double size)
        {
            ApplyMenuFontSize(size);
            ShowStatus($"✅ 菜单字号已设置为: {size}");
        }
        
        /// <summary>
        /// 初始化自适应字体系统
        /// </summary>
        private void InitializeAdaptiveFontSystem()
        {
            try
            {
                // 获取当前配置的字号
                double currentFontSize = _configManager.MenuFontSize;
                
                // 应用自适应字体设置
                ApplyMenuFontSize(currentFontSize);
                
                #if DEBUG
                // System.Diagnostics.Debug.WriteLine($"🎨 自适应字体系统初始化完成，字号: {currentFontSize}");
                #endif
            }
            catch
            {
                #if DEBUG
                // System.Diagnostics.Debug.WriteLine($"❌ 自适应字体系统初始化失败");
                #endif
            }
        }

        #endregion
    }
}

