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
    /// MainWindow Bible Helpers (Utilities, Pinyin, History Persistence, Insert)
    /// </summary>
    public partial class MainWindow
    {
        #region 辅助方法
        
        /// <summary>
        /// 查找Visual树中第一个指定类型的子元素
        /// </summary>
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                    
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            
            return null;
        }
        
        /// <summary>
        /// 查找Visual树中所有指定类型的子元素
        /// </summary>
        private IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    yield return result;
                    
                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }

        /// <summary>
        /// 历史记录列表鼠标滚轮事件（将滚轮事件传递给 ScrollViewer）
        /// </summary>
        private void BibleHistoryList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (BibleHistoryScrollViewer != null)
            {
                // 计算滚动偏移量
                double offset = e.Delta > 0 ? -60 : 60; // 向上滚动为负值，向下滚动为正值
                
                // 应用滚动偏移
                BibleHistoryScrollViewer.ScrollToVerticalOffset(BibleHistoryScrollViewer.VerticalOffset + offset);
                
                // 标记事件已处理，防止继续传递
                e.Handled = true;
            }
        }

        #endregion

        #region 拼音快速定位功能

        // IME控制代码已暂时移除，专注功能实现
        /*
        // Windows API for IME control
        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetOpenStatus(IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmAssociateContextEx(IntPtr hWnd, IntPtr hIMC, uint dwFlags);

        private const uint IACE_DEFAULT = 0x0010;
        private const uint IACE_IGNORENOCONTEXT = 0x0020;

        private bool _imeWasEnabled = false; // 记录激活前IME状态
        private IntPtr _previousIMC = IntPtr.Zero; // 记录之前的IME上下文
        */

        /// <summary>
        /// 禁用IME（已通过XAML实现）
        /// </summary>
        private void DisableIME()
        {
            // IME控制已通过XAML的InputMethod属性实现：
            // BibleVerseScrollViewer 设置了 InputMethod.PreferredImeState="Off" 和 InputMethod.IsInputMethodEnabled="False"
            // 这样当焦点在圣经区域时，会自动禁用中文输入法，强制英文输入
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[圣经拼音] IME已通过XAML禁用（InputMethod.PreferredImeState=Off）");
#endif
        }

        /// <summary>
        /// 恢复IME状态（已通过XAML实现）
        /// </summary>
        private void RestoreIME()
        {
            // IME控制已通过XAML的InputMethod属性实现，无需手动恢复
            // 当焦点离开BibleVerseScrollViewer时，输入法会自动恢复正常状态
//#if DEBUG
            //System.Diagnostics.Debug.WriteLine("[圣经拼音] IME会在焦点离开时自动恢复");
//#endif
        }

        /// <summary>
        /// 初始化拼音快速定位服务
        /// </summary>
        private void InitializePinyinService()
        {
            try
            {
                // 检查 BibleService 是否可用
                if (_bibleService == null)
                {
                    //System.Diagnostics.Debug.WriteLine("[圣经拼音] BibleService 未初始化，跳过拼音服务初始化");
                    return;
                }

                _pinyinService = new ImageColorChanger.Services.BiblePinyinService(_bibleService);
                _pinyinInputManager = new ImageColorChanger.Services.BiblePinyinInputManager(
                    _pinyinService,
                    OnPinyinLocationConfirmedAsync,
                    OnPinyinHintUpdateAsync,
                    OnPinyinDeactivate
                );

                //System.Diagnostics.Debug.WriteLine("[圣经拼音] 拼音快速定位服务初始化成功");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[圣经拼音] 拼音快速定位服务初始化失败: {ex.Message}");
                _pinyinInputManager = null; // 确保为 null，后续检查会跳过拼音功能
            }
        }

        /// <summary>
        /// 拼音输入退出回调（隐藏提示框）
        /// </summary>
        private void OnPinyinDeactivate()
        {
            //#if DEBUG
            //Debug.WriteLine("[圣经拼音] 退出拼音输入模式");
            //#endif
            
            // 隐藏提示框
            BiblePinyinHintControl.Hide();
            
            // 恢复IME（已禁用）
            RestoreIME();
        }

        /// <summary>
        /// 经文滚动区键盘事件（激活拼音输入）
        /// </summary>
        private async void BibleVerseScrollViewer_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isBibleMode) return;

            // 检查拼音输入管理器是否已初始化
            if (_pinyinInputManager == null)
            {
                return;
            }

            // 如果拼音输入已激活，优先处理ESC键（取消输入框，不关闭投影）
            if (_pinyinInputManager.IsActive && e.Key == Key.Escape)
            {
                //#if DEBUG
                //Debug.WriteLine("[圣经拼音] ESC键被拦截 - 关闭输入框，不关闭投影");
                //#endif
                
                await _pinyinInputManager.ProcessKeyAsync(e.Key);
                e.Handled = true; // 完全拦截ESC键，防止关闭投影
                return;
            }

            // 如果还未激活，字母键激活拼音输入
            if (!_pinyinInputManager.IsActive && e.Key >= Key.A && e.Key <= Key.Z)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经拼音] 激活拼音输入 - 按键: {e.Key}");
                //#endif
                
                // 禁用IME，强制英文输入
                DisableIME();
                
                _pinyinInputManager.Activate();
            }

            // 如果已激活，处理所有键盘输入
            if (_pinyinInputManager.IsActive)
            {
                await _pinyinInputManager.ProcessKeyAsync(e.Key);
                e.Handled = true; // 阻止默认行为
            }
        }

        /// <summary>
        /// 经文滚动区鼠标点击事件（点击空白区域退出拼音输入）
        /// </summary>
        private void BibleVerseScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isBibleMode) return;

            // 检查拼音输入管理器是否已初始化
            if (_pinyinInputManager == null)
            {
                return;
            }

            // 检查点击位置是否在提示框外
            if (_pinyinInputManager.IsActive)
            {
                var clickPoint = e.GetPosition(BiblePinyinHintControl);
                var isClickOnHint = clickPoint.X >= 0 && clickPoint.X <= BiblePinyinHintControl.ActualWidth &&
                                   clickPoint.Y >= 0 && clickPoint.Y <= BiblePinyinHintControl.ActualHeight;

                if (!isClickOnHint)
                {
                    _pinyinInputManager.Deactivate();
                    BiblePinyinHintControl.Hide();
                    
                    // 恢复IME状态
                    RestoreIME();
                }
            }
        }

        /// <summary>
        /// 上帧按钮点击事件（向上滚动）
        /// </summary>
        private void BtnBiblePrevVerse_Click(object sender, RoutedEventArgs e)
        {
            if (!_isBibleMode || BibleVerseList == null || BibleVerseList.Items.Count == 0)
                return;

            HandleVerseScroll(-1);
        }

        /// <summary>
        /// 下帧按钮点击事件（向下滚动）
        /// </summary>
        private void BtnBibleNextVerse_Click(object sender, RoutedEventArgs e)
        {
            if (!_isBibleMode || BibleVerseList == null || BibleVerseList.Items.Count == 0)
                return;

            HandleVerseScroll(1);
        }

        // 滚轮对齐相关字段
        private System.Windows.Threading.DispatcherTimer _scrollAlignTimer;
        private DateTime _lastScrollTime = DateTime.MinValue; // 上次滚动时间
        private const int SCROLL_THROTTLE_MS = 50; // 滚动节流时间（毫秒）

        /// <summary>
        /// 经文滚动区鼠标滚轮事件（按高亮经文逐节跳转）
        /// </summary>
        private void BibleVerseScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_isBibleMode || BibleVerseList == null || BibleVerseList.Items.Count == 0)
                return;

            // 阻止默认滚动行为
            e.Handled = true;

            // 计算滚动方向
            int direction = e.Delta > 0 ? -1 : 1; // 向上滚轮=-1（向上滚动），向下滚轮=+1（向下滚动）

            HandleVerseScroll(direction);
        }

        /// <summary>
        /// 处理经文滚动（通用逻辑，供鼠标滚轮和键盘事件调用）
        /// </summary>
        private void HandleVerseScroll(int direction)
        {
            // 节流：防止滚动事件触发过快（无动画模式下可以适当放宽）
            var now = DateTime.Now;
            if ((now - _lastScrollTime).TotalMilliseconds < 30)
            {
                return;
            }
            _lastScrollTime = now;

            // 逻辑改为：基于“当前高亮经文”逐节跳转（例如 4 -> 5）
            NavigateHighlightedVerse(direction);
        }

        /// <summary>
        /// 查找当前滚动位置最接近顶部的经文索引
        /// </summary>
        private int FindClosestVerseIndex(double currentOffset)
        {
            if (BibleVerseList == null || BibleVerseList.Items.Count == 0)
                return 0;

            // 获取标题和顶部边距的总高度
            double headerHeight = 0;
            if (BibleChapterTitleBorder != null)
                headerHeight += BibleChapterTitleBorder.ActualHeight;
            headerHeight += 20; // 顶部边距

            // 如果滚动位置在标题区域，返回第一节
            if (currentOffset < headerHeight)
            {
                // Debug.WriteLine($"  📍 在标题区域，返回节1");
                return 0;
            }

            // 🔧 新策略：使用 CalculateVerseOffset 来计算每一节的精确位置
            // 这样即使节未渲染（Container为null），也能正确判断
            int totalVerses = BibleVerseList.Items.Count;
            
            // 从后往前查找，找到第一个起始位置 <= currentOffset 的节
            for (int i = totalVerses - 1; i >= 0; i--)
            {
                double verseOffset = CalculateVerseOffset(i);
                if (currentOffset >= verseOffset)
                {
                    // Debug.WriteLine($"  ✅ 找到节{i + 1}，起始位置: {verseOffset:F1}px，当前位置: {currentOffset:F1}px");
                    return i;
                }
            }

            // 理论上不应该到这里，返回第一节
            // Debug.WriteLine($"  ⚠️ 未找到匹配，返回第一节");
            return 0;
        }

        /// <summary>
        /// 平滑滚动到指定经文
        /// </summary>
        private void ScrollToVerseSmooth(int verseIndex)
        {
            if (BibleVerseList == null || verseIndex < 0 || verseIndex >= BibleVerseList.Items.Count)
                return;

            // 计算目标滚动位置
            double targetOffset = CalculateVerseOffset(verseIndex);

            // 使用计时器实现平滑滚动
            double startOffset = BibleVerseScrollViewer.VerticalOffset;
            double distance = targetOffset - startOffset;
            
            // 如果距离很小，直接跳转
            if (Math.Abs(distance) < 5)
            {
                BibleVerseScrollViewer.ScrollToVerticalOffset(targetOffset);
                return;
            }

            // 平滑滚动参数
            int steps = 6; // 滚动步数（更快的动画）
            int currentStep = 0;

            if (_scrollAlignTimer == null)
            {
                _scrollAlignTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16) // 约60fps
                };
            }
            else
            {
                _scrollAlignTimer.Stop();
                _scrollAlignTimer.Tick -= null; // 清除旧的事件处理
            }

            System.Windows.Threading.DispatcherTimer localTimer = _scrollAlignTimer;
            System.EventHandler tickHandler = null;
            
            tickHandler = (s, e) =>
            {
                currentStep++;
                
                if (currentStep >= steps)
                {
                    // 最后一步，精确到目标位置
                    BibleVerseScrollViewer.ScrollToVerticalOffset(targetOffset);
                    localTimer.Tick -= tickHandler;
                    localTimer.Stop();
                }
                else
                {
                    // 使用缓动函数（ease-out）
                    double progress = (double)currentStep / steps;
                    double easedProgress = 1 - Math.Pow(1 - progress, 3); // cubic ease-out
                    double newOffset = startOffset + distance * easedProgress;
                    BibleVerseScrollViewer.ScrollToVerticalOffset(newOffset);
                }
            };

            _scrollAlignTimer.Tick += tickHandler;
            _scrollAlignTimer.Start();
        }

        /// <summary>
        /// 立即跳转到指定经文（无动画）
        /// </summary>
        private void ScrollToVerseInstant(int verseIndex)
        {
            if (BibleVerseList == null || verseIndex < 0 || verseIndex >= BibleVerseList.Items.Count)
                return;

            // 计算目标滚动位置
            double targetOffset = CalculateVerseOffset(verseIndex);

            // 直接跳转，无动画
            BibleVerseScrollViewer.ScrollToVerticalOffset(targetOffset);
        }

        /// <summary>
        /// 计算指定经文的滚动偏移量
        /// </summary>
        private double CalculateVerseOffset(int verseIndex)
        {
            if (BibleVerseList == null || verseIndex < 0 || verseIndex >= BibleVerseList.Items.Count)
                return 0;

            // 获取标题和顶部边距的总高度
            double headerHeight = 0;
            if (BibleChapterTitleBorder != null)
                headerHeight += BibleChapterTitleBorder.ActualHeight;
            headerHeight += 20; // 顶部边距

            // 如果是第一节，滚动到标题后
            if (verseIndex == 0)
                return headerHeight;

            // 计算前面所有经文的累计高度
            double accumulatedHeight = headerHeight;
            int nullCount = 0;
            
            for (int i = 0; i < verseIndex; i++)
            {
                var container = BibleVerseList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container != null)
                {
                    accumulatedHeight += container.ActualHeight;
                }
                else
                {
                    nullCount++;
                }
            }

