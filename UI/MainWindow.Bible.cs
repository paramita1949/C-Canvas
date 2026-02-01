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
    /// MainWindow 圣经功能扩展
    /// </summary>
    public partial class MainWindow
    {
        #region 圣经功能字段

        private IBibleService _bibleService;
        private int _currentBook = 1;      // 当前书卷（默认创世记）
        private int _currentChapter = 1;    // 当前章节
        private int _currentVerse = 1;      // 当前节号
        private bool _isBibleMode = false;  // 是否处于圣经模式
        private bool _bibleNavigationInitialized = false;  // 圣经导航是否已初始化（用于保留用户选择状态）
        private ObservableCollection<BibleHistoryItem> _historySlots = new ObservableCollection<BibleHistoryItem>(); // 20个历史槽位
        private ObservableCollection<BibleVerse> _mergedVerses = new ObservableCollection<BibleVerse>(); // 合并后的经文列表
        
        // 双击检测
        private DateTime _lastHistoryClickTime = DateTime.MinValue;
        private BibleHistoryItem _lastHistoryClickedItem = null;
        private const int DoubleClickInterval = 300; // 毫秒
        
        // 拼音快速定位功能
        private ImageColorChanger.Services.BiblePinyinService _pinyinService;
        private ImageColorChanger.Services.BiblePinyinInputManager _pinyinInputManager;
        
        // 🆕 导航栏同步标志（防止同步时触发不必要的事件）
        private bool _isNavigationSyncing = false;
        
        // 圣经样式设置 Popup（复用实例）
        private BibleInsertStylePopup _bibleStylePopup = null;
        
        // 圣经设置窗口（复用实例）
        private BibleSettingsWindow _bibleSettingsWindow = null;
        
        /// <summary>
        /// 拼音输入是否激活（供主窗口ESC键判断使用）
        /// </summary>
        public bool IsPinyinInputActive => _pinyinInputManager?.IsActive ?? false;
        
        /// <summary>
        /// 处理拼音输入的ESC键（供全局热键调用）
        /// </summary>
        public async System.Threading.Tasks.Task ProcessPinyinEscapeKeyAsync()
        {
            if (_pinyinInputManager != null && _pinyinInputManager.IsActive)
            {
                await _pinyinInputManager.ProcessKeyAsync(Key.Escape);
            }
        }

        #endregion

        #region 圣经数据模型

        /// <summary>
        /// 圣经历史记录项（槽位）
        /// </summary>
        public class BibleHistoryItem : INotifyPropertyChanged
        {
            public int Index { get; set; }              // 槽位序号 (1-10)
            public string DisplayText { get; set; }     // 显示文本（如"创世记1章1-31节"）
            public int BookId { get; set; }             // 书卷ID
            public int Chapter { get; set; }            // 章
            public int StartVerse { get; set; }         // 起始节
            public int EndVerse { get; set; }           // 结束节
            
            private bool _isChecked;
            public bool IsChecked 
            { 
                get => _isChecked;
                set
                {
                    if (_isChecked != value)
                    {
                        _isChecked = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                    }
                }
            }
            
            private bool _isLocked;
            public bool IsLocked 
            { 
                get => _isLocked;
                set
                {
                    if (_isLocked != value)
                    {
                        _isLocked = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocked)));
                        
                        // 通知主窗口更新清空按钮样式
                        OnLockedStateChanged?.Invoke();
                    }
                }
            }
            
            // 静态事件，用于通知锁定状态改变
            public static event Action OnLockedStateChanged;

            public event PropertyChangedEventHandler PropertyChanged;
        }

        /// <summary>
        /// 圣经导航树节点（支持层级展开）
        /// </summary>
        public class BibleNavigationNode : INotifyPropertyChanged
        {
            public string DisplayText { get; set; }                           // 显示文本
            public BibleNodeType NodeType { get; set; }                       // 节点类型
            public string CategoryName { get; set; }                          // 分类名（如"摩西五经"）
            public int BookId { get; set; }                                   // 书卷ID（书卷/章节点有效）
            public int Chapter { get; set; }                                  // 章号（章节点有效）
            public ObservableCollection<BibleNavigationNode> Children { get; set; }  // 子节点
            
            private bool _isExpanded;
            public bool IsExpanded
            {
                get => _isExpanded;
                set
                {
                    if (_isExpanded != value)
                    {
                        _isExpanded = value;
                        OnPropertyChanged(nameof(IsExpanded));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// 圣经导航节点类型
        /// </summary>
        public enum BibleNodeType
        {
            Category,   // 分类（旧约、新约、摩西五经等）
            Book,       // 书卷（创世记、出埃及记等）
            Chapter     // 章（第1章、第2章等）
        }

        #endregion

        #region 圣经服务初始化

        /// <summary>
        /// 初始化圣经服务
        /// </summary>
        private void InitializeBibleService()
        {
            try
            {
                // 🔧 重要：手动创建 BibleService，使用主窗口的 _configManager 实例
                // 这样确保配置修改能立即生效
                var cache = App.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                _bibleService = new Services.Implementations.BibleService(cache, _configManager);

                //#if DEBUG
                //Debug.WriteLine("[圣经] 服务初始化成功");
                //#endif

                // 检查数据库是否可用
                Task.Run(async () =>
                {
                    var available = await _bibleService.IsDatabaseAvailableAsync();

                    //#if DEBUG
                    //Debug.WriteLine($"[圣经] 数据库可用: {available}");
                    //#endif

                    if (!available)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            WpfMessageBox.Show(
                                "圣经数据库文件未找到！\n请确保 bible.db 文件位于 data/assets/ 目录下。",
                                "错误",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经] 服务初始化失败: {ex.Message}");
                //#endif

                WpfMessageBox.Show(
                    $"圣经功能初始化失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region 圣经视图切换

        /// <summary>
        /// 重置圣经导航状态（清空所有选择和经文显示）
        /// </summary>
        public void ResetBibleNavigation()
        {
            _bibleNavigationInitialized = false;
            
            // 清空所有下拉框选择
            BibleCategoryList.SelectedIndex = -1;
            BibleBookList.ItemsSource = null;
            BibleChapterList.ItemsSource = null;
            BibleStartVerse.ItemsSource = null;
            BibleEndVerse.ItemsSource = null;
            
            // 清空经文显示
            _mergedVerses.Clear();
            BibleChapterTitle.Text = "";
            
            //#if DEBUG
            //Debug.WriteLine("[圣经] 导航状态已重置");
            //#endif
        }

        /// <summary>
        /// 圣经按钮点击事件
        /// </summary>
        private async void BtnShowBible_Click(object sender, RoutedEventArgs e)
        {
            //#if DEBUG
            //Debug.WriteLine($"[圣经] 圣经按钮被点击");
            //Debug.WriteLine($"   TextEditorPanel 可见性: {TextEditorPanel.Visibility}");
            //Debug.WriteLine($"   当前视图模式: {_currentViewMode}");
            //#endif

            // 🆕 如果在幻灯片编辑模式下，只切换左侧导航面板
            if (TextEditorPanel.Visibility == Visibility.Visible)
            {
                //#if DEBUG
                //Debug.WriteLine($"✅ [圣经] 在幻灯片编辑模式下，切换左侧导航面板");
                //#endif
                
                // 切换左侧导航面板：ProjectTree <-> BibleNavigationPanel
                if (BibleNavigationPanel.Visibility == Visibility.Visible)
                {
                    // 当前显示圣经，切换回项目树
                    BibleNavigationPanel.Visibility = Visibility.Collapsed;
                    ProjectTree.Visibility = Visibility.Visible;
                    _currentViewMode = NavigationViewMode.Projects;
                    _isBibleMode = false;  // 🔧 退出圣经模式
                    
                    //#if DEBUG
                    //Debug.WriteLine($"✅ [圣经] 切换到项目树");
                    //#endif
                }
                else
                {
                    // 当前显示项目树，切换到圣经
                    ProjectTree.Visibility = Visibility.Collapsed;
                    BibleNavigationPanel.Visibility = Visibility.Visible;
                    _currentViewMode = NavigationViewMode.Bible;
                    _isBibleMode = true;  // 🔧 进入圣经模式（关键修复！）
                    
                    // 如果还未初始化，则初始化
                    if (!_bibleNavigationInitialized)
                    {
                        await LoadBibleNavigationDataAsync();
                    }
                    
                    //#if DEBUG
                    //Debug.WriteLine($"✅ [圣经] 切换到圣经导航");
                    //#endif
                }
                
                // 更新按钮状态
                UpdateViewModeButtons();
                
                return;
            }

            // 否则，切换到完整的圣经页面
            //#if DEBUG
            //Debug.WriteLine($"[圣经] 切换到完整圣经页面");
            //#endif

            _isBibleMode = true;
            _currentViewMode = NavigationViewMode.Bible;  // 设置当前视图模式为圣经

            // 清空图片显示（包括合成播放按钮）
            ClearImageDisplay();
            
            // 🆕 更新合成播放按钮显示状态（隐藏按钮）
            UpdateFloatingCompositePlayButton();

            // 隐藏ProjectTree，显示圣经导航面板
            ProjectTree.Visibility = Visibility.Collapsed;
            BibleNavigationPanel.Visibility = Visibility.Visible;

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 导航切换完成, ProjectTree={ProjectTree.Visibility}, BiblePanel={BibleNavigationPanel.Visibility}");
            //
            ////// 🔍 打印导航栏宽度信息（异步调试输出，不需要等待）
            ////_ = Dispatcher.InvokeAsync(() =>
            ////{
            ////    if (NavigationPanelColumn != null)
            ////    {
            ////        Debug.WriteLine($"");
            ////        Debug.WriteLine($"🔍 ===== 圣经导航栏宽度信息 =====");
            ////        Debug.WriteLine($"📐 [导航栏] 设定宽度: {NavigationPanelColumn.Width}");
            ////        Debug.WriteLine($"📐 [导航栏] 实际宽度: {NavigationPanelColumn.ActualWidth:F2}");
            ////    }
            ////    
            ////    if (BibleNavigationPanel != null)
            ////    {
            ////        Debug.WriteLine($"📐 [圣经面板] 实际宽度: {BibleNavigationPanel.ActualWidth:F2}");
            ////    }
            ////    
            ////    // 打印5列的宽度设置
            ////    Debug.WriteLine($"📊 [表格列宽] 第1列(分类): 70");
            ////    Debug.WriteLine($"📊 [表格列宽] 第2列(书卷): 120");
            ////    Debug.WriteLine($"📊 [表格列宽] 第3列(章): 60");
            ////    Debug.WriteLine($"📊 [表格列宽] 第4列(起始节): 60");
            ////    Debug.WriteLine($"📊 [表格列宽] 第5列(结束节): 60");
            ////    Debug.WriteLine($"📊 [表格列宽] 总计: 370");
            ////    Debug.WriteLine($"⚠️  [结论] 导航栏宽度需要390以上才能完整显示5列！");
            ////    Debug.WriteLine($"");
            ////}, System.Windows.Threading.DispatcherPriority.Loaded);
            //#endif

            // 加载圣经数据
            await LoadBibleNavigationDataAsync();
            
            // 🔧 如果启用了保存历史记录，且有勾选或锁定的槽位，自动加载经文
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🔍 [启动加载] 检查自动加载条件:");
            //System.Diagnostics.Debug.WriteLine($"   SaveBibleHistory: {_configManager.SaveBibleHistory}");
            //System.Diagnostics.Debug.WriteLine($"   _historySlots != null: {_historySlots != null}");
            //System.Diagnostics.Debug.WriteLine($"   _historySlots.Count: {_historySlots?.Count ?? 0}");
            //#endif
            
            if (_configManager.SaveBibleHistory && _historySlots != null && _historySlots.Count > 0)
            {
                // 检查是否有锁定的记录
                var lockedSlots = _historySlots.Where(s => s.IsLocked && s.BookId > 0).ToList();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   锁定记录数: {lockedSlots.Count}");
                //#endif
                
                if (lockedSlots.Count > 0)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📖 [启动加载] 发现 {lockedSlots.Count} 个锁定记录，加载到主屏幕");
                    //#endif
                    
                    foreach (var lockedSlot in lockedSlots)
                    {
                        await AddLockedRecordVerses(lockedSlot);
                    }
                }
                else
                {
                    // 没有锁定记录，检查是否有勾选的槽位
                    //#if DEBUG
                    //var allCheckedSlots = _historySlots.Where(s => s.IsChecked).ToList();
                    //System.Diagnostics.Debug.WriteLine($"   所有勾选的槽位数: {allCheckedSlots.Count}");
                    //foreach (var cs in allCheckedSlots)
                    //{
                    //    System.Diagnostics.Debug.WriteLine($"      - 槽位{cs.Index}: BookId={cs.BookId}, DisplayText={cs.DisplayText}");
                    //}
                    //#endif
                    
                    var checkedSlot = _historySlots.FirstOrDefault(s => s.IsChecked && s.BookId > 0);
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   勾选且有内容的槽位: {(checkedSlot != null ? $"槽位{checkedSlot.Index}" : "无")}");
                    //if (checkedSlot != null)
                    //{
                    //    System.Diagnostics.Debug.WriteLine($"   槽位详情: BookId={checkedSlot.BookId}, Chapter={checkedSlot.Chapter}, DisplayText={checkedSlot.DisplayText}");
                    //}
                    //#endif
                    
                    if (checkedSlot != null)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📖 [启动加载] 自动加载勾选的槽位{checkedSlot.Index}的经文: {checkedSlot.DisplayText}");
                        //#endif
                        
                        await LoadVerseRangeAsync(checkedSlot.BookId, checkedSlot.Chapter, checkedSlot.StartVerse, checkedSlot.EndVerse);
                    }
                }
            }
            else
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   ⚠️ 不满足自动加载条件，跳过");
                //#endif
            }
            
            // 初始化拼音快速定位服务
            InitializePinyinService();
            
            // 💾 加载滚动节数设置
            LoadBibleScrollVerseCountSetting();
            
            // 🆕 更新译本选择按钮状态
            UpdateBibleVersionRadioButtons();
            
            // 显示圣经视图区域，隐藏其他区域
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            VideoContainer.Visibility = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Collapsed;
            BibleDisplayContainer.Visibility = Visibility.Visible;
            
            // 🔧 确保 BibleVerseScrollViewer 可见（关键修复！）
            BibleVerseScrollViewer.Visibility = Visibility.Visible;
            
            // 🔧 如果有经文内容，确保列表可见
            if (BibleVerseList.ItemsSource != null && 
                BibleVerseList.ItemsSource is System.Collections.IEnumerable enumerable &&
                enumerable.Cast<object>().Any())
            {
                BibleVerseList.Visibility = Visibility.Visible;
                
                //#if DEBUG
                //Debug.WriteLine($"[圣经] 恢复经文显示，共 {BibleVerseList.Items.Count} 项");
                //#endif
            }

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 圣经视图已显示, ImageScroll={ImageScrollViewer.Visibility}, BibleVerse={BibleVerseScrollViewer.Visibility}");
            //#endif

            // 应用圣经设置
            ApplyBibleSettings();

            // 更新按钮状态
            UpdateViewModeButtons();
        }


        #endregion

        #region 圣经项目树交互

        /// <summary>
        /// 处理圣经项目树节点点击
        /// （在MainWindow.ProjectTree.cs的ProjectTree_MouseClick中调用）
        /// </summary>
        public async Task HandleBibleNodeClickAsync(ProjectTreeItem node)
        {
            if (node == null || !_isBibleMode)
                return;

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 节点点击: {node.Name}, Type={node.Type}, Tag={node.Tag}");
            //#endif

            // 根据节点类型和标签解析书卷和章节
            if (node.Type == TreeItemType.BibleChapter && node.Tag is string tag)
            {
                var parts = tag.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int bookId) && int.TryParse(parts[1], out int chapter))
                {
                    // 章节节点：加载整章经文
                    await LoadChapterVersesAsync(bookId, chapter);
                }
            }
            else if (node.Type == TreeItemType.BibleBook && node.Tag is string bookTag && int.TryParse(bookTag, out int bookIdOnly))
            {
                // 书卷节点：显示第一章
                await LoadChapterVersesAsync(bookIdOnly, 1);
            }
        }

        #endregion

        #region 圣经导航数据加载

        /// <summary>
        /// 加载圣经导航数据（历史记录 + 经文表格）
        /// 🔧 优化：只在首次加载时初始化，后续切换回来时保留用户选择状态
        /// </summary>
        private Task LoadBibleNavigationDataAsync()
        {
            try
            {
                //#if DEBUG
                //var sw = Stopwatch.StartNew();
                //#endif

                // 🔧 只在首次加载时初始化
                if (!_bibleNavigationInitialized)
                {
                    // 初始化20个历史槽位
                    InitializeHistorySlots();
                    BibleHistoryList.ItemsSource = _historySlots;
                    
                    // 订阅锁定状态变化事件
                    BibleHistoryItem.OnLockedStateChanged += UpdateClearButtonStyle;

                    // 从数据库加载历史记录（如果启用了保存功能）
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[圣经初始化] 检查是否需要加载历史记录");
                    //System.Diagnostics.Debug.WriteLine($"   SaveBibleHistory配置: {_configManager.SaveBibleHistory}");
                    //#endif
                    
                    if (_configManager.SaveBibleHistory)
                    {
                        LoadBibleHistoryFromConfig();
                    }

                    // 加载第1列:分类列表(用户要求的10个准确分类)
                    var categories = new ObservableCollection<string>
                    {
                        "旧约",          // 旧约全部39卷
                        "新约",          // 新约全部27卷
                        "摩西五经",      // 创-申 (5卷)
                        "旧约历史",      // 书-斯 (12卷)
                        "诗歌智慧",      // 伯-歌 (5卷)
                        "大先知书",      // 赛-但 (5卷)
                        "小先知书",      // 何-玛 (12卷)
                        "福音使徒",      // 太-徒 (5卷:四福音+使徒行传)
                        "保罗书信",      // 罗-门 (13卷)
                        "普通书信"       // 来-启 (9卷:8封普通书信+启示录)
                    };

                    BibleCategoryList.ItemsSource = categories;

                    // 默认选中"旧约"
                    BibleCategoryList.SelectedIndex = 0;
                    
                    // 标记已初始化
                    _bibleNavigationInitialized = true;

                    //#if DEBUG
                    //sw.Stop();
                    //Debug.WriteLine($"[圣经] 导航数据首次加载完成: {sw.ElapsedMilliseconds}ms, 分类数: {categories.Count}");
                    //#endif
                }
                else
                {
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经] 导航数据已初始化，保留用户选择状态");
                    //#endif
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经] 加载导航数据失败: {ex.Message}");
                //#endif

                WpfMessageBox.Show(
                    $"加载圣经导航失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return Task.CompletedTask;
            }
        }

        #endregion

        #region 圣经经文加载

        /// <summary>
        /// 加载整章经文
        /// </summary>
        private async Task LoadChapterVersesAsync(int book, int chapter)
        {
            try
            {
                _currentBook = book;
                _currentChapter = chapter;
                _currentVerse = 1;

                var verses = await _bibleService.GetChapterVersesAsync(book, chapter);
                var bookInfo = BibleBookConfig.GetBook(book);
                
                // ========================================
                // 📌 统一数据源方案：始终更新 _mergedVerses
                // ========================================
                // 确保绑定
                if (BibleVerseList.ItemsSource != _mergedVerses)
                {
                    BibleVerseList.ItemsSource = _mergedVerses;
                }
                
                // 检查是否有锁定记录
                bool hasLockedRecords = _historySlots.Any(x => x.IsLocked);
                if (hasLockedRecords)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[圣经] 锁定模式：忽略加载章节请求（{bookInfo?.Name}{chapter}章）");
                    //#endif
                    return; // 锁定模式下，不允许通过导航加载新内容
                }
                
                // 🔧 获取该章总节数，显示完整范围（如"创世记3章1-24节"）
                int verseCount = await _bibleService.GetVerseCountAsync(book, chapter);
                
                // 🆕 非锁定模式：同步导航栏状态
                SyncNavigationToRecord(book, chapter, 1, verseCount > 0 ? verseCount : 1);
                
                // 📌 非锁定模式：清空并添加新经文到 _mergedVerses
                string verseText = (verseCount > 1) ? $"1-{verseCount}节" : "1节";
                BibleChapterTitle.Text = $"{bookInfo?.Name}{chapter}章{verseText}";
                BibleChapterTitleBorder.Visibility = Visibility.Visible;
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📌 [非锁定模式] 加载章节: {verses.Count}条经文，{bookInfo?.Name}{chapter}章");
                //#endif
                
                // 清空并添加新数据
                _mergedVerses.Clear();
                foreach (var verse in verses)
                {
                    _mergedVerses.Add(verse);
                }
                
                // 重置滚动条到顶部
                BibleVerseScrollViewer.ScrollToTop();

                // 延迟应用样式
                _ = Dispatcher.InvokeAsync(() =>
                {
                    ApplyBibleSettings();
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                //#if DEBUG
                //sw.Stop();
                //Debug.WriteLine($"[圣经] 加载章节 {book}:{chapter}, 耗时: {sw.ElapsedMilliseconds}ms, 经文数: {verses.Count}");
                //#endif
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经] 加载章节失败: {ex.Message}");
                //#endif

                WpfMessageBox.Show(
                    $"加载经文失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        // 第1列:分类选择事件
        private void BibleCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BibleCategoryList.SelectedItem is not string category)
            {
                BibleBookList.ItemsSource = null;
                BibleChapterList.ItemsSource = null;
                BibleStartVerse.ItemsSource = null;
                BibleEndVerse.ItemsSource = null;
                return;
            }

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 选中分类: {category}");
            //#endif

            // 根据分类加载书卷到第2列
            var allBooks = BibleBookConfig.GetOldTestament().Concat(BibleBookConfig.GetNewTestament());
            IEnumerable<BibleBook> books;

            switch (category)
            {
                case "旧约":
                    books = allBooks.Where(b => b.Testament == "旧约");
                    break;
                case "新约":
                    books = allBooks.Where(b => b.Testament == "新约");
                    break;
                case "旧约历史":
                    books = allBooks.Where(b => b.Category == "历史书" && b.Testament == "旧约");
                    break;
                case "诗歌智慧":
                    books = allBooks.Where(b => b.Category == "诗歌智慧书");
                    break;
                case "福音使徒":
                    // 四福音书 + 使徒行传
                    books = allBooks.Where(b => b.Category == "福音书" || 
                                               (b.Name == "使徒行传" && b.Testament == "新约"));
                    break;
                case "普通书信":
                    // 普通书信 + 启示录
                    books = allBooks.Where(b => b.Category == "普通书信" || b.Name == "启示录");
                    break;
                default:
                    // 摩西五经、大先知书、小先知书、保罗书信直接匹配
                    books = allBooks.Where(b => b.Category == category);
                    break;
            }

            var bookList = books.OrderBy(b => b.BookId).ToList();
            BibleBookList.ItemsSource = bookList;
            
            // 清空书卷、章节和节号选择
            BibleBookList.SelectedIndex = -1;
            BibleChapterList.ItemsSource = null;
            BibleStartVerse.ItemsSource = null;
            BibleEndVerse.ItemsSource = null;

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 加载了 {bookList.Count} 卷书，已清空选择");
            //#endif
        }

        // 第2列:书卷选择事件
        private void BibleBook_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BibleBookList.SelectedItem is not BibleBook book)
            {
                BibleChapterList.ItemsSource = null;
                BibleStartVerse.ItemsSource = null;
                BibleEndVerse.ItemsSource = null;
                return;
            }

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 选中书卷: {book.Name} (BookId={book.BookId})");
            //#endif

            // 生成章列表到第3列
            var chapters = Enumerable.Range(1, book.ChapterCount).Select(c => $"{c}").ToList();
            BibleChapterList.ItemsSource = chapters;
            BibleChapterList.Tag = book.BookId; // 保存BookId供后续使用
            
            // 清空章节选择和起始/结束节列表
            BibleChapterList.SelectedIndex = -1;
            BibleStartVerse.ItemsSource = null;
            BibleEndVerse.ItemsSource = null;

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 加载了 {chapters.Count} 章，已清空章节和节号选择");
            //#endif
        }

        // 第3列:章选择事件（单击只加载节号列表，不显示经文）
        private async void BibleChapter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 🔧 如果正在同步导航栏，跳过事件处理
            if (_isNavigationSyncing) return;
            
            if (BibleChapterList.SelectedItem is not string chapterStr)
                return;

            if (!int.TryParse(chapterStr, out int chapter))
                return;

            if (BibleChapterList.Tag is not int bookId)
                return;

            //#if DEBUG
            //Debug.WriteLine($"[圣经-节数获取] 选中章: BookId={bookId}, Chapter={chapter}");
            //#endif

            // 🔧 BUG修复：获取原始节数（包含"-"节），而不是处理后的列表长度
            // 原因：GetChapterVersesAsync会合并"-"节，导致返回的列表长度小于实际节数
            // 例如：约书亚记3章有17节（包含"-"节），但处理后只有16个元素
            int verseCount = await _bibleService.GetVerseCountAsync(bookId, chapter);
            
            //#if DEBUG
            //var verses = await _bibleService.GetChapterVersesAsync(bookId, chapter);
            //int processedCount = verses?.Count ?? 0;
            //Debug.WriteLine($"[圣经-节数获取] 原始节数: {verseCount}, 处理后列表长度: {processedCount}");
            //if (verseCount != processedCount)
            //{
            //    Debug.WriteLine($"[圣经-节数获取] ⚠️ 检测到节数差异！该章可能包含精简的'-'节");
            //}
            //#endif
            
            if (verseCount > 0)
            {
                // 生成节号列表 1, 2, 3, ... verseCount（使用原始节数）
                var verseNumbers = Enumerable.Range(1, verseCount).Select(v => v.ToString()).ToList();
                
                BibleStartVerse.ItemsSource = verseNumbers;
                BibleEndVerse.ItemsSource = verseNumbers;
                
                // 清空起始节和结束节选择，要求用户手动选择
                BibleStartVerse.SelectedIndex = -1;
                BibleEndVerse.SelectedIndex = -1;
                
                // ========================================
                // 📌 统一数据源方案：不需要手动清空
                // ========================================
                // 主屏幕始终绑定到 _mergedVerses，由加载函数负责清空和添加

                //#if DEBUG
                //Debug.WriteLine($"[圣经-节数获取] 已加载节号列表 1-{verseCount}，等待用户选择节范围");
                //#endif
            }
        }
        
        // 第3列:章双击事件（双击加载整章经文）
        private async void BibleChapter_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BibleChapterList.SelectedItem is not string chapterStr)
                return;

            if (!int.TryParse(chapterStr, out int chapter))
                return;

            if (BibleChapterList.Tag is not int bookId)
                return;

            // 🔧 优化：检查是否已经加载了该章的完整内容，避免重复刷新
            int verseCount = await _bibleService.GetVerseCountAsync(bookId, chapter);
            
            // 检查当前是否已经是这一章的完整范围（1到最后一节）
            bool isAlreadyFullChapter = 
                _currentBook == bookId && 
                _currentChapter == chapter &&
                BibleStartVerse.SelectedIndex == 0 && 
                BibleEndVerse.SelectedIndex == verseCount - 1 &&
                BibleStartVerse.Items.Count == verseCount;

            if (isAlreadyFullChapter)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 已显示该章完整内容，跳过重复加载: BookId={bookId}, Chapter={chapter}");
                //#endif
                // 只更新历史槽位，不刷新屏幕
                AddToHistory(bookId, chapter, 1, verseCount);
                return;
            }

            //#if DEBUG
            //Debug.WriteLine($"[圣经-节数获取] 双击章: BookId={bookId}, Chapter={chapter}，加载整章");
            //#endif
            
            if (verseCount > 0)
            {
                // 生成节号列表 1, 2, 3, ... verseCount（使用原始节数）
                var verseNumbers = Enumerable.Range(1, verseCount).Select(v => v.ToString()).ToList();
                
                BibleStartVerse.ItemsSource = verseNumbers;
                BibleEndVerse.ItemsSource = verseNumbers;
                
                // 默认选中第1节和最后一节
                BibleStartVerse.SelectedIndex = 0;
                BibleEndVerse.SelectedIndex = verseCount - 1;

                //#if DEBUG
                //Debug.WriteLine($"[圣经-节数获取] 双击加载整章，节范围: 1-{verseCount}");
                //#endif
                
                // 🆕 如果在幻灯片编辑模式，双击章节自动插入整章经文
                if (TextEditorPanel.Visibility == Visibility.Visible)
                {
                    #if DEBUG
                    Debug.WriteLine($"✅ [圣经双击] 双击章节，自动插入整章: BookId={bookId}, Chapter={chapter}, 节范围: 1-{verseCount}");
                    #endif
                    
                    await CreateBibleTextElements(bookId, chapter, 1, verseCount);
                }
                else
                {
                    // 🔧 非编辑模式：加载经文到投影区（与双击开始节行为一致）
                    await LoadVerseRangeAsync(bookId, chapter, 1, verseCount);
                }
                
                // 🔧 更新历史槽位
                AddToHistory(bookId, chapter, 1, verseCount);
            }
        }

        // 第4列:起始节选择事件（单击只选中，不加载经文）
        private void BibleStartVerse_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BibleStartVerse.SelectedItem == null)
                return;

            if (!int.TryParse(BibleStartVerse.SelectedItem.ToString(), out int startVerse))
                return;

            // 🔧 优化：自动滚动结束节列表到起始节位置，并自动选中结束节为起始节
            // 但是不加载经文，要等用户选择结束节（或双击开始节）才加载
            ScrollAndSelectEndVerseWithoutLoad(startVerse);
        }

        // 第4列:起始节双击事件（双击代表只要这一节，立即加载经文）
        private async void BibleStartVerse_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BibleStartVerse.SelectedItem == null)
                return;

            if (!int.TryParse(BibleStartVerse.SelectedItem.ToString(), out int startVerse))
                return;

            if (BibleChapterList.Tag is not int bookId)
                return;

            if (BibleChapterList.SelectedItem is not string chapterStr)
                return;

            if (!int.TryParse(chapterStr, out int chapter))
                return;

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 双击起始节: BookId={bookId}, Chapter={chapter}, Verse={startVerse}");
            //#endif

            // 🔧 双击起始节：将结束节也设置为起始节（表示只要一节）
            // 先设置结束节选择（不触发加载，避免重复）
            if (BibleEndVerse.Items.Count > 0 && startVerse > 0 && startVerse <= BibleEndVerse.Items.Count)
            {
                int targetIndex = startVerse - 1;
                BibleEndVerse.SelectionChanged -= BibleEndVerse_SelectionChanged;
                BibleEndVerse.SelectedIndex = targetIndex;
                BibleEndVerse.SelectionChanged += BibleEndVerse_SelectionChanged;
            }
            
            // 🆕 如果在幻灯片编辑模式，双击起始节自动插入该节经文
            if (TextEditorPanel.Visibility == Visibility.Visible)
            {
                //#if DEBUG
                //Debug.WriteLine($"✅ [圣经双击] 双击起始节，自动插入单节: BookId={bookId}, Chapter={chapter}, Verse={startVerse}");
                //#endif
                
                await CreateBibleTextElements(bookId, chapter, startVerse, startVerse);
            }
            else
            {
                // 非编辑模式：直接加载这一节经文到投影区
                await LoadVerseRangeAsync(bookId, chapter, startVerse, startVerse);
            }
            
            // 添加到历史记录
            AddToHistory(bookId, chapter, startVerse, startVerse);

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 双击加载完成: {startVerse}节");
            //#endif
        }

        /// <summary>
        /// 滚动结束节列表到指定节号，并自动选中该节（但不触发加载）
        /// </summary>
        private void ScrollAndSelectEndVerseWithoutLoad(int verseNumber)
        {
            if (BibleEndVerse.Items.Count == 0 || verseNumber <= 0 || verseNumber > BibleEndVerse.Items.Count)
                return;

            // verseNumber是从1开始的，所以索引是verseNumber-1
            int targetIndex = verseNumber - 1;

            // 🔧 临时取消事件处理，避免触发加载
            BibleEndVerse.SelectionChanged -= BibleEndVerse_SelectionChanged;
            BibleEndVerse.SelectedIndex = targetIndex;
            BibleEndVerse.SelectionChanged += BibleEndVerse_SelectionChanged;
            
            // 🔧 延迟滚动：使用LineUp/LineDown的方式精确滚动
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经滚动] 开始滚动：目标节号={verseNumber}, 索引={targetIndex}（不触发加载）");
                    //#endif
                    
                    var scrollViewer = FindVisualChild<ScrollViewer>(BibleEndVerse);
                    if (scrollViewer != null)
                    {
                        // 🔧 方案1：先滚动到顶部，然后使用LineDown精确滚动到目标行
                        scrollViewer.ScrollToTop();
                        
                        // 使用LineDown滚动到目标索引（每次滚动一行）
                        for (int i = 0; i < targetIndex; i++)
                        {
                            scrollViewer.LineDown();
                        }
                        
                        //#if DEBUG
                        //Debug.WriteLine($"[圣经滚动] LineDown完成，最终偏移={scrollViewer.VerticalOffset:F2}");
                        //#endif
                    }
                }
                catch (Exception)
                {
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经滚动] 异常: {ex.Message}");
                    //#endif
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        
        /// <summary>
        /// 滚动结束节列表到指定节号，并自动选中该节（触发加载）
        /// </summary>
        private void ScrollAndSelectEndVerse(int verseNumber)
        {
            if (BibleEndVerse.Items.Count == 0 || verseNumber <= 0 || verseNumber > BibleEndVerse.Items.Count)
                return;

            // verseNumber是从1开始的，所以索引是verseNumber-1
            int targetIndex = verseNumber - 1;

            // 🔧 立即选中（触发加载经文）
            BibleEndVerse.SelectedIndex = targetIndex;
            
            // 🔧 延迟滚动：使用LineUp/LineDown的方式精确滚动
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经滚动] 开始滚动：目标节号={verseNumber}, 索引={targetIndex}");
                    //#endif
                    
                    var scrollViewer = FindVisualChild<ScrollViewer>(BibleEndVerse);
                    if (scrollViewer != null)
                    {
                        // 🔧 方案1：先滚动到顶部，然后使用LineDown精确滚动到目标行
                        scrollViewer.ScrollToTop();
                        
                        // 使用LineDown滚动到目标索引（每次滚动一行）
                        for (int i = 0; i < targetIndex; i++)
                        {
                            scrollViewer.LineDown();
                        }
                        
                        //#if DEBUG
                        //Debug.WriteLine($"[圣经滚动] LineDown完成，最终偏移={scrollViewer.VerticalOffset:F2}");
                        //#endif
                    }
                }
                catch (Exception)
                {
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经滚动] 异常: {ex.Message}");
                    //#endif
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // 第5列:结束节选择事件
        /// <summary>
        /// 结束节选择改变事件（重构版 - 自动创建文本框）
        /// </summary>
        private async void BibleEndVerse_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 🔧 如果正在同步导航栏，跳过事件处理（防止重复加载）
            if (_isNavigationSyncing) return;

            if (BibleStartVerse.SelectedItem == null || BibleEndVerse.SelectedItem == null)
                return;

            if (!int.TryParse(BibleStartVerse.SelectedItem.ToString(), out int startVerse))
                return;

            if (!int.TryParse(BibleEndVerse.SelectedItem.ToString(), out int endVerse))
                return;

            if (BibleChapterList.Tag is not int bookId)
                return;

            if (BibleChapterList.SelectedItem is not string chapterStr)
                return;

            if (!int.TryParse(chapterStr, out int chapter))
                return;

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 结束节改变: {startVerse}-{endVerse}");
            //Debug.WriteLine($"   TextEditorPanel 可见性: {TextEditorPanel.Visibility}");
            //#endif

            // 🆕 如果在幻灯片编辑模式，自动创建文本框元素
            if (TextEditorPanel.Visibility == Visibility.Visible)
            {
                //#if DEBUG
                //Debug.WriteLine($"✅ [圣经] 在幻灯片编辑模式，自动创建文本框元素");
                //#endif

                await CreateBibleTextElements(bookId, chapter, startVerse, endVerse);
                return;
            }

            // 否则，加载到投影记录（圣经浏览模式）
            //#if DEBUG
            //Debug.WriteLine($"✅ [圣经] 在圣经浏览模式，加载到投影记录");
            //#endif

            // 重新加载指定范围的经文
            await LoadVerseRangeAsync(bookId, chapter, startVerse, endVerse);

            // 添加到历史记录
            AddToHistory(bookId, chapter, startVerse, endVerse);
        }


        /// <summary>
        /// 加载指定范围的经文
        /// </summary>
        private async Task LoadVerseRangeAsync(int bookId, int chapter, int startVerse, int endVerse)
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"📖 [加载经文] ========== 开始 ==========");
            //System.Diagnostics.Debug.WriteLine($"   参数: BookId={bookId}, Chapter={chapter}, Verse={startVerse}-{endVerse}");
            //#endif

            try
            {
                _currentBook = bookId;
                _currentChapter = chapter;
                _currentVerse = startVerse;

                // 🔧 使用新的智能方法，自动处理"-"节的情况
                var verses = await _bibleService.GetVerseRangeAsync(bookId, chapter, startVerse, endVerse);

                var book = BibleBookConfig.GetBook(bookId);
                // 🔧 如果开始节和结束节相同，只显示一个节号（如"18节"），否则显示范围（如"18-25节"）
                string verseText = (startVerse == endVerse) ? $"{startVerse}节" : $"{startVerse}-{endVerse}节";

                // ========================================
                // 📌 统一数据源方案：始终更新 _mergedVerses
                // ========================================
                // 确保绑定
                if (BibleVerseList.ItemsSource != _mergedVerses)
                {
                    BibleVerseList.ItemsSource = _mergedVerses;
                }

                // 检查是否有锁定记录
                bool hasLockedRecords = _historySlots.Any(x => x.IsLocked);
                if (hasLockedRecords)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[圣经] 锁定模式：忽略加载经文请求（{book?.Name}{chapter}:{startVerse}-{endVerse}）");
                    //#endif
                    return; // 锁定模式下，不允许通过导航加载新内容
                }

                // 🆕 非锁定模式：同步导航栏状态
                SyncNavigationToRecord(bookId, chapter, startVerse, endVerse);

                // 📌 非锁定模式：清空并添加新经文到 _mergedVerses
                BibleChapterTitle.Text = $"{book?.Name}{chapter}章 {verseText}";
                BibleChapterTitleBorder.Visibility = Visibility.Visible;

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📌 [非锁定模式] 加载经文: {verses.Count}条，{book?.Name}{chapter}:{startVerse}-{endVerse}");
                //#endif

                // 清空并添加新数据
                _mergedVerses.Clear();
                foreach (var verse in verses)
                {
                    _mergedVerses.Add(verse);
                }

                // 重置滚动条到顶部
                BibleVerseScrollViewer.ScrollToTop();

                // 🔧 设置主屏幕底部扩展空间（等于视口高度,支持底部内容向上拉）
                UpdateMainScreenBottomExtension();

                // 🔧 应用样式后再更新投影（确保高度计算完成）
                _ = Dispatcher.InvokeAsync(() =>
                {
                    ApplyBibleSettings();

                    // 🆕 样式应用完成后，更新投影
                    if (_isBibleMode && _projectionManager != null && _projectionManager.IsProjecting)
                    {
                        // 检查是否有锁定记录
                        if (_historySlots.Any(x => x.IsLocked))
                        {
                            // 📌 锁定模式：加载新章节时，不更新投影（保持锁定记录投影）
                        }
                        else
                        {
                            // 📌 非锁定模式：样式应用后更新投影
                            RenderBibleToProjection();
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
//#if DEBUG
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[圣经] 加载经文范围失败: {ex.Message}");
//            }
//#else
            catch (Exception)
            {
            }
//#endif
        }

        /// <summary>
        /// 自动保存到勾选的槽位（只更新勾选的槽位，不创建新记录）
        /// </summary>
        private void AddToHistory(int bookId, int chapter, int startVerse, int endVerse)
        {
            try
            {
                var book = BibleBookConfig.GetBook(bookId);
                // 🔧 如果开始节和结束节相同，只显示一个节号（如"3节"），否则显示范围（如"3-5节"）
                string verseText = (startVerse == endVerse) ? $"{startVerse}节" : $"{startVerse}-{endVerse}节";
                string displayText = $"{book?.Name}{chapter}章{verseText}";

                // 找到所有勾选的槽位
                var checkedSlots = _historySlots.Where(s => s.IsChecked).ToList();

                if (checkedSlots.Count == 0)
                {
                    //#if DEBUG
                    //Debug.WriteLine("[圣经] 没有勾选任何槽位，不保存");
                    //#endif
                    return;
                }

                // 只更新勾选的槽位（可能有多个）
                // ⚠️ 锁定的槽位不允许更新
                foreach (var slot in checkedSlots)
                {
                    // 📌 跳过已锁定的槽位
                    if (slot.IsLocked)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"[圣经] 跳过已锁定的槽位{slot.Index}: {slot.DisplayText}");
                        //#endif
                        continue;
                    }
                    
                    slot.BookId = bookId;
                    slot.Chapter = chapter;
                    slot.StartVerse = startVerse;
                    slot.EndVerse = endVerse;
                    slot.DisplayText = displayText;

                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[圣经] 更新槽位{slot.Index}: {displayText}");
                    //#endif
                }

                // 刷新列表显示
                BibleHistoryList.Items.Refresh();
            }
//#if DEBUG
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[圣经] 保存到历史槽位失败: {ex.Message}");
//            }
//#else
            catch (Exception)
            {
            }
//#endif
        }

        /// <summary>
        /// 🆕 同步导航栏状态到投影记录（非锁定模式）
        /// 功能：根据投影记录自动选择对应的书卷、章节、起始节和结束节，并将书卷滚动到第一位
        /// </summary>
        private void SyncNavigationToRecord(int bookId, int chapter, int startVerse, int endVerse)
        {
            try
            {
                var book = BibleBookConfig.GetBook(bookId);
                if (book == null) return;

                // 🔧 设置同步标志，防止触发选择事件
                _isNavigationSyncing = true;

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经导航同步] 开始同步: {book.Name}{chapter}章{startVerse}-{endVerse}节");
                //#endif

                // 第1步：选择对应的分类（根据书卷所属分类）
                string targetCategory = book.Category;
                
                // 特殊处理：福音使徒和普通书信
                if (book.Category == "福音书" || book.Name == "使徒行传")
                {
                    targetCategory = "福音使徒";
                }
                else if (book.Category == "普通书信" || book.Name == "启示录")
                {
                    targetCategory = "普通书信";
                }

                // 查找并选择对应的分类
                if (BibleCategoryList.ItemsSource is System.Collections.ObjectModel.ObservableCollection<string> categories)
                {
                    var targetCategoryItem = categories.FirstOrDefault(c => c == targetCategory);
                    if (targetCategoryItem != null && BibleCategoryList.SelectedItem?.ToString() != targetCategory)
                    {
                        BibleCategoryList.SelectedItem = targetCategoryItem;
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"[圣经导航同步] 已选择分类: {targetCategory}");
                        //#endif
                    }
                }

                // 第2步：选择书卷（需要等待分类加载完成）
                Dispatcher.InvokeAsync(() =>
                {
                    if (BibleBookList.ItemsSource is System.Collections.Generic.List<BibleBook> bookList)
                    {
                        var targetBook = bookList.FirstOrDefault(b => b.BookId == bookId);
                        if (targetBook != null)
                        {
                            BibleBookList.SelectedItem = targetBook;
                            
                            // 🆕 将选中的书卷滚动到第一位（顶部）
                            BibleBookList.ScrollIntoView(targetBook);
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[圣经导航同步] 已选择书卷: {targetBook.Name}，并滚动到顶部");
                            //#endif
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // 第3步：选择章节（需要等待书卷加载完成）
                Dispatcher.InvokeAsync(async () =>
                {
                    if (BibleChapterList.ItemsSource is System.Collections.Generic.List<string> chapterList)
                    {
                        var targetChapter = chapterList.FirstOrDefault(c => c == chapter.ToString());
                        if (targetChapter != null)
                        {
                            BibleChapterList.SelectedItem = targetChapter;
                            
                            // 🆕 将选中的章节滚动到第一位（顶部）
                            BibleChapterList.ScrollIntoView(targetChapter);
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[圣经导航同步] 已选择章节: {chapter}章，并滚动到顶部");
                            //#endif
                            
                            // 🔧 手动加载节号列表（因为选择事件被标志阻止了）
                            int verseCount = await _bibleService.GetVerseCountAsync(bookId, chapter);
                            if (verseCount > 0)
                            {
                                var verseNumbers = Enumerable.Range(1, verseCount).Select(v => v.ToString()).ToList();
                                BibleStartVerse.ItemsSource = verseNumbers;
                                BibleEndVerse.ItemsSource = verseNumbers;
                                
                                // 🔧 等待UI更新完成
                                await System.Threading.Tasks.Task.Delay(10);
                                
                                // 第4步：选择起始节和结束节（确保节号列表已加载）
                                var targetStartVerse = verseNumbers.FirstOrDefault(v => v == startVerse.ToString());
                                if (targetStartVerse != null)
                                {
                                    BibleStartVerse.SelectedItem = targetStartVerse;
                                    
                                    // 🆕 将选中的起始节滚动到第一位（顶部）
                                    BibleStartVerse.ScrollIntoView(targetStartVerse);
                                    
                                    //#if DEBUG
                                    //System.Diagnostics.Debug.WriteLine($"[圣经导航同步] 已选择起始节: {startVerse}节，并滚动到顶部");
                                    //#endif
                                }

                                var targetEndVerse = verseNumbers.FirstOrDefault(v => v == endVerse.ToString());
                                if (targetEndVerse != null)
                                {
                                    BibleEndVerse.SelectedItem = targetEndVerse;
                                    
                                    // 🆕 将选中的结束节滚动到第一位（顶部）
                                    BibleEndVerse.ScrollIntoView(targetEndVerse);
                                    
                                    //#if DEBUG
                                    //System.Diagnostics.Debug.WriteLine($"[圣经导航同步] 已选择结束节: {endVerse}节，并滚动到顶部");
                                    //#endif
                                }
                            }
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [圣经导航同步] 同步完成: {book.Name}{chapter}章{startVerse}-{endVerse}节");
                //#endif
            }
            catch (Exception)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [圣经导航同步] 同步失败");
                //#endif
            }
            finally
            {
                // 🔧 延迟重置同步标志，确保所有异步操作完成
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(10);
                    _isNavigationSyncing = false;
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }


        /// <summary>
        /// 加载单节经文
        /// </summary>
        private async Task<BibleVerse> LoadVerseAsync(int book, int chapter, int verse)
        {
            try
            {
                var verseData = await _bibleService.GetVerseAsync(book, chapter, verse);

                //#if DEBUG
                //Debug.WriteLine($"[圣经] 加载经文: {verseData?.Reference} - {verseData?.Scripture}");
                //#endif

                return verseData;
            }
//#if DEBUG
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[圣经] 加载经文失败: {ex.Message}");
//                return null;
//            }
//#else
            catch (Exception)
            {
                return null;
            }
//#endif
        }

        /// <summary>
        /// 更新主屏幕底部扩展空间（支持底部内容向上拉）
        /// </summary>
        private void UpdateMainScreenBottomExtension()
        {
            try
            {
                // 等待 ScrollViewer 完成布局
                Dispatcher.InvokeAsync(() =>
                {
                    if (BibleVerseScrollViewer != null && BibleBottomExtension != null)
                    {
                        double viewportHeight = BibleVerseScrollViewer.ViewportHeight;
                        BibleBottomExtension.Height = viewportHeight;
                        
                        //#if DEBUG
                        //Debug.WriteLine($"🔧 [主屏扩展] 设置底部扩展高度: {viewportHeight:F2}");
                        //Debug.WriteLine($"🔧 [主屏扩展] 说明: 主屏幕和投影的底部扩展高度必须一致(=屏幕/视口高度)，以确保顶部对齐");
                        //#endif
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception)
            {
                // 忽略错误
            }
        }
        
        /// <summary>
        /// 圣经内容滚动事件（同步到投影）
        /// </summary>
        private void BibleVerseContentScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 如果投影已开启且在圣经模式，同步滚动位置
            if (_isBibleMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                // 🔧 直接偏移滚动同步：主屏幕和投影屏幕使用相同的渲染逻辑，内容高度一致，直接同步滚动偏移
                double mainScrollOffset = BibleVerseScrollViewer.VerticalOffset;
                
                //#if DEBUG
                //_debugScrollCount++;
                //bool shouldDebug = (_debugScrollCount % 10 == 0);
                //
                ////// 每隔10次输出一次详细信息（减少日志量）
                ////if (shouldDebug)
                ////{
                ////    Debug.WriteLine($"");
                ////    Debug.WriteLine($"🔍 ===== 圣经滚动详细调试 =====");
                ////    
                ////    // 获取主屏幕DPI
                ////    var mainDpi = VisualTreeHelper.GetDpi(BibleVerseScrollViewer);
                ////    Debug.WriteLine($"📐 [主屏幕] DPI: {mainDpi.PixelsPerInchX} x {mainDpi.PixelsPerInchY}");
                ////    Debug.WriteLine($"📐 [主屏幕] DPI缩放: {mainDpi.DpiScaleX:F2} x {mainDpi.DpiScaleY:F2}");
                ////    
                ////    Debug.WriteLine($"📊 [主屏幕] 滚动偏移: {mainScrollOffset:F2} (将传给投影)");
                ////    Debug.WriteLine($"📊 [主屏幕] 可滚动高度: {BibleVerseScrollViewer.ScrollableHeight:F2}");
                ////    Debug.WriteLine($"📊 [主屏幕] 视口高度: {BibleVerseScrollViewer.ViewportHeight:F2}");
                ////    Debug.WriteLine($"📊 [主屏幕] 内容总高度: {BibleVerseScrollViewer.ExtentHeight:F2}");
                ////    
                ////    if (BibleChapterTitle != null)
                ////    {
                ////        Debug.WriteLine($"📊 [主屏幕] 标题实际高度: {BibleChapterTitle.ActualHeight:F2}");
                ////        var titleBorder = BibleChapterTitle.Parent as Border;
                ////        if (titleBorder != null)
                ////        {
                ////            Debug.WriteLine($"📊 [主屏幕] 标题Border总高度: {titleBorder.ActualHeight:F2} (含Padding)");
                ////        }
                ////    }
                ////    
                ////    if (BibleVerseList != null)
                ////    {
                ////        Debug.WriteLine($"📊 [主屏幕] 经文列表高度: {BibleVerseList.ActualHeight:F2}");
                ////    }
                ////    
                ////    if (BibleBottomExtension != null)
                ////    {
                ////        Debug.WriteLine($"📊 [主屏幕] 底部扩展高度: {BibleBottomExtension.ActualHeight:F2}");
                ////    }
                ////}
                //#endif

                // 🔧 圣经滚动同步：直接使用主屏滚动位置（与歌词投影完全一致）
                // 因为两者使用相同的渲染逻辑，内容高度一致，直接同步滚动偏移
                _projectionManager.SyncBibleScroll(BibleVerseScrollViewer);
            }
        }
        
//#if DEBUG
//        private int _debugScrollCount = 0;
//#endif

        #endregion

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
            if (sender is not Border border || border.Tag is not BibleVerse clickedVerse)
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
            // ItemsControl 不支持 ScrollIntoView，使用其他方式实现滚动
            // 可以通过查找子元素并计算位置来滚动
            // 暂时简化实现：不自动滚动（用户可以手动滚动查看）
            // 如果需要自动滚动，可以后续使用 VisualTreeHelper 查找元素位置实现
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
            //System.Diagnostics.Debug.WriteLine($"🖱️ [历史记录点击] ========== 开始 ==========");
            //#endif

            if (sender is Border border && border.DataContext is BibleHistoryItem item)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   槽位: {item.Index}, 显示文本: {item.DisplayText}");
                //System.Diagnostics.Debug.WriteLine($"   当前状态: IsChecked={item.IsChecked}, IsLocked={item.IsLocked}");
                //#endif

                // 🔧 双击检测
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
                    //System.Diagnostics.Debug.WriteLine($"   ✅ 检测到双击，切换锁定状态");
                    //#endif

                    // 双击：切换锁定状态
                    bool wasLocked = item.IsLocked;
                    item.IsLocked = !item.IsLocked;

                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   锁定状态: {wasLocked} -> {item.IsLocked}");
                    //#endif

                    // 🆕 锁定后自动勾选（但不触发单击逻辑）
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

                    // 🆕 增量更新：根据锁定状态决定是添加还是删除
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
                    //System.Diagnostics.Debug.WriteLine($"🖱️ [历史记录点击] ========== 结束（双击） ==========\n");
                    //#endif
                    return;
                }

                // 🔧 单击逻辑：检查是否有锁定记录
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
                    //System.Diagnostics.Debug.WriteLine($"🖱️ [历史记录点击] ========== 结束（锁定模式单击） ==========\n");
                    //#endif
                    return;
                }

                // 🔧 无锁定记录时：单选模式
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
                        await LoadVerseRangeAsync(item.BookId, item.Chapter, item.StartVerse, item.EndVerse);
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
                        await LoadVerseRangeAsync(item.BookId, item.Chapter, item.StartVerse, item.EndVerse);
                    }
                }

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🖱️ [历史记录点击] ========== 结束（单击） ==========\n");
                //#endif
            }
        }

        /// <summary>
        /// 历史记录列表选择事件
        /// 注意：实际加载经文由BibleHistoryItem_Click处理，此处不重复加载
        /// </summary>
        private void BibleHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 🔧 此事件暂时保留，用于未来可能的选中状态同步
            // 实际的经文加载由BibleHistoryItem_Click事件处理，避免重复加载
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
                    item.IsChecked = false;  // 🆕 同时清空勾选状态，防止误覆盖
                }
                
                await LoadAndDisplayLockedRecords(); // 会清空显示
                
                // 🆕 更新清空按钮样式（从绿色恢复成白色）
                UpdateClearButtonStyle();
                
                // 🆕 刷新列表显示，确保界面更新
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
        /// 🔧 使用增量更新，避免刷新闪烁
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
                
                // 🆕 构建新的经文列表
                var newVerses = new List<BibleVerse>();
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"📚 [锁定模式] 开始加载 {lockedItems.Count} 条锁定记录");
                #endif
                
                foreach (var item in lockedItems)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"📖 [锁定模式] 加载槽位{item.Index}: {item.DisplayText}");
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
                            System.Diagnostics.Debug.WriteLine($"  ✅ 添加经文: {item.BookId}章{item.Chapter}:{verse}节");
                            #endif
                        }
                    }
                }
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"📚 [锁定模式] 加载完成，共 {newVerses.Count} 行（含标题）");
                #endif
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经] 合并完成，共 {newVerses.Count} 行（含标题）");
                //#endif
                
                // 更新主屏幕标题（清空，因为合并模式下每组都有自己的标题）
                BibleChapterTitle.Text = "";
                
                // 🆕 增量更新：只在首次绑定时设置ItemsSource，后续使用Clear/Add
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
                System.Diagnostics.Debug.WriteLine($"📊 [锁定模式] 最终列表中共 {_mergedVerses.Count} 条记录");
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
                
                // 更新投影（🆕 使用 VisualBrush）
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
                
                // 🔧 检查是否有锁定记录，如果有锁定记录，_mergedVerses应该只包含锁定记录的经文
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
                        //System.Diagnostics.Debug.WriteLine($"🔒 [增量添加] 检测到非锁定模式的经文，清空_mergedVerses（{_mergedVerses.Count}条）");
                        //#endif
                        _mergedVerses.Clear();
                    }
                }
                
                // 🔧 检查该记录是否已经存在，避免重复插入
                var book = BibleBookConfig.GetBook(item.BookId);
                string verseText = (item.StartVerse == item.EndVerse) ? $"{item.StartVerse}节" : $"{item.StartVerse}-{item.EndVerse}节";
                string titleText = $"{book?.Name}{item.Chapter}章{verseText}";
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🔍 [增量添加] 开始检查记录: {titleText}");
                //System.Diagnostics.Debug.WriteLine($"🔍 [增量添加] 当前列表总数: {_mergedVerses.Count}");
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
                //System.Diagnostics.Debug.WriteLine($"🔍 [增量添加] 检查结果: alreadyExists={alreadyExists}, 查找条件: Verse=0, Book={item.BookId}, Chapter={item.Chapter}, Scripture={titleText}");
                //#endif
                
                if (alreadyExists)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"🔒 [增量添加] 记录已存在，跳过插入: {titleText}");
                    //#endif
                    return;
                }
                
                // 找到插入位置（根据槽位顺序）
                // 🔧 应该根据 _mergedVerses 中实际已存在的锁定记录来计算插入位置
                var lockedItems = _historySlots
                    .Where(x => x.IsLocked && x.BookId > 0)
                    .OrderBy(x => x.Index)
                    .ToList();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🔍 [插入位置计算] 锁定记录总数: {lockedItems.Count}, 当前item槽位: {item.Index}");
                //#endif
                
                int insertIndex = 0;
                foreach (var lockedItem in lockedItems)
                {
                    if (lockedItem == item)
                    {
                        // 找到当前item的位置
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"🔍 [插入位置计算] 找到当前item，停止计算，insertIndex={insertIndex}");
                        //#endif
                        break;
                    }
                    
                    // 🔧 检查 _mergedVerses 中是否已经有该锁定记录
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
                        //System.Diagnostics.Debug.WriteLine($"🔍 [插入位置计算] 槽位{lockedItem.Index}已存在，增加{verseCount}，insertIndex={insertIndex}");
                        //#endif
                    }
                    // 如果不存在，说明还没有加载，不增加 insertIndex
                }
                
                // 🔧 确保 insertIndex 不超过当前列表长度
                if (insertIndex > _mergedVerses.Count)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"🔍 [插入位置计算] insertIndex({insertIndex})超过列表长度({_mergedVerses.Count})，调整为{_mergedVerses.Count}");
                    //#endif
                    insertIndex = _mergedVerses.Count;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🔍 [插入位置计算] 最终结果: 当前列表总数={_mergedVerses.Count}, 计算出的插入位置={insertIndex}, 记录={titleText}");
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
                //    System.Diagnostics.Debug.WriteLine($"🔍 [插入调试] 第{debugIdx}条: Verse={v.Verse}, Scripture={v.Scripture?.Substring(0, Math.Min(30, v.Scripture?.Length ?? 0))}");
                //}
                //#endif
                
                // 逐个插入（ObservableCollection会自动更新UI）
                int insertPos = insertIndex;
                foreach (var verse in versesToAdd)
                {
                    _mergedVerses.Insert(insertPos, verse);
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"🔍 [插入调试] 插入到位置 {insertPos}, Verse={verse.Verse}, 当前列表总数: {_mergedVerses.Count}");
                    //#endif
                    insertPos++;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📊 [增量添加] 插入完成，最终列表共 {_mergedVerses.Count} 条记录");
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
                    //System.Diagnostics.Debug.WriteLine($"🔍 [二次样式应用] 再次应用样式确保完整");
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
            //System.Diagnostics.Debug.WriteLine($"🔄 [更新合并投影] ========== 开始 ==========");
            //#endif

            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
                var verseList = _mergedVerses.ToList();

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   合并经文数量: {verseList.Count}");
                //#endif

                if (verseList.Count > 0 && BibleVerseScrollViewer != null)
                {
                    // 🆕 使用 VisualBrush 投影（100%像素级一致）
                    _projectionManager.UpdateBibleProjectionWithVisualBrush(BibleVerseScrollViewer);
                }
                else
                {
                    _projectionManager.ClearProjectionDisplay();
                }
            }

            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🔄 [更新合并投影] ========== 结束 ==========\n");
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
                        System.Diagnostics.Debug.WriteLine("🔄 [圣经设置] 译本切换，重新加载经文");
                        #endif
                        
                        // 应用设置
                        ApplyBibleSettings();

                        // 🔄 重新加载当前章节
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
                                // 📌 非锁定模式：更新当前章节的投影
                                RenderBibleToProjection();
                            }
                        }
                    },
                    // 样式改变回调（只刷新样式，不重新加载经文）
                    async () =>
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine("🎨 [圣经设置] 样式改变，刷新显示");
                        //#endif
                        
                        // 应用设置
                        ApplyBibleSettings();
                        
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

                // 🔧 计算窗口位置：统一定位在设置按钮的右边
                if (_configManager.BibleSettingsWindowLeft.HasValue && _configManager.BibleSettingsWindowTop.HasValue)
                {
                    _bibleSettingsWindow.Left = _configManager.BibleSettingsWindowLeft.Value;
                    _bibleSettingsWindow.Top = _configManager.BibleSettingsWindowTop.Value;
                }
                else if (BtnBibleSettings != null)
                {
                    // 🔧 相对于主窗口定位，避免屏幕坐标转换问题
                    // 获取按钮相对于主窗口的位置
                    var buttonPos = BtnBibleSettings.TransformToAncestor(this)
                        .Transform(new System.Windows.Point(0, 0));
                    
                    // 🔧 简单定位：窗口位置 = 主窗口位置 + 按钮相对位置 + 偏移
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
                        // 📌 锁定模式：译本切换时，更新锁定记录的投影
                        await UpdateProjectionFromMergedVerses();
                    }
                    else
                    {
                        // 📌 非锁定模式：更新当前章节的投影
                        RenderBibleToProjection();
                    }
                }
                
                ShowStatus($"✅ 已切换到: {versionName}");
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经译本] 切换失败: {ex.Message}");
                //#else
                _ = ex;
                //#endif
                ShowStatus($"❌ 切换译本失败");
            }
        }

        /// <summary>
        /// 应用圣经设置到界面
        /// </summary>
        private void ApplyBibleSettings()
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🎨 [应用圣经样式] ========== 开始 ==========");
            //#endif

            try
            {
                // 应用背景色
                var backgroundColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleBackgroundColor);
                BibleVerseScrollViewer.Background = new WpfSolidColorBrush(backgroundColor);

                // 应用标题背景色（与经文背景色一致）
                BibleChapterTitleBorder.Background = new WpfSolidColorBrush(backgroundColor);

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
                    //System.Diagnostics.Debug.WriteLine($"⚠️ [ApplyVerseStyles] 列表为空，跳过样式应用");
                    //#endif
                    return;
                }

                // ========================================
                // 📌 模式判断
                // ========================================
                var firstVerse = BibleVerseList.Items[0] as BibleVerse;
                bool isLockedMode = firstVerse != null && firstVerse.Verse == 0;

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🔍 [ApplyVerseStyles] 开始应用样式，总共 {BibleVerseList.Items.Count} 条记录，模式={isLockedMode}");
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
                            System.Diagnostics.Debug.WriteLine($"⚠️ [ApplyVerseStyles] 容器{i}未生成（null），但该经文被高亮: {tempVerse.Reference}");
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
                    //    System.Diagnostics.Debug.WriteLine($"🔧 [ApplyVerseStyles] 处理高亮经文{i}: {verse.Reference}");
                    //}
                    //#endif

                    // 🔧 查找单个 TextBlock（新布局）
                    var verseTextBlock = FindVisualChild<TextBlock>(container);
                    if (verseTextBlock != null)
                    {
                        // 清空并重新构建 Inlines
                        verseTextBlock.Inlines.Clear();
                        verseTextBlock.FontFamily = fontFamily;
                        
                        // ========================================
                        // 📌 锁定模式：渲染标题行
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
                            // 📌 渲染普通经文行（锁定模式和非锁定模式通用）
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
                            //    System.Diagnostics.Debug.WriteLine($"📝 [圣经主屏] 经文{i}使用默认颜色: {verse.Reference}");
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
                    // 📌 设置Border的Margin（节间距）
                    // ========================================
                    var border = FindVisualChild<Border>(container);
                    if (border != null)
                    {
                        // 📌 锁定模式：标题行使用更大的间距（记录之间的分隔）
                        if (verse.Verse == 0)
                        {
                            // 第一个标题行：顶部间距为0（置顶显示）
                            // 后续标题行：顶部间距固定为60（作为记录分隔，不随节距变化）
                            double topMargin = (i == 0) ? 0 : 60;
                            // 标题底部间距固定为15，不随节距变化
                            border.Margin = new Thickness(0, topMargin, 0, 15);
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"🔍 [主屏标题Margin] i={i}, 节距配置={_configManager.BibleVerseSpacing}, topMargin={topMargin}(固定), 底部固定=15, 实际Margin={border.Margin}");
                            //#endif
                        }
                        else
                        {
                            // 📌 普通经文行：第一节经文上边距固定为0（与标题间距由XAML中的Border控制），其他经文使用配置的节距
                            double topMargin = (i == 0 || (i == 1 && _mergedVerses.Count > 0 && _mergedVerses[0].Verse == 0)) 
                                ? 0  // 第一节经文：上边距为0
                                : _configManager.BibleVerseSpacing / 2;  // 其他经文：使用配置的节距
                            
                            border.Margin = new Thickness(0, topMargin, 0, _configManager.BibleVerseSpacing / 2);
                            
                            //#if DEBUG
                            //if (i <= 1) // 输出前两个经文的调试信息
                            //{
                            //    System.Diagnostics.Debug.WriteLine($"🔍 [主屏经文Margin] i={i}, 第{verse.Verse}节, 节距配置={_configManager.BibleVerseSpacing}, topMargin={topMargin}, 实际Margin={border.Margin}");
                            //}
                            //#endif
                        }
                        
                        //#if DEBUG
                        //if (i == 0) // 只输出第一个经文的调试信息
                        //{
                        //    Debug.WriteLine($"");
                        //    Debug.WriteLine($"🔧 [圣经样式应用]");
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


