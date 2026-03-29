using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models.Bible;
using ImageColorChanger.Services.Interfaces;
using SkiaSharp;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSize = System.Windows.Size;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfPixelFormats = System.Windows.Media.PixelFormats;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Bible Settings
    /// </summary>
    public partial class MainWindow
    {
        #region 圣经设置


        /// <summary>
        /// 圣经导航面板设置按钮点击事件
        /// </summary>
        private void BtnBibleSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果窗口已存在且可见，则关闭它
                if (_bibleSettingsWindow != null && _bibleSettingsWindow.IsVisible)
                {
                    _bibleSettingsWindow.Close();
                    return;
                }
                
                // 创建新的设置窗口
                _bibleSettingsWindow = new BibleSettingsWindow(_configManager, _bibleService, 
                    // 译本切换回调（需要重新加载经文）
                    async () =>
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine(" [圣经设置] 译本切换，重新加载经文");
                        #endif
                        
                        // 应用设置
                        ApplyBibleSettings();
                        ApplyBibleSearchResultFontSizes();

                        //  重新加载当前章节
                        if (_isBibleMode && _currentBook > 0 && _currentChapter > 0)
                        {
                            await LoadChapterVersesAsync(_currentBook, _currentChapter);
                        }

                        // 如果投影已开启，重新渲染投影
                        if (_projectionManager != null && _projectionManager.IsProjecting)
                        {
                            bool hasLockedRecords = _historySlots.Any(x => x.IsLocked);
                            
                            if (hasLockedRecords)
                            {
                                await UpdateProjectionFromMergedVerses();
                            }
                            else
                            {
                                // 非锁定模式：更新当前章节的投影
                                RenderBibleToProjection();
                            }
                        }
                    },
                    // 样式改变回调（只刷新样式，不重新加载经文）
                    async () =>
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine("[圣经设置] 样式改变，刷新显示");
                        //#endif
                        
                        // 应用设置
                        ApplyBibleSettings();
                        ApplyBibleSearchResultFontSizes();
                        
                        // 如果投影已开启，重新渲染投影（保持当前经文和高亮状态）
                        if (_projectionManager != null && _projectionManager.IsProjecting)
                        {
                            bool hasLockedRecords = _historySlots.Any(x => x.IsLocked);
                            
                            if (hasLockedRecords)
                            {
                                await UpdateProjectionFromMergedVerses();
                            }
                            else
                            {
                                RenderBibleToProjection();
                            }
                        }
                    })
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                // 窗口关闭时清理实例
                _bibleSettingsWindow.Closed += (s, args) => 
                { 
                    _bibleSettingsWindow = null;
                    // 移除主窗口点击监听
                    this.PreviewMouseDown -= MainWindow_SettingsClose_PreviewMouseDown;
                };
                
                // 添加主窗口点击监听（点击主窗口任意位置关闭设置窗口）
                this.PreviewMouseDown -= MainWindow_SettingsClose_PreviewMouseDown; // 先移除避免重复
                this.PreviewMouseDown += MainWindow_SettingsClose_PreviewMouseDown;

                // 计算窗口位置：统一定位在设置按钮的右边
                if (_configManager.BibleSettingsWindowLeft.HasValue && _configManager.BibleSettingsWindowTop.HasValue)
                {
                    _bibleSettingsWindow.Left = _configManager.BibleSettingsWindowLeft.Value;
                    _bibleSettingsWindow.Top = _configManager.BibleSettingsWindowTop.Value;
                }
                else if (BtnBibleSettings != null)
                {
                    // 相对于主窗口定位，避免屏幕坐标转换问题
                    // 获取按钮相对于主窗口的位置
                    var buttonPos = BtnBibleSettings.TransformToAncestor(this)
                        .Transform(new System.Windows.Point(0, 0));
                    
                    // 简单定位：窗口位置 = 主窗口位置 + 按钮相对位置 + 偏移
                    _bibleSettingsWindow.Left = this.Left + buttonPos.X + BtnBibleSettings.ActualWidth + 20;
                    _bibleSettingsWindow.Top = this.Top + buttonPos.Y + 30;
                }

                // 显示窗口
                _bibleSettingsWindow.Show();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"打开设置失败：{ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 主窗口点击时关闭设置窗口（选择颜色时除外）
        /// </summary>
        private void MainWindow_SettingsClose_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_bibleSettingsWindow != null && _bibleSettingsWindow.IsVisible)
            {
                // 如果正在选择颜色，不关闭窗口
                if (_bibleSettingsWindow.IsSelectingColor)
                {
                    return;
                }
                
                _bibleSettingsWindow.Close();
            }
        }

        /// <summary>
        /// 更新底部工具栏译本选择状态
        /// </summary>
        private void UpdateBibleVersionRadioButtons()
        {
            try
            {
                var dbFileName = _configManager.BibleDatabaseFileName ?? "bible.db";
                
                if (RadioBibleVersionSimplified != null)
                    RadioBibleVersionSimplified.IsChecked = (dbFileName == "bible.db");
                
                if (RadioBibleVersionTraditional != null)
                    RadioBibleVersionTraditional.IsChecked = (dbFileName == "hehebenfanti.db");
                
                //#if DEBUG
                //Debug.WriteLine($"[圣经译本] 更新按钮状态: {dbFileName}");
                //#endif
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经译本] 更新按钮状态失败: {ex.Message}");
                //#else
                _ = ex;
                //#endif
            }
        }

        /// <summary>
        /// 底部工具栏快速切换译本
        /// </summary>
        private async void BibleVersionRadio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.RadioButton radioButton) return;
            
            try
            {
                var dbFileName = radioButton.Tag?.ToString() ?? "bible.db";
                var versionName = radioButton.Content?.ToString() ?? "和合本";
                
                // 检查是否真的切换了译本
                if (_configManager.BibleDatabaseFileName == dbFileName)
                {
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经译本] 已经是当前译本: {versionName}");
                    //#endif
                    return;
                }
                
                //#if DEBUG
                //Debug.WriteLine($"[圣经译本] 快速切换: {versionName} ({dbFileName})");
                //#endif
                
                // 保存配置
                _configManager.BibleVersion = versionName;
                _configManager.BibleDatabaseFileName = dbFileName;
                
                // 更新数据库路径
                _bibleService?.UpdateDatabasePath();
                
                // 重新加载当前章节
                if (_isBibleMode && _currentBook > 0 && _currentChapter > 0)
                {
                    await LoadChapterVersesAsync(_currentBook, _currentChapter);
                    
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经译本] 已重新加载: {BibleBookConfig.GetBook(_currentBook).Name} {_currentChapter}章");
                    //#endif
                }
                
                // 如果投影已开启，重新渲染投影
                if (_projectionManager != null && _projectionManager.IsProjecting)
                {
                    // 检查是否有锁定记录
                    bool hasLockedRecords = _historySlots.Any(x => x.IsLocked);
                    
                    if (hasLockedRecords)
                    {
                        // 锁定模式：译本切换时，更新锁定记录的投影
                        await UpdateProjectionFromMergedVerses();
                    }
                    else
                    {
                        // 非锁定模式：更新当前章节的投影
                        RenderBibleToProjection();
                    }
                }
                
                ShowStatus($"已切换到: {versionName}");
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经译本] 切换失败: {ex.Message}");
                //#else
                _ = ex;
                //#endif
                ShowStatus($"切换译本失败");
            }
        }

        /// <summary>
        /// 应用圣经设置到界面
        /// </summary>
        private void ApplyBibleSettings()
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[应用圣经样式] ========== 开始 ==========");
            //#endif

            try
            {
                // 应用背景色
                var backgroundColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleBackgroundColor);
                BibleVerseScrollViewer.Background = new WpfSolidColorBrush(backgroundColor);

                // 应用标题背景色（与经文背景色一致）
                BibleChapterTitleBorder.Background = new WpfSolidColorBrush(backgroundColor);
                if (BibleChapterTitleScrollBorder != null)
                {
                    BibleChapterTitleScrollBorder.Background = new WpfSolidColorBrush(backgroundColor);
                }

                // 应用标题样式 - 使用FontService加载字体（支持自定义字体文件）
                var titleFontFamily = Core.FontService.Instance.GetFontFamilyByFamily(_configManager.BibleFontFamily);
                if (titleFontFamily == null)
                {
                    // 回退到系统字体
                    titleFontFamily = new WpfFontFamily(_configManager.BibleFontFamily);
                }
                BibleChapterTitle.FontFamily = titleFontFamily;
                BibleChapterTitle.FontSize = _configManager.BibleTitleFontSize;
                var titleColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleTitleColor);
                BibleChapterTitle.Foreground = new WpfSolidColorBrush(titleColor);
                ApplyBibleTitleDisplayMode(true);

                // 应用经文样式到已生成的项
                ApplyVerseStyles();

                //#if DEBUG
                //Debug.WriteLine("[圣经] 界面样式已更新");
                //#endif
            }
            catch (Exception)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经] 应用设置失败: {ex.Message}");
                //#endif
            }
        }

        private void ApplyBibleTitleDisplayMode(bool visible)
        {
            bool fixedTitle = _configManager?.BibleFixedTitle ?? true;

            if (BibleChapterTitleBorder != null)
            {
                BibleChapterTitleBorder.Visibility =
                    fixedTitle && visible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (BibleChapterTitleScrollBorder != null)
            {
                BibleChapterTitleScrollBorder.Visibility =
                    !fixedTitle && visible ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateBibleScrollViewerPaddingForTitle(fixedTitle, visible);
        }

        private void UpdateBibleScrollViewerPaddingForTitle(bool fixedTitle, bool visible)
        {
            if (BibleVerseScrollViewer == null)
            {
                return;
            }

            double topInset = 0d;
            if (fixedTitle && visible && BibleChapterTitleBorder != null)
            {
                BibleChapterTitleBorder.UpdateLayout();
                topInset = BibleChapterTitleBorder.ActualHeight;
                if (topInset <= 0)
                {
                    double fallbackFont = _configManager?.BibleTitleFontSize ?? 32d;
                    topInset = Math.Max(60d, fallbackFont + 30d);
                }
            }

            var current = BibleVerseScrollViewer.Padding;
            if (Math.Abs(current.Top - topInset) > 0.5d)
            {
                BibleVerseScrollViewer.Padding = new Thickness(
                    current.Left,
                    topInset,
                    current.Right,
                    current.Bottom);
            }
        }

        /// <summary>
        /// 应用经文样式到列表项（主屏幕）
        /// 支持两种模式：
        /// 1. 锁定模式：第一条记录是标题行（Verse=0），使用不同的margin
        /// 2. 非锁定模式：所有记录都是普通经文，使用统一的margin
        /// </summary>
        private void ApplyVerseStyles()
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"✨ [应用经文样式] ========== 开始 ==========");
            //#endif

            try
            {
                if (BibleVerseList.Items.Count == 0)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [ApplyVerseStyles] 列表为空，跳过样式应用");
                    //#endif
                    return;
                }

                // ========================================
                // 模式判断
                // ========================================
                var firstVerse = BibleVerseList.Items[0] as BibleVerse;
                bool isLockedMode = firstVerse != null && firstVerse.Verse == 0;

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[ApplyVerseStyles] 开始应用样式，总共 {BibleVerseList.Items.Count} 条记录，模式={isLockedMode}");
                //#endif

                var textColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleTextColor);
                var verseNumberColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleVerseNumberColor);

                // 使用FontService加载字体（支持自定义字体文件）
                var fontFamily = Core.FontService.Instance.GetFontFamilyByFamily(_configManager.BibleFontFamily);
                if (fontFamily == null)
                {
                    // 回退到系统字体
                    fontFamily = new WpfFontFamily(_configManager.BibleFontFamily);
                }

                // 遍历所有已生成的容器
                for (int i = 0; i < BibleVerseList.Items.Count; i++)
                {
                    var container = BibleVerseList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (container == null)
                    {
                        #if DEBUG
                        var tempVerse = BibleVerseList.Items[i] as BibleVerse;
                        if (tempVerse != null && tempVerse.IsHighlighted)
                        {
                            System.Diagnostics.Debug.WriteLine($" [ApplyVerseStyles] 容器{i}未生成（null），但该经文被高亮: {tempVerse.Reference}");
                        }
                        #endif
                        continue;
                    }

                    var verse = BibleVerseList.Items[i] as BibleVerse;
                    if (verse == null)
                        continue;
                    
                    //#if DEBUG
                    //if (verse.IsHighlighted)
                    //{
                    //    System.Diagnostics.Debug.WriteLine($"[ApplyVerseStyles] 处理高亮经文{i}: {verse.Reference}");
                    //}
                    //#endif

                    // 查找单个 TextBlock（新布局）
                    var verseTextBlock = FindVisualChild<TextBlock>(container);
                    if (verseTextBlock != null)
                    {
                        // 清空并重新构建 Inlines
                        verseTextBlock.Inlines.Clear();
                        verseTextBlock.FontFamily = fontFamily;
                        
                        // ========================================
                        // 锁定模式：渲染标题行
                        // ========================================
                        if (verse.Verse == 0)
                        {
                            // 标题行：只显示标题文本，不显示节号
                            var titleColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleTitleColor);
                            
                            verseTextBlock.FontSize = _configManager.BibleTitleFontSize;
                            verseTextBlock.FontWeight = FontWeights.Bold;
                            
                            var titleRun = new System.Windows.Documents.Run
                            {
                                Text = verse.Scripture, // 标题文本存储在Scripture字段
                                Foreground = new WpfSolidColorBrush(titleColor)
                            };
                            verseTextBlock.Inlines.Add(titleRun);
                        }
                        else
                        {
                            // ========================================
                            // 渲染普通经文行（锁定模式和非锁定模式通用）
                            // ========================================
                            verseTextBlock.FontSize = _configManager.BibleFontSize;
                            verseTextBlock.FontWeight = FontWeights.Normal;
                            
                            // 根据高亮状态选择颜色（只影响经文内容，不影响节号）
                            WpfColor scriptureColor = textColor;
                            if (verse.IsHighlighted)
                            {
                                var highlightColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleHighlightColor);
                                scriptureColor = highlightColor;
                                
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"✨ [圣经主屏] 应用高亮颜色到经文内容: {verse.Reference}");
                                //System.Diagnostics.Debug.WriteLine($"   - 配置高亮颜色: {_configManager.BibleHighlightColor}");
                                //System.Diagnostics.Debug.WriteLine($"   - 转换后颜色: R={highlightColor.R}, G={highlightColor.G}, B={highlightColor.B}, A={highlightColor.A}");
                                //System.Diagnostics.Debug.WriteLine($"   - 默认经文颜色: {_configManager.BibleTextColor}");
                                //#endif
                            }
                            //#if DEBUG
                            //else
                            //{
                            //    System.Diagnostics.Debug.WriteLine($"[圣经主屏] 经文{i}使用默认颜色: {verse.Reference}");
                            //}
                            //#endif

                            // 添加节号（作为第一个 Run）- 节号始终使用独立的节号颜色
                            var verseNumberRun = new System.Windows.Documents.Run
                            {
                                Text = verse.VerseNumberText + " ",
                                FontFamily = fontFamily,
                                FontSize = _configManager.BibleVerseNumberFontSize,
                                FontWeight = FontWeights.Bold,
                                Foreground = new WpfSolidColorBrush(verseNumberColor) // 节号始终使用独立颜色
                            };
                            verseTextBlock.Inlines.Add(verseNumberRun);

                            // 添加经文内容（处理格式标记）
                            var scripture = verse.Scripture ?? "";
                            
                            // 检查是否有格式标记
                            var pattern = @"<u>(.*?)</u>";
                            var matches = System.Text.RegularExpressions.Regex.Matches(scripture, pattern);
                            
                            if (matches.Count == 0)
                            {
                                // 没有格式标记，直接添加
                                var scriptureRun = new System.Windows.Documents.Run
                                {
                                    Text = scripture,
                                    Foreground = new WpfSolidColorBrush(scriptureColor)
                                };
                                verseTextBlock.Inlines.Add(scriptureRun);
                            }
                            else
                            {
                                // 有格式标记，移除标记后添加（简化处理）
                                var cleanText = Utils.TextFormatHelper.StripHtmlTags(scripture);
                                var scriptureRun = new System.Windows.Documents.Run
                                {
                                    Text = cleanText,
                                    Foreground = new WpfSolidColorBrush(scriptureColor)
                                };
                                verseTextBlock.Inlines.Add(scriptureRun);
                            }
                        }
                    }
                    
                    // ========================================
                    // 设置Border的Margin（节间距）
                    // ========================================
                    var border = FindVisualChild<Border>(container);
                    if (border != null)
                    {
                        // 锁定模式：标题行使用更大的间距（记录之间的分隔）
                        if (verse.Verse == 0)
                        {
                            // 第一个标题行：顶部间距为0（置顶显示）
                            // 后续标题行：顶部间距固定为60（作为记录分隔，不随节距变化）
                            double topMargin = (i == 0) ? 0 : 60;
                            // 标题底部间距固定为15，不随节距变化
                            border.Margin = new Thickness(0, topMargin, 0, 15);
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[主屏标题Margin] i={i}, 节距配置={_configManager.BibleVerseSpacing}, topMargin={topMargin}(固定), 底部固定=15, 实际Margin={border.Margin}");
                            //#endif
                        }
                        else
                        {
                            // 普通经文行：第一节经文上边距固定为0（与标题间距由XAML中的Border控制），其他经文使用配置的节距
                            double topMargin = (i == 0 || (i == 1 && _mergedVerses.Count > 0 && _mergedVerses[0].Verse == 0)) 
                                ? 0  // 第一节经文：上边距为0
                                : _configManager.BibleVerseSpacing / 2;  // 其他经文：使用配置的节距
                            
                            border.Margin = new Thickness(0, topMargin, 0, _configManager.BibleVerseSpacing / 2);
                            
                            //#if DEBUG
                            //if (i <= 1) // 输出前两个经文的调试信息
                            //{
                            //    System.Diagnostics.Debug.WriteLine($"[主屏经文Margin] i={i}, 第{verse.Verse}节, 节距配置={_configManager.BibleVerseSpacing}, topMargin={topMargin}, 实际Margin={border.Margin}");
                            //}
                            //#endif
                        }
                        
                        //#if DEBUG
                        //if (i == 0) // 只输出第一个经文的调试信息
                        //{
                        //    Debug.WriteLine($"");
                        //    Debug.WriteLine($"[圣经样式应用]");
                        //    Debug.WriteLine($"   字体大小: {_configManager.BibleFontSize}px");
                        //    Debug.WriteLine($"   节间距配置: {_configManager.BibleVerseSpacing}px");
                        //    Debug.WriteLine($"   Border Margin: {border.Margin} (上下各{_configManager.BibleVerseSpacing / 2}px)");
                        //    Debug.WriteLine($"   说明: 节间距控制经文之间的间距");
                        //    Debug.WriteLine($"");
                        //}
                        //#endif
                    }
                }

                // 更新边距
                BibleVerseList.Margin = new Thickness(_configManager.BibleMargin, 0, _configManager.BibleMargin, 0);

                //#if DEBUG
                //Debug.WriteLine($"[圣经] 已应用样式到 {BibleVerseList.Items.Count} 个经文项");
                //#endif
            }
            catch (Exception)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经] 应用经文样式失败: {ex.Message}");
                //#endif
            }
        }

        #endregion

    }
}