#if DEBUG
            if (nullCount > 0)
            {
                Debug.WriteLine($"⚠️ [经文对齐] 节{verseIndex + 1} 前有{nullCount}个节未渲染，位置可能不准: {accumulatedHeight:F1}px");
            }
#endif

            return accumulatedHeight;
        }

        /// <summary>
        /// 拼音定位确认回调
        /// </summary>
        private async System.Threading.Tasks.Task OnPinyinLocationConfirmedAsync(ImageColorChanger.Services.ParseResult result)
        {
            if (!result.Success) return;

            try
            {
                // 根据定位类型执行跳转
                if (result.Type == ImageColorChanger.Services.LocationType.Book && result.BookId.HasValue)
                {
                    // 跳转到书卷第一章
                    await LoadChapterVersesAsync(result.BookId.Value, 1);
                    
                    // 添加到历史记录（第一章全部经文）
                    var verseCount = await _bibleService.GetVerseCountAsync(result.BookId.Value, 1);
                    AddPinyinHistoryToEmptySlot(result.BookId.Value, 1, 1, verseCount > 0 ? verseCount : 31);
                }
                else if (result.Type == ImageColorChanger.Services.LocationType.Chapter && 
                         result.BookId.HasValue && result.Chapter.HasValue)
                {
                    // 跳转到指定章
                    await LoadChapterVersesAsync(result.BookId.Value, result.Chapter.Value);
                    
                    // 添加到历史记录（该章全部经文）
                    var verseCount = await _bibleService.GetVerseCountAsync(result.BookId.Value, result.Chapter.Value);
                    AddPinyinHistoryToEmptySlot(result.BookId.Value, result.Chapter.Value, 1, verseCount > 0 ? verseCount : 31);
                }
                else if (result.Type == ImageColorChanger.Services.LocationType.VerseRange && 
                         result.BookId.HasValue && result.Chapter.HasValue && 
                         result.StartVerse.HasValue && result.EndVerse.HasValue)
                {
                    // 跳转到指定节范围
                    await LoadVerseRangeAsync(result.BookId.Value, result.Chapter.Value, 
                                             result.StartVerse.Value, result.EndVerse.Value);
                    
                    // 添加到历史记录
                    AddPinyinHistoryToEmptySlot(result.BookId.Value, result.Chapter.Value, 
                                result.StartVerse.Value, result.EndVerse.Value);
                }

                // 隐藏提示框
                BiblePinyinHintControl.Hide();
                
                // 恢复IME状态
                RestoreIME();
            }
            catch (Exception ex)
            {
                // 失败时也要恢复IME
                RestoreIME();
                
                WpfMessageBox.Show($"定位失败：{ex.Message}", "错误", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 拼音定位专用：优先添加到空槽位，满了才覆盖选中的槽位
        /// </summary>
        private void AddPinyinHistoryToEmptySlot(int bookId, int chapter, int startVerse, int endVerse)
        {
            try
            {
                var book = BibleBookConfig.GetBook(bookId);
                // 🔧 如果开始节和结束节相同，只显示一个节号（如"3节"），否则显示范围（如"3-5节"）
                string verseText = (startVerse == endVerse) ? $"{startVerse}节" : $"{startVerse}-{endVerse}节";
                string displayText = $"{book?.Name}{chapter}章{verseText}";

                BibleHistoryItem targetSlot = null;
                
                // 1. 优先查找空槽位（DisplayText为空或BookId为0）
                var emptySlot = _historySlots.FirstOrDefault(s => string.IsNullOrWhiteSpace(s.DisplayText) || s.BookId == 0);
                
                if (emptySlot != null)
                {
                    // 找到空槽位，直接填充
                    emptySlot.BookId = bookId;
                    emptySlot.Chapter = chapter;
                    emptySlot.StartVerse = startVerse;
                    emptySlot.EndVerse = endVerse;
                    emptySlot.DisplayText = displayText;
                    targetSlot = emptySlot;
                }
                else
                {
                    // 2. 所有槽位都满了，查找勾选的槽位
                    var checkedSlots = _historySlots.Where(s => s.IsChecked).ToList();
                    
                    if (checkedSlots.Count > 0)
                    {
                        // 覆盖第一个勾选的槽位
                        targetSlot = checkedSlots[0];
                        targetSlot.BookId = bookId;
                        targetSlot.Chapter = chapter;
                        targetSlot.StartVerse = startVerse;
                        targetSlot.EndVerse = endVerse;
                        targetSlot.DisplayText = displayText;
                    }
                    else
                    {
                        // 没有勾选的槽位，覆盖最后一个槽位（槽位20）
                        var lastSlot = _historySlots.LastOrDefault();
                        if (lastSlot != null)
                        {
                            lastSlot.BookId = bookId;
                            lastSlot.Chapter = chapter;
                            lastSlot.StartVerse = startVerse;
                            lastSlot.EndVerse = endVerse;
                            lastSlot.DisplayText = displayText;
                            targetSlot = lastSlot;
                        }
                    }
                }

                // 🆕 取消其他槽位的勾选，勾选新填充的槽位
                if (targetSlot != null)
                {
                    foreach (var slot in _historySlots)
                    {
                        slot.IsChecked = (slot == targetSlot);
                    }
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"✅ [拼音搜索] 已自动勾选槽位{targetSlot.Index}: {displayText}");
                    //#endif
                }

                // 刷新列表显示
                BibleHistoryList.Items.Refresh();
            }
            catch
            {
                // 静默失败，不影响用户操作
            }
        }

        /// <summary>
        /// 拼音提示更新回调
        /// </summary>
        private System.Threading.Tasks.Task OnPinyinHintUpdateAsync(string displayText, System.Collections.Generic.List<ImageColorChanger.Services.BibleBookMatch> matches)
        {
            BiblePinyinHintControl.UpdateHint(displayText, matches);
            
            return System.Threading.Tasks.Task.CompletedTask;
        }

        #endregion

        #region 历史记录持久化

        /// <summary>
        /// 保存圣经历史记录到数据库
        /// </summary>
        public void SaveBibleHistoryToConfig()
        {
            try
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"💾 [保存历史] 开始保存圣经历史记录到数据库");
                //System.Diagnostics.Debug.WriteLine($"   _historySlots: {_historySlots?.Count ?? 0} 个槽位");
                //#endif
                
                if (_historySlots == null || _historySlots.Count == 0)
                {
                    return;
                }

                // 使用与DatabaseManager相同的默认路径
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
                using (var db = new Database.CanvasDbContext(dbPath))
                {
                    // 删除旧记录
                    var oldRecords = db.BibleHistory.ToList();
                    db.BibleHistory.RemoveRange(oldRecords);

                    // 保存所有20个槽位（包括空槽位）
                    // 🔧 不保存锁定状态，退出时锁定状态会被清除
                    foreach (var slot in _historySlots)
                    {
                        var record = new Database.Models.BibleHistoryRecord
                        {
                            SlotIndex = slot.Index,
                            DisplayText = slot.DisplayText ?? "",
                            BookId = slot.BookId,
                            Chapter = slot.Chapter,
                            StartVerse = slot.StartVerse,
                            EndVerse = slot.EndVerse,
                            IsChecked = slot.IsChecked,
                            IsLocked = false,  // 🔧 不保存锁定状态
                            UpdatedTime = DateTime.Now
                        };
                        
                        db.BibleHistory.Add(record);
                    }

                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [保存历史] 保存历史记录失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }

        /// <summary>
        /// 从数据库加载圣经历史记录
        /// </summary>
        public void LoadBibleHistoryFromConfig()
        {
            try
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📂 [加载历史] 开始从数据库加载圣经历史记录");
                //#endif
                
                // 使用与DatabaseManager相同的默认路径
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
                using (var db = new Database.CanvasDbContext(dbPath))
                {
                    var historyRecords = db.BibleHistory.OrderBy(h => h.SlotIndex).ToList();
                    
                    if (historyRecords.Count == 0)
                    {
                        return;
                    }

                    // 恢复历史记录到槽位
                    // 🔧 不恢复锁定状态，退出时锁定状态会被清除
                    
                    // 🔧 临时取消订阅事件，避免触发增量更新
                    BibleHistoryItem.OnLockedStateChanged -= UpdateClearButtonStyle;
                    
                    foreach (var record in historyRecords)
                    {
                        var slot = _historySlots.FirstOrDefault(s => s.Index == record.SlotIndex);
                        if (slot != null)
                        {
                            slot.DisplayText = record.DisplayText;
                            slot.BookId = record.BookId;
                            slot.Chapter = record.Chapter;
                            slot.StartVerse = record.StartVerse;
                            slot.EndVerse = record.EndVerse;
                            slot.IsChecked = record.IsChecked;
                            slot.IsLocked = false;  // 🔧 不恢复锁定状态
                        }
                    }
                    
                    // 🔧 重新订阅事件
                    BibleHistoryItem.OnLockedStateChanged += UpdateClearButtonStyle;
                    
                    // 🔧 不再检查锁定记录，因为锁定状态不会被保存和恢复
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [加载历史] 加载历史记录失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }

        /// <summary>
        /// 清空所有历史记录（包括数据库）
        /// </summary>
        public void ClearAllBibleHistory()
        {
            try
            {
                // 清空内存中的槽位
                if (_historySlots != null)
                {
                    foreach (var slot in _historySlots)
                    {
                        slot.DisplayText = "";
                        slot.BookId = 0;
                        slot.Chapter = 0;
                        slot.StartVerse = 0;
                        slot.EndVerse = 0;
                        slot.IsChecked = false;
                        slot.IsLocked = false;
                    }
                }

                // 清空数据库
                // 使用与DatabaseManager相同的默认路径
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
                using (var db = new Database.CanvasDbContext(dbPath))
                {
                    var allRecords = db.BibleHistory.ToList();
                    db.BibleHistory.RemoveRange(allRecords);
                    db.SaveChanges();
                }

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine("🗑️ [清空历史] 已清空所有历史记录（内存+数据库）");
                //#endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [清空历史] 清空历史记录失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }

        #endregion

        #region 圣经经文插入功能

        /// <summary>
        /// 圣经样式设置按钮点击事件（切换显示/隐藏）
        /// </summary>
        private void BtnBibleInsertStyleSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果 Popup 已存在且打开，则关闭
                if (_bibleStylePopup != null && _bibleStylePopup.IsOpen)
                {
                    _bibleStylePopup.IsOpen = false;
                    
                    //#if DEBUG
                    //Debug.WriteLine($"✅ [圣经插入] 样式设置 Popup 已关闭");
                    //#endif
                    return;
                }
                
                // 如果 Popup 不存在，创建新的
                if (_bibleStylePopup == null)
                {
                    _bibleStylePopup = new BibleInsertStylePopup(_dbManager);
                    
                    // 设置 Popup 的位置目标为工具栏按钮
                    _bibleStylePopup.PlacementTarget = sender as UIElement;
                    _bibleStylePopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    
                    // 监听 Popup 关闭事件
                    _bibleStylePopup.Closed += (s, args) =>
                    {
                        //#if DEBUG
                        //Debug.WriteLine($"🔄 [圣经插入] Popup 已关闭");
                        //#endif
                    };
                }
                
                // 打开 Popup
                _bibleStylePopup.IsOpen = true;
                
                //#if DEBUG
                //Debug.WriteLine($"✅ [圣经插入] 样式设置 Popup 已打开");
                //#endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [圣经插入] 切换样式设置 Popup 失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
                
                WpfMessageBox.Show("打开样式设置面板失败", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

    }
}
