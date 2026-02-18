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
    /// MainWindow Bible Navigation (Navigation, Highlighting, Projection, Toolbar)
    /// </summary>
    public partial class MainWindow
    {
        #region 圣经导航

        /// <summary>
        /// 上一节
        /// </summary>
        private async Task NavigateToPreviousVerseAsync()
        {
            if (_currentVerse > 1)
            {
                _currentVerse--;
            }
            else if (_currentChapter > 1)
            {
                // 跳转到上一章的最后一节
                _currentChapter--;
                _currentVerse = await _bibleService.GetVerseCountAsync(_currentBook, _currentChapter);
            }
            else
                {
                    // 已经是第一节，不操作
                    //#if DEBUG
                    //Debug.WriteLine("[圣经] 已经是第一节");
                    //#endif
                    return;
                }

            await LoadAndDisplayCurrentVerseAsync();
        }

        /// <summary>
        /// 下一节
        /// </summary>
        private async Task NavigateToNextVerseAsync()
        {
            var maxVerse = await _bibleService.GetVerseCountAsync(_currentBook, _currentChapter);

            if (_currentVerse < maxVerse)
            {
                _currentVerse++;
            }
            else
            {
                // 跳转到下一章第1节
                var maxChapter = _bibleService.GetChapterCount(_currentBook);
                if (_currentChapter < maxChapter)
                {
                    _currentChapter++;
                    _currentVerse = 1;
                }
                else
                {
                    // 已经是最后一节，不操作
                    //#if DEBUG
                    //Debug.WriteLine("[圣经] 已经是最后一节");
                    //#endif
                    return;
                }
            }

            await LoadAndDisplayCurrentVerseAsync();
        }

        /// <summary>
        /// 导航到相邻经文（上一节/下一节）
        /// </summary>
        /// <param name="offset">偏移量：-1=上一节, +1=下一节</param>
        private async Task NavigateBibleVerseAsync(int offset)
        {
            if (offset < 0)
            {
                await NavigateToPreviousVerseAsync();
            }
            else if (offset > 0)
            {
                await NavigateToNextVerseAsync();
            }
        }

        /// <summary>
        /// 加载并显示当前经文，自动投影
        /// </summary>
        private async Task LoadAndDisplayCurrentVerseAsync()
        {
            var verse = await LoadVerseAsync(_currentBook, _currentChapter, _currentVerse);
            if (verse != null)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经] 导航到: {verse.Reference}");
                //#endif

                // 自动投影
                await ProjectBibleVerseAsync(verse);
            }
        }

        #endregion

        #region 圣经经文点击高亮

        /// <summary>
        /// 经文点击事件（单选模式：只允许一个经文高亮）
        /// </summary>
        private void BibleVerse_Click(object sender, MouseButtonEventArgs e)
        {
            var border = FindBibleVerseBorderFromEvent(e);
            if (border?.Tag is not BibleVerse clickedVerse)
                return;

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 经文点击: {clickedVerse.Reference}");
            //#endif

            // 获取所有经文（支持List和ObservableCollection）
            System.Collections.Generic.IEnumerable<BibleVerse> verses = null;
            
            if (BibleVerseList.ItemsSource is System.Collections.Generic.List<BibleVerse> list)
            {
                verses = list;
            }
            else if (BibleVerseList.ItemsSource is System.Collections.ObjectModel.ObservableCollection<BibleVerse> collection)
            {
                verses = collection;
            }
            
            if (verses == null)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [圣经点击] ItemsSource为null或类型不匹配");
                System.Diagnostics.Debug.WriteLine($"   ItemsSource类型: {BibleVerseList.ItemsSource?.GetType().Name ?? "null"}");
                #endif
                return;
            }

            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"📌 [圣经点击] 点击经文: {clickedVerse.Reference}, 当前高亮状态={clickedVerse.IsHighlighted}");
            //System.Diagnostics.Debug.WriteLine($"   ItemsSource类型: {BibleVerseList.ItemsSource.GetType().Name}");
            //#endif

            // 🔧 优化：点击切换高亮状态
            if (clickedVerse.IsHighlighted)
            {
                // 再次点击已高亮的经文，取消高亮
                clickedVerse.IsHighlighted = false;
            }
            else
            {
                // 点击未高亮的经文，先取消其他经文的高亮
                foreach (var verse in verses)
                {
                    if (verse.IsHighlighted)
                    {
                        verse.IsHighlighted = false;
                    }
                }

                // 高亮当前点击的经文
                clickedVerse.IsHighlighted = true;
            }
            
            // 统一刷新整个列表的UI（确保所有经文颜色正确）
            ApplyVerseStyles();

            // ========================================
            // 📌 重新渲染投影（区分锁定模式和非锁定模式）
            // ========================================
            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
                // 检查是否有锁定记录
                bool hasLockedRecords = _historySlots.Any(x => x.IsLocked);
                
                if (hasLockedRecords)
                {
                    // 📌 锁定模式：点击非锁定记录框中的经文，不应该影响投影
                    // 投影内容由锁定记录控制（_mergedVerses）
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[圣经] 锁定模式：点击经文不更新投影");
                    //#endif
                    // 不更新投影，保持当前锁定记录的投影状态
                }
                else
                {
                    // 📌 非锁定模式：点击经文更新投影
                    RenderBibleToProjection();
                }
            }
        }

        /// <summary>
        /// 上下键导航高亮经文
        /// </summary>
        /// <param name="direction">方向：-1=上一节，1=下一节</param>
        internal void NavigateHighlightedVerse(int direction)
        {
            // 获取所有经文
            System.Collections.Generic.IEnumerable<BibleVerse> verses = null;
            
            if (BibleVerseList.ItemsSource is System.Collections.Generic.List<BibleVerse> list)
            {
                verses = list;
            }
            else if (BibleVerseList.ItemsSource is System.Collections.ObjectModel.ObservableCollection<BibleVerse> collection)
            {
                verses = collection;
            }
            
            if (verses == null || !verses.Any())
            {
                return;
            }

            var versesList = verses.ToList();
            
            // 查找当前高亮的经文
            var currentIndex = -1;
            for (int i = 0; i < versesList.Count; i++)
            {
                if (versesList[i].IsHighlighted)
                {
                    currentIndex = i;
                    break;
                }
            }

            // 如果没有高亮的经文，从第一节开始
            if (currentIndex == -1)
            {
                if (versesList.Count > 0)
                {
                    versesList[0].IsHighlighted = true;
                    
                    // 刷新UI和投影
                    ApplyVerseStyles();
                    if (_projectionManager != null && _projectionManager.IsProjecting)
                    {
                        RenderBibleToProjection();
                    }
                }
                return;
            }

            // 计算目标索引
            var targetIndex = currentIndex + direction;
            
            // 边界检查
            if (targetIndex < 0 || targetIndex >= versesList.Count)
            {
                return; // 超出范围，不操作
            }

            // 取消当前高亮
            versesList[currentIndex].IsHighlighted = false;
            
            // 高亮目标经文
            versesList[targetIndex].IsHighlighted = true;

            // 刷新UI和投影
            ApplyVerseStyles();
            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderBibleToProjection();
            }
            
            // 滚动到可见区域
            ScrollToVerseAtIndex(targetIndex);
        }

        /// <summary>
        /// 滚动到指定索引的经文
        /// </summary>
        private void ScrollToVerseAtIndex(int index)
        {
            ScrollToVerseInstant(index);
        }

        private Border FindBibleVerseBorderFromEvent(MouseButtonEventArgs e)
        {
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null)
            {
                if (current is Border border && border.Tag is BibleVerse)
                {
                    return border;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        #endregion

        #region 圣经投影

        /// <summary>
        /// 投影当前经文（🆕 使用 VisualBrush 100%一致投影）
        /// </summary>
        private async Task ProjectBibleVerseAsync(BibleVerse verse)
        {
            if (verse == null)
                return;

            try
            {
                // 🆕 使用 VisualBrush 直接投影主屏幕内容（100%像素级一致）
                if (_projectionManager != null && BibleVerseScrollViewer != null)
                {
                    _projectionManager.UpdateBibleProjectionWithVisualBrush(BibleVerseScrollViewer);
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"✅ [圣经投影-VisualBrush] 投影经文: {verse.Reference}");
                    //#endif
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [圣经投影-VisualBrush] 投影失败: {ex.Message}");
                #else
                _ = ex;
                #endif

                WpfMessageBox.Show(
                    $"投影失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 投影经文范围（多节）（🆕 使用 VisualBrush 100%一致投影）
        /// </summary>
        private async Task ProjectBibleVerseRangeAsync(int bookId, int chapter, int startVerse, int endVerse)
        {
            try
            {
                // 🆕 使用 VisualBrush 直接投影主屏幕内容（100%像素级一致）
                if (_projectionManager != null && BibleVerseScrollViewer != null)
                {
                    _projectionManager.UpdateBibleProjectionWithVisualBrush(BibleVerseScrollViewer);
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"✅ [圣经投影-VisualBrush] 投影范围: {bookId} {chapter}:{startVerse}-{endVerse}");
                    //#endif
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [圣经投影-VisualBrush] 投影范围失败: {ex.Message}");
                #else
                _ = ex;
                #endif

                WpfMessageBox.Show(
                    $"投影失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 渲染经文到投影屏幕（单节）
        /// </summary>
        private SKBitmap RenderVerseToProjection(BibleVerse verse)
        {
            var verses = new List<BibleVerse> { verse };
            return RenderVersesToProjection(verses);
        }

        /// <summary>
        /// 渲染多节经文到投影屏幕（使用SkiaSharp渲染）
        /// </summary>
        private SKBitmap RenderVersesToProjection(List<BibleVerse> verses)
        {
            if (verses == null || verses.Count == 0)
                return null;

            try
            {
                // 🔧 使用主屏幕的实际宽度来渲染，确保与主屏幕显示一致
                double screenWidth = 0;
                double screenHeight = 0;
                
                Dispatcher.Invoke(() =>
                {
                    if (BibleVerseScrollViewer != null)
                    {
                        screenWidth = BibleVerseScrollViewer.ActualWidth;
                        screenHeight = BibleVerseScrollViewer.ActualHeight;
                    }
                });
                
                // 如果获取失败，使用投影屏幕尺寸作为后备
                if (screenWidth <= 0 || screenHeight <= 0)
                {
                    var (projWidth, projHeight) = _projectionManager.GetProjectionScreenSize();
                    screenWidth = projWidth;
                    screenHeight = projHeight;
                }

                
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"📐 [圣经渲染-SkiaSharp] 屏幕尺寸: {screenWidth}×{screenHeight}, 经文数量: {verses.Count}");
//#endif

                // ========================================
                // ✅ 使用SkiaSharp渲染（替代WPF的Canvas+RenderTargetBitmap）
                // ========================================
                
                // 准备经文列表数据
                var verseItems = new List<Core.BibleVerseItem>();
                
                // 判断模式：锁定模式 vs 非锁定模式
                bool isLockedMode = verses.Count > 0 && verses[0].Verse == 0;
                
                // 获取章节标题文本（仅在非锁定模式使用）
                string chapterTitle = "";
                if (!isLockedMode)
                {
                    chapterTitle = Dispatcher.Invoke(() => BibleChapterTitle?.Text ?? "");
                }
                
                // 添加章节标题（非锁定模式）
                if (!isLockedMode && !string.IsNullOrEmpty(chapterTitle))
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📌 [投影渲染] 添加章节标题: {chapterTitle}");
                    //#endif
                    verseItems.Add(new Core.BibleVerseItem
                    {
                        IsTitle = true,
                        Text = chapterTitle,
                        IsHighlighted = false
                    });
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📌 [投影渲染] 总共 {verseItems.Count} 项（包含标题）");
                //#endif
                
                // 添加所有经文
                foreach (var verse in verses)
                {
                    if (verse.Verse == 0)
                    {
                        // 锁定模式：标题行
                        verseItems.Add(new Core.BibleVerseItem
                        {
                            IsTitle = true,
                            Text = verse.Scripture ?? "",
                            IsHighlighted = false
                        });
                    }
                    else
                    {
                        // 普通经文行
                        var scripture = verse.Scripture ?? "";
                        // 移除格式标记
                        if (scripture.Contains("<u>") || scripture.Contains("</u>"))
                        {
                            scripture = Utils.TextFormatHelper.StripHtmlTags(scripture);
                        }
                        
                        verseItems.Add(new Core.BibleVerseItem
                        {
                            IsTitle = false,
                            VerseNumber = verse.VerseNumberText,
                            Text = scripture,
                            IsHighlighted = verse.IsHighlighted
                        });
                    }
                }
                
                // 创建圣经渲染上下文
                var context = new Core.BibleRenderContext
                {
                    Verses = verseItems,
                    Size = new SKSize((float)screenWidth, (float)screenHeight),
                    BackgroundColor = Core.TextStyle.ParseColor(_configManager.BibleBackgroundColor),
                    Padding = new SKRect(
                        (float)_configManager.BibleMargin,
                        20f,
                        (float)_configManager.BibleMargin,
                        20f),
                    VerseSpacing = (float)_configManager.BibleVerseSpacing,
                    HighlightColor = Core.TextStyle.ParseColor(_configManager.BibleHighlightColor), // 🔧 添加高亮颜色
                    
                    // 标题样式
                    TitleStyle = new Core.TextStyle
                    {
                        FontFamily = _configManager.BibleFontFamily,
                        FontSize = (float)_configManager.BibleTitleFontSize,
                        TextColor = Core.TextStyle.ParseColor(_configManager.BibleTitleColor),
                        IsBold = true,
                        LineSpacing = 1.2f
                    },
                    
                    // 经文样式
                    VerseStyle = new Core.TextStyle
                    {
                        FontFamily = _configManager.BibleFontFamily,
                        FontSize = (float)_configManager.BibleFontSize,
                        TextColor = Core.TextStyle.ParseColor(_configManager.BibleTextColor),
                        IsBold = false,
                        LineSpacing = 1.2f
                    },
                    
                    // 节号样式
                    VerseNumberStyle = new Core.TextStyle
                    {
                        FontFamily = _configManager.BibleFontFamily,
                        FontSize = (float)_configManager.BibleVerseNumberFontSize,
                        TextColor = Core.TextStyle.ParseColor(_configManager.BibleVerseNumberColor),
                        IsBold = true,
                        LineSpacing = 1.2f
                    }
                };
                
                // ✅ 使用SkiaSharp渲染
                var skBitmap = _skiaRenderer.RenderBibleText(context);
                
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"✅ [圣经渲染-SkiaSharp] 完成: {skBitmap.Width}×{skBitmap.Height}");
//#endif
                
                return skBitmap;
            }
//#if DEBUG
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[圣经] 渲染失败: {ex.Message}");
//                return null;
//            }
//#else
            catch (Exception)
            {
                return null;
            }
//#endif
        }

        // ConvertToSKBitmap方法已在MainWindow.Lyrics.cs中定义，此处复用

        /// <summary>
        /// 渲染圣经经文到投影（参考歌词渲染逻辑）
        /// </summary>
        private void RenderBibleToProjection()
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"📺 [渲染投影] ========== 开始 ==========");
            //System.Diagnostics.Debug.WriteLine($"   经文列表项数量: {BibleVerseList.Items.Count}");
            //#endif

            try
            {
                // 如果没有经文，不投影
                if (BibleVerseList.ItemsSource == null || BibleVerseList.Items.Count == 0)
                {
                    //#if DEBUG
                    //Debug.WriteLine("[圣经] 没有经文可投影");
                    //#endif
                    return;
                }

                // 获取当前显示的所有经文
                var versesList = new List<BibleVerse>();
                var verses = BibleVerseList.ItemsSource as System.Collections.IEnumerable;
                if (verses != null)
                {
                    foreach (var item in verses)
                    {
                        if (item is BibleVerse verse)
                        {
                            versesList.Add(verse);
                        }
                    }
                }

                if (versesList.Count == 0)
                {
                    //#if DEBUG
                    //Debug.WriteLine("[圣经] 没有有效的经文数据");
                    //#endif
                    return;
                }

                // 🆕 使用 RenderTargetBitmap 独立渲染投影（解决高亮变色波浪问题）
                if (_projectionManager != null && BibleVerseScrollViewer != null)
                {
                    _projectionManager.UpdateBibleProjectionWithVisualBrush(BibleVerseScrollViewer);

                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"✅ [圣经投影-RenderTargetBitmap] 投影完成，共{versesList.Count}节");
                    //#endif
                }
            }
//#if DEBUG
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[圣经] 渲染投影失败: {ex.Message}\n{ex.StackTrace}");
//            }
//#else
            catch (Exception)
            {
            }
//#endif
        }

        /// <summary>
        /// 投影状态改变时的回调（供主窗口调用）
        /// 当投影开启时，如果在圣经模式，自动投影圣经
        /// </summary>
        public void OnBibleProjectionStateChanged(bool isProjecting)
        {
//#if DEBUG
//            Debug.WriteLine($"[圣经] 投影状态改变 - IsProjecting: {isProjecting}, _isBibleMode: {_isBibleMode}");
//#endif

            if (isProjecting && _isBibleMode)
            {
//#if DEBUG
//                Debug.WriteLine("[圣经] 投影开启且在圣经模式，触发投影");
//#endif
                // 🔧 立即清空图片状态（防止自动刷新显示图片）
                _projectionManager.ClearImageState();

                // 延迟2ms确保投影窗口完全初始化
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(2)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    
                    // 检查是否有锁定记录
                    bool hasLockedRecords = _historySlots.Any(x => x.IsLocked);
                    
                    if (hasLockedRecords)
                    {
                        // 📌 锁定模式：投影开启时，投影锁定记录
                        //#if DEBUG
                        //Debug.WriteLine("[圣经] 锁定模式：延迟后投影锁定记录");
                        //#endif
                        _ = UpdateProjectionFromMergedVerses();
                    }
                    else
                    {
                        // 📌 非锁定模式：投影当前章节
                        //#if DEBUG
                        //Debug.WriteLine("[圣经] 非锁定模式：延迟后投影当前章节");
                        //#endif
                        RenderBibleToProjection();
                    }
                };
                timer.Start();
            }
        }

        #endregion

        #region 底部译本工具栏自动显示/隐藏

        /// <summary>
        /// 鼠标进入底部区域或工具栏时显示
        /// </summary>
        private void BibleVersionTrigger_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 🔧 只在圣经模式下才显示工具栏
            if (!_isBibleMode)
            {
                return;
            }
            
            if (BibleVersionToolbar != null)
            {
                // 直接显示工具栏
                BibleVersionToolbar.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 鼠标离开底部区域和工具栏时隐藏
        /// </summary>
        private void BibleVersionTrigger_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 🔧 只在圣经模式下才处理隐藏逻辑
            if (!_isBibleMode)
            {
                return;
            }
            
            if (BibleVersionToolbar != null)
            {
                // 检查鼠标是否真的离开了整个区域（触发区+工具栏）
                var triggerArea = BibleVersionTriggerArea;
                var toolbar = BibleVersionToolbar;
                
                if (triggerArea != null && toolbar != null && BibleVerseScrollViewer != null)
                {
                    var mousePos = Mouse.GetPosition(BibleVerseScrollViewer);
                    var triggerBounds = new Rect(
                        triggerArea.TranslatePoint(new System.Windows.Point(0, 0), BibleVerseScrollViewer),
                        new System.Windows.Size(triggerArea.ActualWidth, triggerArea.ActualHeight)
                    );
                    
                    // 只检查触发区，因为工具栏可能是Collapsed状态
                    if (triggerBounds.Contains(mousePos))
                    {
                        return;
                    }
                    
                    // 如果工具栏可见，也检查工具栏区域
                    if (toolbar.Visibility == Visibility.Visible)
                    {
                        var toolbarBounds = new Rect(
                            toolbar.TranslatePoint(new System.Windows.Point(0, 0), BibleVerseScrollViewer),
                            new System.Windows.Size(toolbar.ActualWidth, toolbar.ActualHeight)
                        );
                        
                        if (toolbarBounds.Contains(mousePos))
                        {
                            return;
                        }
                    }
                }

                // 隐藏工具栏
                BibleVersionToolbar.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

    }
}
