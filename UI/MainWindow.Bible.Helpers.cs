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
        /// 设置滚动节数的右键菜单项点击事件
        /// </summary>
        private void SetScrollVerseCount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag != null)
            {
                // 尝试将 Tag 转换为 int（可能是 int 或 string）
                int count;
                if (menuItem.Tag is int tagInt)
                {
                    count = tagInt;
                }
                else if (int.TryParse(menuItem.Tag.ToString(), out int tagParsed))
                {
                    count = tagParsed;
                }
                else
                {
#if DEBUG
                    Debug.WriteLine($"❌ [滚动设置] Tag 转换失败: {menuItem.Tag} (类型: {menuItem.Tag.GetType()})");
#endif
                    return;
                }
                
                _scrollVerseCount = count;
                
                // 更新菜单项的选中状态
                MenuScrollCount1.IsChecked = (count == 1);
                MenuScrollCount2.IsChecked = (count == 2);
                MenuScrollCount3.IsChecked = (count == 3);
                MenuScrollCount4.IsChecked = (count == 4);
                MenuScrollCount5.IsChecked = (count == 5);
                MenuScrollCount6.IsChecked = (count == 6);
                MenuScrollCount7.IsChecked = (count == 7);
                MenuScrollCount8.IsChecked = (count == 8);
                MenuScrollCount9.IsChecked = (count == 9);
                MenuScrollCount10.IsChecked = (count == 10);
                
                // 💾 保存设置到数据库
                SaveBibleScrollVerseCountSetting();
                
#if DEBUG
                //Debug.WriteLine($"⚙️ [滚动设置] 已设置滚动节数: {count}节");
#endif
                ShowStatus($"✅ 已设置滚动节数: {count}节");
            }
        }

        /// <summary>
        /// 从数据库加载经文滚动节数设置
        /// </summary>
        private void LoadBibleScrollVerseCountSetting()
        {
            try
            {
                var dbContext = _dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "bible_scroll_verse_count");
                if (setting != null && int.TryParse(setting.Value, out int count) && count >= 1 && count <= 10)
                {
                    _scrollVerseCount = count;
                    
                    // 更新菜单项的选中状态
                    MenuScrollCount1.IsChecked = (count == 1);
                    MenuScrollCount2.IsChecked = (count == 2);
                    MenuScrollCount3.IsChecked = (count == 3);
                    MenuScrollCount4.IsChecked = (count == 4);
                    MenuScrollCount5.IsChecked = (count == 5);
                    MenuScrollCount6.IsChecked = (count == 6);
                    MenuScrollCount7.IsChecked = (count == 7);
                    MenuScrollCount8.IsChecked = (count == 8);
                    MenuScrollCount9.IsChecked = (count == 9);
                    MenuScrollCount10.IsChecked = (count == 10);
                    
#if DEBUG
                    Debug.WriteLine($"✅ [滚动设置] 从数据库加载滚动节数: {count}节");
#endif
                }
            }
            catch (Exception)
            {
#if DEBUG
                // 加载失败不影响功能，静默处理
#endif
            }
        }

        /// <summary>
        /// 保存经文滚动节数设置到数据库
        /// </summary>
        private void SaveBibleScrollVerseCountSetting()
        {
            try
            {
                var dbContext = _dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "bible_scroll_verse_count");
                if (setting == null)
                {
                    setting = new Database.Models.Setting
                    {
                        Key = "bible_scroll_verse_count",
                        Value = _scrollVerseCount.ToString()
                    };
                    dbContext.Settings.Add(setting);
                }
                else
                {
                    setting.Value = _scrollVerseCount.ToString();
                }
                
                dbContext.SaveChanges();
                
#if DEBUG
                //Debug.WriteLine($"💾 [滚动设置] 已保存滚动节数到数据库: {_scrollVerseCount}节");
#endif
            }
            catch (Exception)
            {
#if DEBUG
                // 保存失败不影响功能，静默处理
#endif
            }
        }

        /// <summary>
        /// 上帧按钮点击事件（向上滚动）
        /// </summary>
        private void BtnBiblePrevVerse_Click(object sender, RoutedEventArgs e)
        {
            if (!_isBibleMode || BibleVerseList == null || BibleVerseList.Items.Count == 0)
                return;

            HandleVerseScroll(-1, _scrollVerseCount);
        }

        /// <summary>
        /// 下帧按钮点击事件（向下滚动）
        /// </summary>
        private void BtnBibleNextVerse_Click(object sender, RoutedEventArgs e)
        {
            if (!_isBibleMode || BibleVerseList == null || BibleVerseList.Items.Count == 0)
                return;

            HandleVerseScroll(1, _scrollVerseCount);
        }

        // 滚轮对齐相关字段
        private System.Windows.Threading.DispatcherTimer _scrollAlignTimer;
        private int _currentTargetVerseIndex = -1; // 当前目标经文索引
        private DateTime _lastScrollTime = DateTime.MinValue; // 上次滚动时间
        private const int SCROLL_THROTTLE_MS = 50; // 滚动节流时间（毫秒）
        private int _scrollVerseCount = 1; // 每次滚动的节数（默认1节）

        /// <summary>
        /// 经文滚动区鼠标滚轮事件（自动对齐到经文顶部）
        /// </summary>
        private void BibleVerseScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_isBibleMode || BibleVerseList == null || BibleVerseList.Items.Count == 0)
                return;

            // 阻止默认滚动行为
            e.Handled = true;

            // 计算滚动方向
            int direction = e.Delta > 0 ? -1 : 1; // 向上滚轮=-1（向上滚动），向下滚轮=+1（向下滚动）
            
            //#if DEBUG
            //Debug.WriteLine($"🖱️ [滚轮事件] 方向: {(direction < 0 ? "向上" : "向下")}, _scrollVerseCount={_scrollVerseCount}");
            //#endif
            
            // 调用通用滚动处理逻辑（使用用户设置的滚动节数）
            HandleVerseScroll(direction, _scrollVerseCount);
        }

        /// <summary>
        /// 处理经文滚动（通用逻辑，供鼠标滚轮和键盘事件调用）
        /// </summary>
        private void HandleVerseScroll(int direction)
        {
            HandleVerseScroll(direction, 1); // 默认滚动1节
        }

        /// <summary>
        /// 处理经文滚动（支持指定滚动节数）
        /// </summary>
        /// <param name="direction">滚动方向：-1向上，1向下</param>
        /// <param name="count">滚动节数</param>
        private void HandleVerseScroll(int direction, int count)
        {
            //#if DEBUG
            //Debug.WriteLine($"📥 [HandleVerseScroll] 收到参数: direction={direction}, count={count}");
            //#endif
            
            // 节流：防止滚动事件触发过快（无动画模式下可以适当放宽）
            var now = DateTime.Now;
            if ((now - _lastScrollTime).TotalMilliseconds < 30) // 从50ms降低到30ms，更灵敏
            {
                // Debug.WriteLine($"🖱️ [滚轮对齐] 滚动过快，忽略 ({(now - _lastScrollTime).TotalMilliseconds:F0}ms)");
                return;
            }
            _lastScrollTime = now;

            // 手动滚动
            double currentOffset = BibleVerseScrollViewer.VerticalOffset;
            
            // 找到当前最接近顶部的经文索引
            int currentVerseIndex = FindClosestVerseIndex(currentOffset);
            
            // 🔧 智能对齐：检查当前经文是否已经对齐
            double currentVerseOffset = CalculateVerseOffset(currentVerseIndex);
            double offsetDiff = currentOffset - currentVerseOffset; // 注意：不取绝对值，保留方向
            const double ALIGNMENT_THRESHOLD = 5.0; // 对齐阈值（像素）
            
            int targetVerseIndex;
            
            // 判断是否已对齐（在阈值范围内）
            bool isAligned = Math.Abs(offsetDiff) <= ALIGNMENT_THRESHOLD;
            
            if (isAligned)
            {
                // 🔧 情况1：已对齐，移动指定节数
                targetVerseIndex = Math.Max(0, Math.Min(BibleVerseList.Items.Count - 1, currentVerseIndex + (direction * count)));
            }
            else
            {
                // 🔧 情况2：未对齐，智能修复
                if (direction > 0 && offsetDiff > 0)
                {
                    // 向下滚动且有正偏移：跳到下一节（再加上额外的节数）
                    targetVerseIndex = Math.Min(BibleVerseList.Items.Count - 1, currentVerseIndex + count);
//#if DEBUG
//                    Debug.WriteLine($"⚠️ [未对齐] 当前{currentOffset:F1}px，节{currentVerseIndex + 1}应在{currentVerseOffset:F1}px，偏移{offsetDiff:F1}px");
//#endif
                }
                else if (direction < 0)
                {
                    // 向上滚动：先对齐到当前节，然后再向上移动 (count-1) 节
                    // 如果 count=1，就对齐到当前节；如果 count=2，就到上一节；以此类推
                    targetVerseIndex = Math.Max(0, currentVerseIndex - (count - 1));
//#if DEBUG
//                    Debug.WriteLine($"⚠️ [未对齐] 当前{currentOffset:F1}px，节{currentVerseIndex + 1}应在{currentVerseOffset:F1}px，偏移{offsetDiff:F1}px");
//#endif
                }
                else
                {
                    // 负偏移：对齐到当前节
                    targetVerseIndex = currentVerseIndex;
//#if DEBUG
//                    Debug.WriteLine($"⚠️ [未对齐] 当前{currentOffset:F1}px，节{currentVerseIndex + 1}应在{currentVerseOffset:F1}px，偏移{offsetDiff:F1}px");
//#endif
                }
            }
            
            // 如果已经在边界且已对齐，直接返回
            if (targetVerseIndex == currentVerseIndex && isAligned &&
                ((direction < 0 && currentOffset <= 0) || 
                 (direction > 0 && currentOffset >= BibleVerseScrollViewer.ScrollableHeight)))
            {
                // Debug.WriteLine($"🖱️ [滚轮对齐] 已到达边界，忽略");
                return;
            }

            // 直接跳转到目标经文（无动画，更流畅）
            _currentTargetVerseIndex = targetVerseIndex;
            ScrollToVerseInstant(targetVerseIndex);
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

        /// <summary>
        /// 将选中的经文填充到目标文本框
        /// </summary>
        /// <summary>
        /// 创建圣经文本框元素（重构版 - 自动化流程）
        /// </summary>
        private async Task CreateBibleTextElements(int bookId, int chapter, int startVerse, int endVerse)
        {
            try
            {
                //#if DEBUG
                //Debug.WriteLine($"🔍 [圣经插入] 开始创建圣经文本元素: BookId={bookId}, Chapter={chapter}, StartVerse={startVerse}, EndVerse={endVerse}");
                //#endif

                // 1. 获取经文内容
                var verses = await _bibleService.GetVerseRangeAsync(bookId, chapter, startVerse, endVerse);

                //#if DEBUG
                //Debug.WriteLine($"🔍 [圣经插入] 获取到经文数量: {verses?.Count ?? 0}");
                //if (verses != null && verses.Count > 0)
                //{
                //    foreach (var v in verses)
                //    {
                //        Debug.WriteLine($"🔍 [圣经插入] Verse={v.Verse}, DisplayVerseNumber={v.DisplayVerseNumber}, Scripture={v.Scripture?.Substring(0, Math.Min(20, v.Scripture?.Length ?? 0))}...");
                //    }
                //}
                //else
                //{
                //    Debug.WriteLine($"❌ [圣经插入] 经文列表为空或null");
                //}
                //#endif
                
                // 2. 生成引用
                var book = BibleBookConfig.GetBook(bookId);
                string reference = (startVerse == endVerse) 
                    ? $"{book.Name}{chapter}章{startVerse}节" 
                    : $"{book.Name}{chapter}章{startVerse}-{endVerse}节";
                
                // 3. 格式化经文（带节号）
                string verseContent = FormatVerseWithNumbers(verses);
                
                // 4. 加载样式配置（从数据库）
                var config = LoadBibleInsertConfigFromDatabase();
                
                //#if DEBUG
                //Debug.WriteLine($"✅ [圣经创建] 开始创建文本框元素");
                //Debug.WriteLine($"   引用: {reference}");
                //Debug.WriteLine($"   经文数: {verses.Count}");
                //Debug.WriteLine($"   样式布局: {config.Style}");
                //Debug.WriteLine($"   统一字体: {config.FontFamily}");
                //#endif
                
                // 5. 智能计算插入位置
                var insertPosition = GetSmartInsertPosition();
                double startX = insertPosition.X;
                double startY = insertPosition.Y;
                
                switch (config.Style)
                {
                    case BibleTextInsertStyle.TitleOnTop:
                        // 标题在上，经文在下
                        await CreateSingleTextElement(
                            content: $"[{reference}]",
                            x: startX,
                            y: startY,
                            fontFamily: config.FontFamily,
                            fontSize: config.TitleStyle.FontSize,
                            color: config.TitleStyle.ColorHex,
                            isBold: config.TitleStyle.IsBold
                        );

                        // 使用富文本方式创建经文（节号+经文内容）
                        await CreateRichTextVerseElement(
                            verses: verses,
                            x: startX,
                            y: startY + config.TitleStyle.FontSize * 1.5f + 20, // 标题高度 + 间距
                            config: config
                        );
                        break;
                        
                    case BibleTextInsertStyle.TitleAtBottom:
                        // 经文在上，标题在下
                        int verseLineCount = verses.Count;
                        // 计算经文高度：行数 × 字体大小 × 行间距倍数
                        double verseHeight = verseLineCount * config.VerseStyle.FontSize * config.VerseStyle.VerseSpacing;

                        // 使用富文本方式创建经文（节号+经文内容）
                        await CreateRichTextVerseElement(
                            verses: verses,
                            x: startX,
                            y: startY,
                            config: config
                        );

                        await CreateSingleTextElement(
                            content: $"[{reference}]",
                            x: startX,
                            y: startY + verseHeight + 20, // 经文高度 + 间距
                            fontFamily: config.FontFamily,
                            fontSize: config.TitleStyle.FontSize,
                            color: config.TitleStyle.ColorHex,
                            isBold: config.TitleStyle.IsBold
                        );
                        break;

                    case BibleTextInsertStyle.InlineAtEnd:
                        // 标注在末尾（使用富文本：节号+经文+标题）
                        await CreateRichTextVerseWithTitleElement(
                            verses: verses,
                            reference: reference,
                            x: startX,
                            y: startY,
                            config: config
                        );
                        break;
                        
                    default:
                        // 默认：标题在上
                        await CreateSingleTextElement(
                            content: $"[{reference}]",
                            x: startX,
                            y: startY,
                            fontFamily: config.FontFamily,
                            fontSize: config.TitleStyle.FontSize,
                            color: config.TitleStyle.ColorHex,
                            isBold: config.TitleStyle.IsBold
                        );
                        
                        await CreateSingleTextElement(
                            content: verseContent,
                            x: startX,
                            y: startY + config.TitleStyle.FontSize * 1.5f + 20,
                            fontFamily: config.FontFamily,
                            fontSize: config.VerseStyle.FontSize,
                            color: config.VerseStyle.ColorHex,
                            isBold: config.VerseStyle.IsBold
                        );
                        break;
                }
                
                // 6. 自动隐藏圣经导航栏
                if (config.AutoHideNavigationAfterInsert && 
                    BibleNavigationPanel.Visibility == Visibility.Visible)
                {
                    BibleNavigationPanel.Visibility = Visibility.Collapsed;
                    ProjectTree.Visibility = Visibility.Visible;
                    
                    // 更新视图模式为幻灯片模式，并更新按钮高亮状态
                    _currentViewMode = NavigationViewMode.Projects;
                    UpdateViewModeButtons();
                    
                    //#if DEBUG
                    //Debug.WriteLine($"✅ [圣经创建] 已自动隐藏圣经导航栏，切换到幻灯片模式");
                    //#endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [圣经创建] 创建文本框元素失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
                
                WpfMessageBox.Show("创建经文元素失败", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 格式化经文（带节号）
        /// 🔧 支持"-"节的合并显示（使用DisplayVerseNumber）
        /// </summary>
        private string FormatVerseWithNumbers(List<BibleVerse> verses)
        {
            var lines = new List<string>();
            foreach (var verse in verses)
            {
                // 🔧 优先使用DisplayVerseNumber（处理"-"节合并后的节号，如"10、11"）
                // 注意：需要检查空字符串，不仅仅是null
                var verseNumber = string.IsNullOrEmpty(verse.DisplayVerseNumber)
                    ? verse.Verse.ToString()
                    : verse.DisplayVerseNumber;
                lines.Add($"{verseNumber} {verse.Scripture}");

                //#if DEBUG
                //Debug.WriteLine($"[格式化经文] Verse={verse.Verse}, DisplayVerseNumber={verse.DisplayVerseNumber}, 使用节号={verseNumber}");
                //#endif
            }
            return string.Join("\n", lines);
        }
        
        
        /// <summary>
        /// 创建单个文本框元素（核心方法）
        /// </summary>
        private async Task CreateSingleTextElement(
            string content, 
            double x, 
            double y, 
            string fontFamily, 
            float fontSize, 
            string color, 
            bool isBold)
        {
            if (_currentSlide == null)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [圣经创建] 当前没有选中的幻灯片");
                #endif
                return;
            }
            
            try
            {
                // 计算最大ZIndex，新文本在最上层
                int maxZIndex = 0;
                if (_textBoxes.Count > 0)
                {
                    maxZIndex = _textBoxes.Max(tb => tb.Data.ZIndex);
                }
                
                // 创建新元素
                // 计算合理的高度：行数 * 行高
                int lineCount = content.Split('\n').Length;
                float estimatedHeight = lineCount * fontSize * 1.5f; // 行高 = 字号 * 1.5

                var textElement = new Database.Models.TextElement
                {
                    SlideId = _currentSlide.Id,
                    Content = content,
                    X = x,
                    Y = y,
                    Width = EditorCanvas.ActualWidth * 0.9, // 画布宽度的90%
                    Height = estimatedHeight, // 根据内容估算高度
                    FontFamily = fontFamily,
                    FontSize = fontSize,
                    FontColor = color,
                    IsBold = isBold ? 1 : 0,
                    ZIndex = maxZIndex + 1
                };
                
                // 保存到数据库
                await _textProjectManager.AddElementAsync(textElement);
                
                // 在 UI 线程上创建 DraggableTextBox 并添加到画布
                await Dispatcher.InvokeAsync(() =>
                {
                    var textBox = new UI.Controls.DraggableTextBox(textElement);
                    AddTextBoxToCanvas(textBox);
                    
                    // 标记内容已修改
                    MarkContentAsModified();
                    
                    //#if DEBUG
                    //Debug.WriteLine($"✅ [圣经创建] 文本框已添加到画布");
                    //Debug.WriteLine($"   内容: {content}");
                    //Debug.WriteLine($"   位置: ({x}, {y})");
                    //Debug.WriteLine($"   尺寸: {textBox.Width} x {textBox.Height}");
                    //Debug.WriteLine($"   字体: {fontFamily} {fontSize / 2}pt (数据库) -> {fontSize}pt (显示)");
                    //Debug.WriteLine($"   颜色: {color}");
                    //Debug.WriteLine($"   粗体: {isBold}");
                    //Debug.WriteLine($"   ZIndex: {textElement.ZIndex}");
                    //Debug.WriteLine($"   EditorCanvas.Children.Count: {EditorCanvas.Children.Count}");
                    //Debug.WriteLine($"   _textBoxes.Count: {_textBoxes.Count}");
                    //#endif
                });
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [圣经创建] 创建单个文本框失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }

        /// <summary>
        /// 创建富文本经文元素（节号+经文内容，使用 RichTextSpan）
        /// </summary>
        private async Task CreateRichTextVerseElement(
            List<BibleVerse> verses,
            double x,
            double y,
            BibleTextInsertConfig config)
        {
            //#if DEBUG
            //Debug.WriteLine($"🔍 [CreateRichTextVerseElement] 开始创建富文本经文元素");
            //Debug.WriteLine($"   参数: verses.Count={verses?.Count ?? 0}, x={x}, y={y}");
            //Debug.WriteLine($"   配置: FontFamily={config?.FontFamily}, VerseSize={config?.VerseStyle?.FontSize}, VerseColor={config?.VerseStyle?.ColorHex}");
            //Debug.WriteLine($"   配置: NumberSize={config?.VerseNumberStyle?.FontSize}, NumberColor={config?.VerseNumberStyle?.ColorHex}");
            //Debug.WriteLine($"   当前幻灯片: {(_currentSlide != null ? $"ID={_currentSlide.Id}" : "null")}");
            //#endif

            if (_currentSlide == null || verses == null || verses.Count == 0)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [圣经创建] 当前没有选中的幻灯片或经文为空");
                Debug.WriteLine($"   _currentSlide == null: {_currentSlide == null}");
                Debug.WriteLine($"   verses == null: {verses == null}");
                Debug.WriteLine($"   verses.Count: {verses?.Count ?? 0}");
                #endif
                return;
            }

            try
            {
                // 计算最大ZIndex，新文本在最上层
                int maxZIndex = 0;
                if (_textBoxes.Count > 0)
                {
                    maxZIndex = _textBoxes.Max(tb => tb.Data.ZIndex);
                }

                // 构建完整文本内容（用于显示）
                var contentBuilder = new System.Text.StringBuilder();
                foreach (var verse in verses)
                {
                    if (contentBuilder.Length > 0)
                        contentBuilder.AppendLine();
                    contentBuilder.Append($"{verse.Verse} {verse.Scripture}");
                }
                string fullContent = contentBuilder.ToString();

                // 节距直接使用行间距倍数（1.0-2.5）
                double lineSpacing = config.VerseStyle.VerseSpacing;

                // 计算高度：行数 × 字体大小 × 行间距倍数
                int lineCount = verses.Count;
                float estimatedHeight = lineCount * config.VerseStyle.FontSize * (float)lineSpacing;

                //#if DEBUG
                //Debug.WriteLine($"🔍 [CreateRichTextVerseElement] 行间距={lineSpacing:F1}");
                //#endif

                // 创建文本元素
                var textElement = new Database.Models.TextElement
                {
                    SlideId = _currentSlide.Id,
                    Content = fullContent,
                    X = x,
                    Y = y,
                    Width = EditorCanvas.ActualWidth * 0.9,
                    Height = estimatedHeight,
                    FontFamily = config.FontFamily,
                    FontSize = config.VerseStyle.FontSize,
                    FontColor = config.VerseStyle.ColorHex,
                    IsBold = config.VerseStyle.IsBold ? 1 : 0,
                    LineSpacing = lineSpacing,  // 应用行间距
                    ZIndex = maxZIndex + 1
                };

                // 保存到数据库
                await _textProjectManager.AddElementAsync(textElement);

                // 创建富文本片段（RichTextSpan）
                var richTextSpans = new List<Database.Models.RichTextSpan>();
                int spanOrder = 0;

                //#if DEBUG
                //Debug.WriteLine($"🔍 [CreateRichTextVerseElement] 开始创建富文本片段，经文数量: {verses.Count}");
                //#endif

                foreach (var verse in verses)
                {
                    //#if DEBUG
                    //Debug.WriteLine($"   处理第 {verse.Verse} 节: {verse.Scripture?.Substring(0, Math.Min(20, verse.Scripture?.Length ?? 0))}...");
                    //#endif

                    // 🔧 节号片段（优先使用DisplayVerseNumber，支持"-"节合并显示）
                    var verseNumber = string.IsNullOrEmpty(verse.DisplayVerseNumber)
                        ? verse.Verse.ToString()
                        : verse.DisplayVerseNumber;

                    richTextSpans.Add(new Database.Models.RichTextSpan
                    {
                        TextElementId = textElement.Id,
                        SpanOrder = spanOrder++,
                        Text = verseNumber,
                        FontFamily = config.FontFamily,
                        FontSize = config.VerseNumberStyle.FontSize,
                        FontColor = config.VerseNumberStyle.ColorHex,
                        IsBold = config.VerseNumberStyle.IsBold ? 1 : 0
                    });

                    // 空格片段
                    richTextSpans.Add(new Database.Models.RichTextSpan
                    {
                        TextElementId = textElement.Id,
                        SpanOrder = spanOrder++,
                        Text = " ",
                        FontFamily = config.FontFamily,
                        FontSize = config.VerseStyle.FontSize,
                        FontColor = config.VerseStyle.ColorHex,
                        IsBold = config.VerseStyle.IsBold ? 1 : 0
                    });

                    // 经文内容片段
                    richTextSpans.Add(new Database.Models.RichTextSpan
                    {
                        TextElementId = textElement.Id,
                        SpanOrder = spanOrder++,
                        Text = verse.Scripture,
                        FontFamily = config.FontFamily,
                        FontSize = config.VerseStyle.FontSize,
                        FontColor = config.VerseStyle.ColorHex,
                        IsBold = config.VerseStyle.IsBold ? 1 : 0
                    });

                    // 换行片段（除了最后一节）
                    if (verse != verses.Last())
                    {
                        richTextSpans.Add(new Database.Models.RichTextSpan
                        {
                            TextElementId = textElement.Id,
                            SpanOrder = spanOrder++,
                            Text = "\n",
                            FontFamily = config.FontFamily,
                            FontSize = config.VerseStyle.FontSize,
                            FontColor = config.VerseStyle.ColorHex,
                            IsBold = config.VerseStyle.IsBold ? 1 : 0
                        });
                    }
                }

                // 保存富文本片段到数据库
                //#if DEBUG
                //Debug.WriteLine($"🔍 [CreateRichTextVerseElement] 保存 {richTextSpans.Count} 个富文本片段到数据库");
                //#endif

                foreach (var span in richTextSpans)
                {
                    await _textProjectManager.AddRichTextSpanAsync(span);
                }

                //#if DEBUG
                //Debug.WriteLine($"✅ [CreateRichTextVerseElement] 富文本片段保存完成");
                //#endif

                // 将富文本片段关联到文本元素
                textElement.RichTextSpans = richTextSpans;

                //#if DEBUG
                //Debug.WriteLine($"🔍 [CreateRichTextVerseElement] 创建 DraggableTextBox 并添加到画布");
                //Debug.WriteLine($"   TextElement.Id={textElement.Id}, Content长度={textElement.Content?.Length ?? 0}");
                //Debug.WriteLine($"   RichTextSpans数量={textElement.RichTextSpans?.Count ?? 0}");
                //#endif

                // 在 UI 线程上创建 DraggableTextBox 并添加到画布
                await Dispatcher.InvokeAsync(() =>
                {
                    var textBox = new UI.Controls.DraggableTextBox(textElement);
                    AddTextBoxToCanvas(textBox);
                    MarkContentAsModified();

                    //#if DEBUG
                    //Debug.WriteLine($"✅ [CreateRichTextVerseElement] 文本框已添加到画布");
                    //#endif
                });
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [圣经创建] 创建富文本经文元素失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
        }

        /// <summary>
        /// 创建富文本经文+标题元素（节号+经文内容+标题，使用 RichTextSpan）
        /// </summary>
        private async Task CreateRichTextVerseWithTitleElement(
            List<BibleVerse> verses,
            string reference,
            double x,
            double y,
            BibleTextInsertConfig config)
        {
            if (_currentSlide == null || verses == null || verses.Count == 0)
            {
                return;
            }

            try
            {
                int maxZIndex = 0;
                if (_textBoxes.Count > 0)
                {
                    maxZIndex = _textBoxes.Max(tb => tb.Data.ZIndex);
                }

                // 构建完整文本内容
                var contentBuilder = new System.Text.StringBuilder();
                foreach (var verse in verses)
                {
                    if (contentBuilder.Length > 0)
                        contentBuilder.Append(" ");
                    contentBuilder.Append($"{verse.Verse} {verse.Scripture}");
                }
                contentBuilder.Append($" [{reference}]");
                string fullContent = contentBuilder.ToString();

                // 计算高度
                float estimatedHeight = config.VerseStyle.FontSize * 1.5f;

                var textElement = new Database.Models.TextElement
                {
                    SlideId = _currentSlide.Id,
                    Content = fullContent,
                    X = x,
                    Y = y,
                    Width = EditorCanvas.ActualWidth * 0.9,
                    Height = estimatedHeight,
                    FontFamily = config.FontFamily,
                    FontSize = config.VerseStyle.FontSize,
                    FontColor = config.VerseStyle.ColorHex,
                    IsBold = config.VerseStyle.IsBold ? 1 : 0,
                    ZIndex = maxZIndex + 1
                };

                await _textProjectManager.AddElementAsync(textElement);

                // 创建富文本片段
                var richTextSpans = new List<Database.Models.RichTextSpan>();
                int spanOrder = 0;

                foreach (var verse in verses)
                {
                    // 🔧 节号（优先使用DisplayVerseNumber，支持"-"节合并显示）
                    var verseNumber = string.IsNullOrEmpty(verse.DisplayVerseNumber)
                        ? verse.Verse.ToString()
                        : verse.DisplayVerseNumber;

                    richTextSpans.Add(new Database.Models.RichTextSpan
                    {
                        TextElementId = textElement.Id,
                        SpanOrder = spanOrder++,
                        Text = verseNumber,
                        FontFamily = config.FontFamily,
                        FontSize = config.VerseNumberStyle.FontSize,
                        FontColor = config.VerseNumberStyle.ColorHex,
                        IsBold = 1
                    });

                    // 空格
                    richTextSpans.Add(new Database.Models.RichTextSpan
                    {
                        TextElementId = textElement.Id,
                        SpanOrder = spanOrder++,
                        Text = " ",
                        FontFamily = config.FontFamily,
                        FontSize = config.VerseStyle.FontSize,
                        FontColor = config.VerseStyle.ColorHex,
                        IsBold = config.VerseStyle.IsBold ? 1 : 0
                    });

                    // 经文内容
                    richTextSpans.Add(new Database.Models.RichTextSpan
                    {
                        TextElementId = textElement.Id,
                        SpanOrder = spanOrder++,
                        Text = verse.Scripture,
                        FontFamily = config.FontFamily,
                        FontSize = config.VerseStyle.FontSize,
                        FontColor = config.VerseStyle.ColorHex,
                        IsBold = config.VerseStyle.IsBold ? 1 : 0
                    });

                    // 空格（除了最后一节）
                    if (verse != verses.Last())
                    {
                        richTextSpans.Add(new Database.Models.RichTextSpan
                        {
                            TextElementId = textElement.Id,
                            SpanOrder = spanOrder++,
                            Text = " ",
                            FontFamily = config.FontFamily,
                            FontSize = config.VerseStyle.FontSize,
                            FontColor = config.VerseStyle.ColorHex,
                            IsBold = config.VerseStyle.IsBold ? 1 : 0
                        });
                    }
                }

                // 标题片段
                richTextSpans.Add(new Database.Models.RichTextSpan
                {
                    TextElementId = textElement.Id,
                    SpanOrder = spanOrder++,
                    Text = $" [{reference}]",
                    FontFamily = config.FontFamily,
                    FontSize = config.TitleStyle.FontSize,
                    FontColor = config.TitleStyle.ColorHex,
                    IsBold = config.TitleStyle.IsBold ? 1 : 0
                });

                // 保存富文本片段
                foreach (var span in richTextSpans)
                {
                    await _textProjectManager.AddRichTextSpanAsync(span);
                }

                textElement.RichTextSpans = richTextSpans;

                await Dispatcher.InvokeAsync(() =>
                {
                    var textBox = new UI.Controls.DraggableTextBox(textElement);
                    AddTextBoxToCanvas(textBox);
                    MarkContentAsModified();
                });
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [圣经创建] 创建富文本经文+标题元素失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
        }

        /// <summary>
        /// 主窗口失去焦点时，关闭圣经样式 Popup
        /// 注意：不再自动关闭所有侧边面板，只关闭圣经相关组件
        /// </summary>
        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            // 关闭圣经样式 Popup
            if (_bibleStylePopup != null && _bibleStylePopup.IsOpen)
            {
                _bibleStylePopup.IsOpen = false;
            }

            // 关闭文本编辑悬浮工具栏（修复：切换到其他软件时工具栏仍显示）
            if (BibleToolbar != null && BibleToolbar.IsOpen)
            {
                BibleToolbar.IsOpen = false;
            }

            // 隐藏圣经译本选择工具栏
            if (BibleVersionToolbar != null && BibleVersionToolbar.Visibility == Visibility.Visible)
            {
                BibleVersionToolbar.Visibility = Visibility.Collapsed;
            }

            // 注意：不再自动关闭所有侧边面板，让用户通过ESC或点击来控制
            // CloseAllSidePanels(); // 移除这行
            // 取消编辑框选中：移除这行，让用户通过ESC控制
            // if (_selectedTextBox != null)
            // {
            //     _selectedTextBox.SetSelected(false);
            //     _selectedTextBox = null;
            // }
        }
        
        /// <summary>
        /// 主窗口状态变化时（最小化、最大化等），关闭圣经样式 Popup
        /// 注意：不再自动关闭所有侧边面板，只关闭圣经相关组件
        /// </summary>
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            // 关闭圣经样式 Popup
            if (_bibleStylePopup != null && _bibleStylePopup.IsOpen)
            {
                _bibleStylePopup.IsOpen = false;
            }

            // 隐藏圣经译本选择工具栏
            if (BibleVersionToolbar != null && BibleVersionToolbar.Visibility == Visibility.Visible)
            {
                BibleVersionToolbar.Visibility = Visibility.Collapsed;
            }

            if (WindowState == WindowState.Minimized)
            {
                if (BibleToolbar != null)
                {
                    BibleToolbar.IsOpen = false;
                }

                CloseAllSidePanels();
            }

            // 注意：不再自动关闭所有侧边面板，让用户通过ESC或点击来控制
            // CloseAllSidePanels(); // 移除这行
            // 取消编辑框选中：移除这行，让用户通过ESC控制
            // if (_selectedTextBox != null)
            // {
            //     _selectedTextBox.SetSelected(false);
            //     _selectedTextBox = null;
            // }
        }
        
        /// <summary>
        /// 主窗口位置变化时，关闭圣经样式 Popup
        /// 注意：不再自动关闭所有侧边面板，只关闭圣经相关组件
        /// </summary>
        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            // 关闭圣经样式 Popup
            if (_bibleStylePopup != null && _bibleStylePopup.IsOpen)
            {
                _bibleStylePopup.IsOpen = false;
            }

            // 隐藏圣经译本选择工具栏
            if (BibleVersionToolbar != null && BibleVersionToolbar.Visibility == Visibility.Visible)
            {
                BibleVersionToolbar.Visibility = Visibility.Collapsed;
            }

            // 注意：不再自动关闭所有侧边面板，让用户通过ESC或点击来控制
            // CloseAllSidePanels(); // 移除这行
            // 取消编辑框选中：移除这行，让用户通过ESC控制
            // if (_selectedTextBox != null)
            // {
            //     _selectedTextBox.SetSelected(false);
            //     _selectedTextBox = null;
            // }
        }

    /// <summary>
    /// 智能计算经文插入位置
    /// </summary>
    private System.Windows.Point GetSmartInsertPosition()
    {
        const double margin = 20;  // 边距
        const double spacing = 30; // 元素间距
        
        try
        {
            // 如果Canvas为空，返回左上角位置
            if (_textBoxes.Count == 0)
            {
                //#if DEBUG
                //Debug.WriteLine($"📍 [智能插入] Canvas为空，插入到左上角: ({margin}, {margin})");
                //#endif
                
                return new System.Windows.Point(margin, margin);
            }
                
                // 找到最后一个文本框（ZIndex最大的）
                var lastTextBox = _textBoxes.OrderByDescending(tb => Canvas.GetZIndex(tb)).FirstOrDefault();
                
                if (lastTextBox != null)
                {
                    double lastX = Canvas.GetLeft(lastTextBox);
                    double lastY = Canvas.GetTop(lastTextBox);
                    double lastHeight = lastTextBox.ActualHeight > 0 ? lastTextBox.ActualHeight : 100;
                    
                    // 在最后一个元素下方插入
                    double newX = lastX;
                    double newY = lastY + lastHeight + spacing;
                    
                    // 如果超出Canvas底部，则重新开始一列
                    if (newY + 200 > EditorCanvas.ActualHeight && EditorCanvas.ActualHeight > 0)
                    {
                        double lastWidth = lastTextBox.ActualWidth > 0 ? lastTextBox.ActualWidth : 300;
                        newX = lastX + lastWidth + spacing;
                        newY = margin;
                        
                        // 如果右侧也超出，则回到左上角
                        if (newX + 300 > EditorCanvas.ActualWidth && EditorCanvas.ActualWidth > 0)
                        {
                            newX = margin;
                            newY = margin;
                        }
                    }
                    
                    //#if DEBUG
                    //Debug.WriteLine($"📍 [智能插入] 在最后元素下方: ({newX:F0}, {newY:F0})");
                    //Debug.WriteLine($"   最后元素位置: ({lastX:F0}, {lastY:F0}), 高度: {lastHeight:F0}");
                    //#endif
                    
                    return new System.Windows.Point(newX, newY);
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"⚠️ [智能插入] 计算位置失败: {ex.Message}，使用默认位置");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
            
            // 默认位置
            return new System.Windows.Point(margin, margin);
        }
        
        /// <summary>
        /// 从数据库加载圣经插入配置
        /// </summary>
        private BibleTextInsertConfig LoadBibleInsertConfigFromDatabase()
        {
            var config = new BibleTextInsertConfig();

            // 从数据库加载配置
            config.Style = (BibleTextInsertStyle)int.Parse(_dbManager.GetBibleInsertConfigValue("style", "0"));
            config.FontFamily = _dbManager.GetBibleInsertConfigValue("font_family", "DengXian");

            config.TitleStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("title_color", "#FF0000");
            config.TitleStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("title_size", "50"));
            config.TitleStyle.IsBold = _dbManager.GetBibleInsertConfigValue("title_bold", "1") == "1";

            config.VerseStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("verse_color", "#FF9A35");
            config.VerseStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("verse_size", "40"));
            config.VerseStyle.IsBold = _dbManager.GetBibleInsertConfigValue("verse_bold", "0") == "1";
            config.VerseStyle.VerseSpacing = float.Parse(_dbManager.GetBibleInsertConfigValue("verse_spacing", "1.2"));

            config.VerseNumberStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("verse_number_color", "#FFFF00");
            config.VerseNumberStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("verse_number_size", "40"));
            config.VerseNumberStyle.IsBold = _dbManager.GetBibleInsertConfigValue("verse_number_bold", "1") == "1";

            config.AutoHideNavigationAfterInsert = _dbManager.GetBibleInsertConfigValue("auto_hide_navigation", "1") == "1";
            
            //#if DEBUG
            //Debug.WriteLine($"📝 [圣经插入] 从数据库加载配置");
            //Debug.WriteLine($"   字体: {config.FontFamily}");
            //Debug.WriteLine($"   标题字体大小（实际值 = 显示值×2）: {config.TitleStyle.FontSize}");
            //Debug.WriteLine($"   经文字体大小（实际值 = 显示值×2）: {config.VerseStyle.FontSize}");
            //#endif
            
            return config;
        }

        #endregion

    }
}
