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
using ImageColorChanger.UI.Modules;
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
    /// MainWindow Bible Search (Search, History Buttons)
    /// </summary>
    public partial class MainWindow
    {
        #region 圣经搜索

        /// <summary>
        /// 搜索圣经经文
        /// </summary>
        private async Task SearchBibleAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return;

            try
            {
                //#if DEBUG
                //var sw = Stopwatch.StartNew();
                //#endif

                var results = await _bibleService.SearchVersesAsync(keyword);

                //#if DEBUG
                //sw.Stop();
                //Debug.WriteLine($"[圣经] 搜索 '{keyword}': {sw.ElapsedMilliseconds}ms, 结果数: {results.Count}");
                //#endif

                // TODO: 显示搜索结果
                // ShowBibleSearchResults(results);
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经] 搜索失败: {ex.Message}");
                //#endif

                WpfMessageBox.Show(
                    $"搜索失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region 圣经历史记录按钮事件

        /// <summary>
        /// 初始化历史槽位（1-20号）
        /// </summary>
        private void InitializeHistorySlots()
        {
            _historySlots.Clear();
            
            // 创建20个空槽位
            for (int i = 1; i <= 20; i++)
            {
                _historySlots.Add(new BibleHistoryItem
                {
                    Index = i,
                    DisplayText = "",
                    BookId = 0,
                    Chapter = 0,
                    StartVerse = 0,
                    EndVerse = 0,
                    IsChecked = (i == 1) // 默认勾选第一个槽位
                });
            }
            
            //#if DEBUG
            //Debug.WriteLine("[圣经] 初始化20个历史槽位，默认勾选槽位1");
            //#endif
        }

        /// <summary>
        /// 历史记录项点击事件 - 点击整行切换勾选状态并加载经文
        /// 支持双击检测：双击切换锁定状态
        /// </summary>
        private async void BibleHistoryItem_Click(object sender, MouseButtonEventArgs e)
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[历史记录点击] ========== 开始 ==========");
            //#endif

            if (sender is Border border && border.DataContext is BibleHistoryItem item)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   槽位: {item.Index}, 显示文本: {item.DisplayText}");
                //System.Diagnostics.Debug.WriteLine($"   当前状态: IsChecked={item.IsChecked}, IsLocked={item.IsLocked}");
                //#endif

                // 双击检测
                var now = DateTime.Now;
                var interval = (now - _lastHistoryClickTime).TotalMilliseconds;
                var isDoubleClick = interval < DoubleClickInterval && _lastHistoryClickedItem == item;

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   点击间隔: {interval:F0}ms, 是否双击: {isDoubleClick}");
                //#endif

                _lastHistoryClickTime = now;
                _lastHistoryClickedItem = item;

                if (isDoubleClick)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"    检测到双击，切换锁定状态");
                    //#endif

                    // 双击：切换锁定状态
                    bool wasLocked = item.IsLocked;
                    item.IsLocked = !item.IsLocked;

                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   锁定状态: {wasLocked} -> {item.IsLocked}");
                    //#endif

                    // 锁定后自动勾选（但不触发单击逻辑）
                    if (item.IsLocked && !item.IsChecked)
                    {
                        // 使用私有字段直接设置，避免触发PropertyChanged
                        item.IsChecked = true;
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   自动勾选槽位");
                        //#endif
                    }

                    // 强制刷新列表显示以确保边框更新
                    BibleHistoryList.Items.Refresh();

                    // 增量更新：根据锁定状态决定是添加还是删除
                    if (item.IsLocked)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   添加锁定记录的经文");
                        //#endif
                        // 新增锁定：插入该记录的经文
                        await AddLockedRecordVerses(item);
                    }
                    else
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   删除锁定记录的经文");
                        //#endif
                        // 取消锁定：删除该记录的经文
                        RemoveLockedRecordVerses(item);
                    }

                    // 更新投影
                    await UpdateProjectionFromMergedVerses();

                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[历史记录点击] ========== 结束（双击） ==========\n");
                    //#endif
                    return;
                }

                // 单击逻辑：检查是否有锁定记录
                bool hasLockedRecords = _historySlots.Any(x => x.IsLocked);

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   是否有锁定记录: {hasLockedRecords}");
                //#endif

                if (hasLockedRecords)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   锁定模式：切换勾选状态，不切换主屏幕");
                    //#endif
                    // 有锁定记录时：允许勾选，但不切换主屏幕内容
                    item.IsChecked = !item.IsChecked;
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   勾选状态: {item.IsChecked}");
                    //System.Diagnostics.Debug.WriteLine($"[历史记录点击] ========== 结束（锁定模式单击） ==========\n");
                    //#endif
                    return;
                }

                // 无锁定记录时：单选模式
                if (!item.IsChecked)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   单选模式：取消其他槽位勾选，勾选当前槽位");
                    //#endif

                    // 槽位未勾选：取消其他所有记录的勾选，并勾选当前记录
                    foreach (var slot in _historySlots)
                    {
                        if (slot != item)
                        {
                            slot.IsChecked = false;
                        }
                    }

                    // 勾选当前记录
                    item.IsChecked = true;

                    // 如果有有效经文数据，则加载经文
                    if (item.BookId > 0)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   加载经文: BookId={item.BookId}, Chapter={item.Chapter}, Verse={item.StartVerse}-{item.EndVerse}");
                        //#endif
                        await HandleBibleHistoryItemVerseSelectionAsync(item);
                    }
                    else
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   空白记录：清空主屏幕和投影");
                        //#endif
                        // 空白记录：清空主屏幕和投影屏幕
                        _mergedVerses.Clear();
                        BibleChapterTitle.Text = "";
                        BibleChapterTitleBorder.Visibility = Visibility.Visible;

                        if (_projectionManager != null && _projectionManager.IsProjecting)
                        {
                            _projectionManager.ClearProjectionDisplay();
                        }
                    }
                }
                else
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   槽位已勾选：重新加载经文");
                    //#endif

                    // 槽位已勾选：重新加载该经文（用于从保存的配置恢复后点击）
                    // 如果有有效经文数据，则加载经文到主屏幕
                    if (item.BookId > 0)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   重新加载经文: BookId={item.BookId}, Chapter={item.Chapter}, Verse={item.StartVerse}-{item.EndVerse}");
                        //#endif
                        await HandleBibleHistoryItemVerseSelectionAsync(item);
                    }
                }

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[历史记录点击] ========== 结束（单击） ==========\n");
                //#endif
            }
        }

        /// <summary>
        /// 历史记录列表选择事件
        /// 注意：实际加载经文由BibleHistoryItem_Click处理，此处不重复加载
        /// </summary>
        private void BibleHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 此事件暂时保留，用于未来可能的选中状态同步
            // 实际的经文加载由BibleHistoryItem_Click事件处理，避免重复加载
        }

        private async Task HandleBibleHistoryItemVerseSelectionAsync(BibleHistoryItem item)
        {
            if (item == null || item.BookId <= 0)
            {
                return;
            }

            bool shouldUseSlideFlow = BibleUiBehaviorResolver.ShouldUseHistorySlideFlow(
                TextEditorPanel?.Visibility == Visibility.Visible);

            if (shouldUseSlideFlow)
            {
                await HandleBibleVerseSelectionInSlideModeAsync(
                    item.BookId,
                    item.Chapter,
                    item.StartVerse,
                    item.EndVerse);
                return;
            }

            await LoadVerseRangeAsync(item.BookId, item.Chapter, item.StartVerse, item.EndVerse);
        }

        /// <summary>
        /// 全选历史记录
        /// </summary>
        private void BtnHistorySelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _historySlots)
            {
                item.IsChecked = true;
            }

            //#if DEBUG
            //Debug.WriteLine("[圣经] 全选历史槽位");
            //#endif
        }

        /// <summary>
        /// 全不选历史记录
        /// </summary>
        private void BtnHistoryDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _historySlots)
            {
                item.IsChecked = false;
            }

            //#if DEBUG
            //Debug.WriteLine("[圣经] 全不选历史槽位");
            //#endif
        }

        /// <summary>
        /// 清空勾选的历史记录
        /// </summary>
        private async void BtnHistoryClearSelected_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否有锁定记录
            var hasLocked = _historySlots.Any(x => x.IsLocked);
            
            if (hasLocked)
            {
                // 清空所有锁定状态和勾选状态
                foreach (var item in _historySlots)
                {
                    item.IsLocked = false;
                    item.IsChecked = false;  // 同时清空勾选状态，防止误覆盖
                }
                
                await LoadAndDisplayLockedRecords(); // 会清空显示
                
                // 更新清空按钮样式（从绿色恢复成白色）
                UpdateClearButtonStyle();
                
                // 刷新列表显示，确保界面更新
                BibleHistoryList.Items.Refresh();
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("[圣经] 已清空所有锁定和勾选");
                #endif
            }
            else
            {
                // 原有逻辑：清空勾选的历史记录
                var checkedItems = _historySlots.Where(h => h.IsChecked).ToList();
                
                foreach (var item in checkedItems)
                {
                    // 清空槽位内容
                    item.BookId = 0;
                    item.Chapter = 0;
                    item.StartVerse = 0;
                    item.EndVerse = 0;
                    item.DisplayText = "";
                    item.IsChecked = false;
                }

                // 刷新列表显示
                BibleHistoryList.Items.Refresh();

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[圣经] 清除了 {checkedItems.Count} 个勾选的槽位");
                #endif
            }
        }
        
        /// <summary>
        /// 加载并显示所有锁定记录（核心方法）
        /// 使用增量更新，避免刷新闪烁
        /// </summary>
        private async Task LoadAndDisplayLockedRecords()
        {
            try
            {
                // 获取所有锁定的记录（按槽位顺序）
                var lockedItems = _historySlots
                    .Where(x => x.IsLocked && x.BookId > 0)
                    .OrderBy(x => x.Index)
                    .ToList();
                
                if (lockedItems.Count == 0)
                {
                    // 没有锁定记录，清空显示
                    _mergedVerses.Clear();
                    BibleChapterTitle.Text = "";
                    
                    if (_projectionManager != null && _projectionManager.IsProjecting)
                    {
                        _projectionManager.ClearProjectionDisplay();
                    }
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine("[圣经] 无锁定记录，清空显示");
                    //#endif
                    return;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 开始合并 {lockedItems.Count} 条锁定记录");
                //#endif
                
                // 构建新的经文列表
                var newVerses = new List<BibleVerse>();
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[锁定模式] 开始加载 {lockedItems.Count} 条锁定记录");
                #endif
                
                foreach (var item in lockedItems)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[锁定模式] 加载槽位{item.Index}: {item.DisplayText}");
                    #endif
                    
                    // 添加分隔标题（Verse=0 标记为标题行）
                    newVerses.Add(new BibleVerse 
                    { 
                        Book = item.BookId,
                        Chapter = item.Chapter,
                        Verse = 0, // 标题行节号为0
                        Scripture = item.DisplayText // 使用DisplayText作为标题内容
                    });
                    
                    // 加载该记录的所有经文
                    for (int verse = item.StartVerse; verse <= item.EndVerse; verse++)
                    {
                        var verseData = await _bibleService.GetVerseAsync(item.BookId, item.Chapter, verse);
                        if (verseData != null)
                        {
                            newVerses.Add(verseData);
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"   添加经文: {item.BookId}章{item.Chapter}:{verse}节");
                            #endif
                        }
                    }
                }
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[锁定模式] 加载完成，共 {newVerses.Count} 行（含标题）");
                #endif
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 合并完成，共 {newVerses.Count} 行（含标题）");
                //#endif
                
                // 更新主屏幕标题（清空，因为合并模式下每组都有自己的标题）
                BibleChapterTitle.Text = "";
                
                // 增量更新：只在首次绑定时设置ItemsSource，后续使用Clear/Add
                if (BibleVerseList.ItemsSource != _mergedVerses)
                {
                    BibleVerseList.ItemsSource = _mergedVerses;
                }
                
                // 清空旧数据
                _mergedVerses.Clear();
                
                // 逐个添加新数据（ObservableCollection会自动通知UI更新，无闪烁）
                foreach (var verse in newVerses)
                {
                    _mergedVerses.Add(verse);
                }
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[锁定模式] 最终列表中共 {_mergedVerses.Count} 条记录");
                for (int i = 0; i < _mergedVerses.Count; i++)
                {
                    var v = _mergedVerses[i];
                    if (v.Verse == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"  [{i}] 标题: {v.Scripture}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  [{i}] 经文: {v.Book}章{v.Chapter}:{v.Verse}节");
                    }
                }
                #endif
                
                // 应用样式（包括标题行的特殊样式）
                await Dispatcher.InvokeAsync(() => 
                {
                    ApplyVerseStyles();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
                
                // 更新投影（使用 VisualBrush）
                if (_projectionManager != null && _projectionManager.IsProjecting && BibleVerseScrollViewer != null)
                {
                    _projectionManager.UpdateBibleProjectionWithVisualBrush(BibleVerseScrollViewer);
                }
            }
            catch (Exception)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 加载锁定记录失败: {ex.Message}");
                //#endif
            }
        }
        
        /// <summary>
        /// 增量添加锁定记录的经文（插入到正确位置）
        /// </summary>
        private async Task AddLockedRecordVerses(BibleHistoryItem item)
        {
            try
            {
                // 确保已绑定到 ObservableCollection
                if (BibleVerseList.ItemsSource != _mergedVerses)
                {
                    BibleVerseList.ItemsSource = _mergedVerses;
                }
                
                // 检查是否有锁定记录，如果有锁定记录，_mergedVerses应该只包含锁定记录的经文
                // 如果_mergedVerses中有非锁定模式的经文（没有标题行），应该先清空
                bool hasLockedRecords = _historySlots.Any(x => x.IsLocked && x.BookId > 0);
                if (hasLockedRecords)
                {
                    // 检查_mergedVerses中是否有标题行（锁定记录的标记）
                    bool hasTitleRow = _mergedVerses.Any(v => v.Verse == 0);
                    if (!hasTitleRow && _mergedVerses.Count > 0)
                    {
                        // _mergedVerses中有经文但没有标题行，说明是非锁定模式的经文，应该清空
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [增量添加] 检测到非锁定模式的经文，清空_mergedVerses（{_mergedVerses.Count}条）");
                        //#endif
                        _mergedVerses.Clear();
                    }
                }
                
                // 检查该记录是否已经存在，避免重复插入
                var book = BibleBookConfig.GetBook(item.BookId);
                string verseText = (item.StartVerse == item.EndVerse) ? $"{item.StartVerse}节" : $"{item.StartVerse}-{item.EndVerse}节";
                string titleText = $"{book?.Name}{item.Chapter}章{verseText}";
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[增量添加] 开始检查记录: {titleText}");
                //System.Diagnostics.Debug.WriteLine($"[增量添加] 当前列表总数: {_mergedVerses.Count}");
                //for (int i = 0; i < _mergedVerses.Count; i++)
                //{
                //    var v = _mergedVerses[i];
                //    if (v.Verse == 0)
                //    {
                //        System.Diagnostics.Debug.WriteLine($"  [{i}] 标题: {v.Scripture} (Book={v.Book}, Chapter={v.Chapter})");
                //    }
                //    else
                //    {
                //        System.Diagnostics.Debug.WriteLine($"  [{i}] 经文: {v.Book}章{v.Chapter}:{v.Verse}节");
                //    }
                //}
                //#endif
                
                bool alreadyExists = _mergedVerses.Any(v => 
                    v.Verse == 0 && 
                    v.Book == item.BookId && 
                    v.Chapter == item.Chapter &&
                    v.Scripture == titleText);
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[增量添加] 检查结果: alreadyExists={alreadyExists}, 查找条件: Verse=0, Book={item.BookId}, Chapter={item.Chapter}, Scripture={titleText}");
                //#endif
                
                if (alreadyExists)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [增量添加] 记录已存在，跳过插入: {titleText}");
                    //#endif
                    return;
                }
                
                // 找到插入位置（根据槽位顺序）
                // 应该根据 _mergedVerses 中实际已存在的锁定记录来计算插入位置
                var lockedItems = _historySlots
                    .Where(x => x.IsLocked && x.BookId > 0)
                    .OrderBy(x => x.Index)
                    .ToList();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[插入位置计算] 锁定记录总数: {lockedItems.Count}, 当前item槽位: {item.Index}");
                //#endif
                
                int insertIndex = 0;
                foreach (var lockedItem in lockedItems)
                {
                    if (lockedItem == item)
                    {
                        // 找到当前item的位置
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"[插入位置计算] 找到当前item，停止计算，insertIndex={insertIndex}");
                        //#endif
                        break;
                    }
                    
                    // 检查 _mergedVerses 中是否已经有该锁定记录
                    var existingBook = BibleBookConfig.GetBook(lockedItem.BookId);
                    string existingVerseText = (lockedItem.StartVerse == lockedItem.EndVerse) ? $"{lockedItem.StartVerse}节" : $"{lockedItem.StartVerse}-{lockedItem.EndVerse}节";
                    string existingTitleText = $"{existingBook?.Name}{lockedItem.Chapter}章{existingVerseText}";
                    
                    bool itemExists = _mergedVerses.Any(v => 
                        v.Verse == 0 && 
                        v.Book == lockedItem.BookId && 
                        v.Chapter == lockedItem.Chapter &&
                        v.Scripture == existingTitleText);
                    
                    if (itemExists)
                    {
                        // 如果已存在，计算该记录的经文总数（标题+经文）
                        int verseCount = 1 + (lockedItem.EndVerse - lockedItem.StartVerse + 1);
                        insertIndex += verseCount;
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"[插入位置计算] 槽位{lockedItem.Index}已存在，增加{verseCount}，insertIndex={insertIndex}");
                        //#endif
                    }
                    // 如果不存在，说明还没有加载，不增加 insertIndex
                }
                
                // 确保 insertIndex 不超过当前列表长度
                if (insertIndex > _mergedVerses.Count)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[插入位置计算] insertIndex({insertIndex})超过列表长度({_mergedVerses.Count})，调整为{_mergedVerses.Count}");
                    //#endif
                    insertIndex = _mergedVerses.Count;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[插入位置计算] 最终结果: 当前列表总数={_mergedVerses.Count}, 计算出的插入位置={insertIndex}, 记录={titleText}");
                //#endif
                
                // 构建要插入的经文列表
                var versesToAdd = new List<BibleVerse>();
                
                // 标题文本已在上面生成，直接使用
                
                // 添加标题
                versesToAdd.Add(new BibleVerse 
                { 
                    Book = item.BookId,
                    Chapter = item.Chapter,
                    Verse = 0,
                    Scripture = titleText
                });
                
                // 加载经文
                for (int verse = item.StartVerse; verse <= item.EndVerse; verse++)
                {
                    var verseData = await _bibleService.GetVerseAsync(item.BookId, item.Chapter, verse);
                    if (verseData != null)
                    {
                        versesToAdd.Add(verseData);
                    }
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 插入 {versesToAdd.Count} 行经文");
                //for (int debugIdx = 0; debugIdx < versesToAdd.Count; debugIdx++)
                //{
                //    var v = versesToAdd[debugIdx];
                //    System.Diagnostics.Debug.WriteLine($"[插入调试] 第{debugIdx}条: Verse={v.Verse}, Scripture={v.Scripture?.Substring(0, Math.Min(30, v.Scripture?.Length ?? 0))}");
                //}
                //#endif
                
                // 逐个插入（ObservableCollection会自动更新UI）
                int insertPos = insertIndex;
                foreach (var verse in versesToAdd)
                {
                    _mergedVerses.Insert(insertPos, verse);
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[插入调试] 插入到位置 {insertPos}, Verse={verse.Verse}, 当前列表总数: {_mergedVerses.Count}");
                    //#endif
                    insertPos++;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[增量添加] 插入完成，最终列表共 {_mergedVerses.Count} 条记录");
                //for (int i = 0; i < _mergedVerses.Count; i++)
                //{
                //    var v = _mergedVerses[i];
                //    if (v.Verse == 0)
                //    {
                //        System.Diagnostics.Debug.WriteLine($"  [{i}] 标题: {v.Scripture}");
                //    }
                //    else
                //    {
                //        System.Diagnostics.Debug.WriteLine($"  [{i}] 经文: {v.Book}章{v.Chapter}:{v.Verse}节");
                //    }
                //}
                //#endif
                
                // 更新主屏幕标题（有锁定记录时隐藏章节标题Border）
                BibleChapterTitle.Text = "";
                BibleChapterTitleBorder.Visibility = System.Windows.Visibility.Collapsed;
                
                // 应用样式（使用Render优先级确保容器已生成）
                await Dispatcher.InvokeAsync(() => 
                {
                    ApplyVerseStyles();
                }, System.Windows.Threading.DispatcherPriority.Render);
                
                // 再次应用样式（确保所有容器都已生成）
                await Dispatcher.InvokeAsync(() => 
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[二次样式应用] 再次应用样式确保完整");
                    //#endif
                    ApplyVerseStyles();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 添加锁定记录失败: {ex.Message}");
                //#endif
            }
        }
        
        /// <summary>
        /// 删除锁定记录的经文
        /// </summary>
        private void RemoveLockedRecordVerses(BibleHistoryItem item)
        {
            try
            {
                if (_mergedVerses.Count == 0)
                    return;
                
                // 找到该记录的标题行
                var titleVerse = _mergedVerses.FirstOrDefault(v => 
                    v.Verse == 0 && 
                    v.Book == item.BookId && 
                    v.Chapter == item.Chapter &&
                    v.Scripture == item.DisplayText);
                
                if (titleVerse == null)
                    return;
                
                int titleIndex = _mergedVerses.IndexOf(titleVerse);
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 找到标题行索引: {titleIndex}");
                //#endif
                
                // 删除标题
                _mergedVerses.RemoveAt(titleIndex);
                
                // 删除后续的经文（直到遇到下一个标题或列表结束）
                while (titleIndex < _mergedVerses.Count && _mergedVerses[titleIndex].Verse != 0)
                {
                    _mergedVerses.RemoveAt(titleIndex);
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 删除完成，剩余 {_mergedVerses.Count} 行");
                //#endif
                
                // 如果没有锁定记录了，清空标题并显示章节标题Border
                if (!_historySlots.Any(x => x.IsLocked))
                {
                    BibleChapterTitle.Text = "";
                    BibleChapterTitleBorder.Visibility = System.Windows.Visibility.Visible;
                }
            }
            catch (Exception)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 删除锁定记录失败: {ex.Message}");
                //#endif
            }
        }
        
        /// <summary>
        /// 从合并的经文列表更新投影
        /// </summary>
        private async Task UpdateProjectionFromMergedVerses()
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($" [更新合并投影] ========== 开始 ==========");
            //#endif

            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
                var verseList = _mergedVerses.ToList();

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   合并经文数量: {verseList.Count}");
                //#endif

                if (verseList.Count > 0 && BibleVerseScrollViewer != null)
                {
                    // 使用 VisualBrush 投影（100%像素级一致）
                    _projectionManager.UpdateBibleProjectionWithVisualBrush(BibleVerseScrollViewer);
                }
                else
                {
                    _projectionManager.ClearProjectionDisplay();
                }
            }

            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($" [更新合并投影] ========== 结束 ==========\n");
            //#endif

            await Task.CompletedTask;
        }
        
        /// <summary>
        /// 更新清空按钮样式（根据锁定状态）
        /// </summary>
        private void UpdateClearButtonStyle()
        {
            if (BtnHistoryClearSelected == null) return;
            
            var hasLocked = _historySlots.Any(x => x.IsLocked);
            
            if (hasLocked)
            {
                // 有锁定记录：绿色按钮
                BtnHistoryClearSelected.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 255, 0));
            }
            else
            {
                // 无锁定记录：恢复白色（MenuButtonStyle的默认颜色）
                BtnHistoryClearSelected.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 255, 255));
            }
        }

        #endregion

    }
}

