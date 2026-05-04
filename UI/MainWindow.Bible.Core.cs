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
    /// MainWindow Bible Core (Fields, Models, Initialization, Views, Tree, Data, Verses)
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
        private enum BibleCopyHeaderStyle
        {
            Short,      // [约1:1-2]
            Full,       // [约翰福音1:1-2]
            Chapter     // [约翰福音1章1-2节]
        }

        private BibleCopyHeaderStyle _bibleCopyHeaderStyle = BibleCopyHeaderStyle.Short;
        private const string BibleCopyHeaderStyleSettingKey = "BibleCopyHeaderStyle";
        
        // 双击检测
        private DateTime _lastHistoryClickTime = DateTime.MinValue;
        private BibleHistoryItem _lastHistoryClickedItem = null;
        private const int DoubleClickInterval = 300; // 毫秒
        
        // 拼音快速定位功能
        private ImageColorChanger.Services.BiblePinyinService _pinyinService;
        private ImageColorChanger.Managers.BiblePinyinInputManager _pinyinInputManager;
        
        // 导航栏同步标志（防止同步时触发不必要的事件）
        private bool _isNavigationSyncing = false;
        private bool _bibleNdiScrollPublishQueued = false;
        private long _lastBibleNdiScrollPublishTick = 0;
        
        // 圣经样式设置 Popup（复用实例）
        private BibleInsertStylePopup _bibleStylePopup = null;
        
        // 圣经设置窗口（复用实例）
        private BibleSettingsWindow _bibleSettingsWindow = null;

        // 历史记录悬浮预览
        private System.Threading.CancellationTokenSource _bibleHistoryPreviewCts = null;
        private BibleHistoryItem _bibleHistoryPreviewPendingItem = null;
        
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

        private string GetBibleDbFilePath()
        {
            var dbFileName = _configManager?.BibleDatabaseFileName ?? "bible.db";
            return System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "data", "assets", dbFileName);
        }

        /// <summary>
        /// 初始化圣经服务
        /// </summary>
        private void InitializeBibleService()
        {
            try
            {
                _bibleModuleController ??= new Modules.BibleModuleController(Dispatcher);
                _memoryCache ??= _mainWindowServices.GetRequired<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                _bibleService = _bibleModuleController.CreateService(_memoryCache, _configManager);

                //#if DEBUG
                //Debug.WriteLine("[圣经] 服务初始化成功");
                //#endif

                _bibleModuleController.StartDatabaseAvailabilityProbe(
                    _bibleService,
                    () =>
                    {
                        WpfMessageBox.Show(
                            "圣经数据库文件未找到！\n请确保 bible.db 文件位于 data/assets/ 目录下。",
                            "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });

                LoadBibleCopyStyleSetting();
                EnsureBibleSearchComponentsInitialized();
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

            // 如果在幻灯片编辑模式下，只切换左侧导航面板
            if (TextEditorPanel.Visibility == Visibility.Visible)
            {
                //#if DEBUG
                //Debug.WriteLine($" [圣经] 在幻灯片编辑模式下，切换左侧导航面板");
                //#endif
                
                // 切换左侧导航面板：ProjectTree <-> BibleNavigationPanel
                if (BibleNavigationPanel.Visibility == Visibility.Visible)
                {
                    // 当前显示圣经，切换回项目树
                    bool wasInBibleMode = _isBibleMode;
                    bool isProjectionActive = _projectionManager?.IsProjectionActive == true;
                    BibleNavigationPanel.Visibility = Visibility.Collapsed;
                    ProjectTree.Visibility = Visibility.Visible;
                    _currentViewMode = NavigationViewMode.Projects;
                    _isBibleMode = false;  // 退出圣经模式
                    if (Modules.BibleUiBehaviorResolver.ShouldClearBibleProjectionWhenSwitchingToSlides(
                            wasInBibleMode,
                            isProjectionActive))
                    {
                        ClearProjectedBibleContentForSlideSwitch();
                    }
                    else
                    {
                        ApplyBibleTitleDisplayMode(false);
                        SyncProjectionBibleTitle();
                    }

                    if (Modules.BibleUiBehaviorResolver.ShouldAutoLoadBlankSlideOnProjectsViewEntry(
                            wasInBibleMode,
                            isProjectionActive,
                            _isProjectionLocked))
                    {
                        await EnsureBlankSlideOnBibleProjectionExitAsync();
                    }
                    else if (Modules.BibleUiBehaviorResolver.ShouldRestoreLockedSlideOnProjectsViewEntry(
                                 _isProjectionLocked,
                                 _lockedProjectionProjectId.HasValue,
                                 _lockedProjectionSlideId.HasValue))
                    {
                        await RestoreLockedProjectionSlideAsync();
                    }
                    else
                    {
                        RefreshProjectionFromCurrentSlideIfNeeded();
                    }
                    
                    //#if DEBUG
                    //Debug.WriteLine($" [圣经] 切换到项目树");
                    //#endif
                }
                else
                {
                    // 当前显示项目树，切换到圣经
                    ProjectTree.Visibility = Visibility.Collapsed;
                    BibleNavigationPanel.Visibility = Visibility.Visible;
                    _currentViewMode = NavigationViewMode.Bible;
                    _isBibleMode = true;  // 进入圣经模式（关键修复！）
                    // 切入圣经时先清空旧标题，避免显示上一次投影记录的标题。
                    // 当前版本要求：经文需手动点击历史槽/导航后才显示。
                    BibleChapterTitle.Text = string.Empty;
                    ApplyBibleTitleDisplayMode(false);
                    SyncProjectionBibleTitle();
                    
                    // 如果还未初始化，则初始化
                    if (!_bibleNavigationInitialized)
                    {
                        await LoadBibleNavigationDataAsync();
                    }
                    
                    //#if DEBUG
                    //Debug.WriteLine($" [圣经] 切换到圣经导航");
                    //#endif
                }
                
                // 更新按钮状态
                UpdateViewModeButtons();
                if (_currentViewMode == NavigationViewMode.Projects && _currentTextProject != null)
                {
                    ApplyUnifiedSearchResetAndTreeRefresh(TreeItemType.TextProject, _currentTextProject.Id);
                }
                else
                {
                    ApplyUnifiedSearchResetAndTreeRefresh();
                }
                
                return;
            }

            // 否则，切换到完整的圣经页面
            //#if DEBUG
            //Debug.WriteLine($"[圣经] 切换到完整圣经页面");
            //#endif

            _isBibleMode = true;
            _currentViewMode = NavigationViewMode.Bible;  // 设置当前视图模式为圣经

            // 切入圣经时先清空旧标题，避免显示上一次投影记录的标题。
            // 当前版本要求：经文需手动点击历史槽/导航后才显示。
            BibleChapterTitle.Text = string.Empty;
            ApplyBibleTitleDisplayMode(false);
            SyncProjectionBibleTitle();

            // 清空图片显示（包括合成播放按钮）
            ClearImageDisplay();
            
            // 更新合成播放按钮显示状态（隐藏按钮）
            UpdateFloatingCompositePlayButton();

            // 隐藏ProjectTree，显示圣经导航面板
            ProjectTree.Visibility = Visibility.Collapsed;
            BibleNavigationPanel.Visibility = Visibility.Visible;

            //#if DEBUG
            //Debug.WriteLine($"[圣经] 导航切换完成, ProjectTree={ProjectTree.Visibility}, BiblePanel={BibleNavigationPanel.Visibility}");
            //
            ////// 打印导航栏宽度信息（异步调试输出，不需要等待）
            ////_ = Dispatcher.InvokeAsync(() =>
            ////{
            ////    if (NavigationPanelColumn != null)
            ////    {
            ////        Debug.WriteLine($"");
            ////        Debug.WriteLine($"===== 圣经导航栏宽度信息 =====");
            ////        Debug.WriteLine($" [导航栏] 设定宽度: {NavigationPanelColumn.Width}");
            ////        Debug.WriteLine($" [导航栏] 实际宽度: {NavigationPanelColumn.ActualWidth:F2}");
            ////    }
            ////    
            ////    if (BibleNavigationPanel != null)
            ////    {
            ////        Debug.WriteLine($" [圣经面板] 实际宽度: {BibleNavigationPanel.ActualWidth:F2}");
            ////    }
            ////    
            ////    // 打印5列的宽度设置
            ////    Debug.WriteLine($"[表格列宽] 第1列(分类): 70");
            ////    Debug.WriteLine($"[表格列宽] 第2列(书卷): 120");
            ////    Debug.WriteLine($"[表格列宽] 第3列(章): 60");
            ////    Debug.WriteLine($"[表格列宽] 第4列(起始节): 60");
            ////    Debug.WriteLine($"[表格列宽] 第5列(结束节): 60");
            ////    Debug.WriteLine($"[表格列宽] 总计: 370");
            ////    Debug.WriteLine($"  [结论] 导航栏宽度需要390以上才能完整显示5列！");
            ////    Debug.WriteLine($"");
            ////}, System.Windows.Threading.DispatcherPriority.Loaded);
            //#endif

            // 加载圣经数据
            await LoadBibleNavigationDataAsync();
            
            // 需求：从文件/幻灯片切回圣经时，不自动加载历史槽经文。
            // 历史槽只保留状态，必须手动点击槽位才触发经文加载。
            
            // 初始化拼音快速定位服务
            InitializePinyinService();
            
            // 更新译本选择按钮状态
            UpdateBibleVersionRadioButtons();
            
            // 显示圣经视图区域，隐藏其他区域
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            VideoContainer.Visibility = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Collapsed;
            BibleDisplayContainer.Visibility = Visibility.Visible;
            
            // 确保 BibleVerseScrollViewer 可见（关键修复！）
            BibleVerseScrollViewer.Visibility = Visibility.Visible;
            
            // 如果有经文内容，确保列表可见
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
            ApplyUnifiedSearchResetAndTreeRefresh();
            EnsureBibleQuickLocateFocus("BtnShowBible_Click");
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
        /// 优化：只在首次加载时初始化，后续切换回来时保留用户选择状态
        /// </summary>
        private Task LoadBibleNavigationDataAsync()
        {
            try
            {
                //#if DEBUG
                //var sw = Stopwatch.StartNew();
                //#endif

                // 只在首次加载时初始化
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

                    // 加载第1列:分类列表
                    var categories = new ObservableCollection<string>
                    {
                        "全部",
                        "旧约",
                        "新约",
                        "摩西五经",
                        "旧约历史",
                        "诗歌智慧",
                        "大先知书",
                        "小先知书",
                        "福音使徒",
                        "保罗书信",
                        "普通书信"
                    };

                    BibleCategoryList.ItemsSource = categories;

                    // 默认选中"全部"
                    BibleCategoryList.SelectedItem = "全部";
                    
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
                // 统一数据源方案：始终更新 _mergedVerses
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
                
                // 获取该章总节数，显示完整范围（如"创世记3章1-24节"）
                int verseCount = await _bibleService.GetVerseCountAsync(book, chapter);
                
                // 非锁定模式：同步导航栏状态
                SyncNavigationToRecord(book, chapter, 1, verseCount > 0 ? verseCount : 1);
                
                // 非锁定模式：清空并添加新经文到 _mergedVerses
                string verseText = (verseCount > 1) ? $"1-{verseCount}节" : "1节";
                BibleChapterTitle.Text = $"{bookInfo?.Name}{chapter}章{verseText}";
                ApplyBibleTitleDisplayMode(true);
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[非锁定模式] 加载章节: {verses.Count}条经文，{bookInfo?.Name}{chapter}章");
                //#endif
                
                // 清空并添加新数据
                _mergedVerses.Clear();
                foreach (var verse in verses)
                {
                    _mergedVerses.Add(verse);
                }

                EnsureFirstVerseHighlightedByDefault();
                
                // 重置滚动条到顶部
                BibleVerseScrollViewer.ScrollToTop();
                
                // 设置主屏幕底部扩展空间（按置顶偏移增加额外空间，避免后段经文无法继续上推）
                UpdateMainScreenBottomExtension();

                // 应用样式后再更新投影（确保高度计算完成）
                _ = Dispatcher.InvokeAsync(() =>
                {
                    ApplyBibleSettings();

                    // 样式应用完成后，更新投影
                    if (_isBibleMode && _projectionManager != null && _projectionManager.IsProjecting)
                    {
                        // 检查是否有锁定记录
                        if (_historySlots.Any(x => x.IsLocked))
                        {
                            // 锁定模式：加载新章节时，不更新投影（保持锁定记录投影）
                        }
                        else
                        {
                            // 非锁定模式：样式应用后更新投影
                            RenderBibleToProjection();
                        }
                    }
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
                case "全部":
                    books = allBooks;
                    break;
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
                    books = allBooks.Where(b => b.Category == "福音书" ||
                                               (b.Name == "使徒行传" && b.Testament == "新约"));
                    break;
                case "普通书信":
                    books = allBooks.Where(b => b.Category == "普通书信" || b.Name == "启示录");
                    break;
                default:
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
            // 如果正在同步导航栏，跳过事件处理
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

            // BUG修复：获取原始节数（包含"-"节），而不是处理后的列表长度
            // 原因：GetChapterVersesAsync会合并"-"节，导致返回的列表长度小于实际节数
            // 例如：约书亚记3章有17节（包含"-"节），但处理后只有16个元素
            int verseCount = await _bibleService.GetVerseCountAsync(bookId, chapter);
            
            //#if DEBUG
            //var verses = await _bibleService.GetChapterVersesAsync(bookId, chapter);
            //int processedCount = verses?.Count ?? 0;
            //Debug.WriteLine($"[圣经-节数获取] 原始节数: {verseCount}, 处理后列表长度: {processedCount}");
            //if (verseCount != processedCount)
            //{
            //    Debug.WriteLine($"[圣经-节数获取]  检测到节数差异！该章可能包含精简的'-'节");
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
                // 统一数据源方案：不需要手动清空
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

            // 优化：检查是否已经加载了该章的完整内容，避免重复刷新
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
                
                // 如果在幻灯片编辑模式，双击章节自动插入整章经文
                if (TextEditorPanel.Visibility == Visibility.Visible)
                {
                    #if DEBUG
                    Debug.WriteLine($" [圣经双击] 双击章节，自动插入整章: BookId={bookId}, Chapter={chapter}, 节范围: 1-{verseCount}");
                    #endif
                    
                    await HandleBibleVerseSelectionInSlideModeAsync(bookId, chapter, 1, verseCount);
                }
                else
                {
                    // 非编辑模式：加载经文到投影区（与双击开始节行为一致）
                    await LoadVerseRangeAsync(bookId, chapter, 1, verseCount);
                }
                
                // 更新历史槽位
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

            // 优化：自动滚动结束节列表到起始节位置，并自动选中结束节为起始节
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

            // 双击起始节：将结束节也设置为起始节（表示只要一节）
            // 先设置结束节选择（不触发加载，避免重复）
            if (BibleEndVerse.Items.Count > 0 && startVerse > 0 && startVerse <= BibleEndVerse.Items.Count)
            {
                int targetIndex = startVerse - 1;
                BibleEndVerse.SelectionChanged -= BibleEndVerse_SelectionChanged;
                BibleEndVerse.SelectedIndex = targetIndex;
                BibleEndVerse.SelectionChanged += BibleEndVerse_SelectionChanged;
            }
            
            // 如果在幻灯片编辑模式，双击起始节自动插入该节经文
            if (TextEditorPanel.Visibility == Visibility.Visible)
            {
                //#if DEBUG
                //Debug.WriteLine($" [圣经双击] 双击起始节，自动插入单节: BookId={bookId}, Chapter={chapter}, Verse={startVerse}");
                //#endif
                
                await HandleBibleVerseSelectionInSlideModeAsync(bookId, chapter, startVerse, startVerse);
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

            // 临时取消事件处理，避免触发加载
            BibleEndVerse.SelectionChanged -= BibleEndVerse_SelectionChanged;
            BibleEndVerse.SelectedIndex = targetIndex;
            BibleEndVerse.SelectionChanged += BibleEndVerse_SelectionChanged;
            
            // 延迟滚动：使用LineUp/LineDown的方式精确滚动
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
                        // 方案1：先滚动到顶部，然后使用LineDown精确滚动到目标行
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

            // 立即选中（触发加载经文）
            BibleEndVerse.SelectedIndex = targetIndex;
            
            // 延迟滚动：使用LineUp/LineDown的方式精确滚动
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
                        // 方案1：先滚动到顶部，然后使用LineDown精确滚动到目标行
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
            // 如果正在同步导航栏，跳过事件处理（防止重复加载）
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

            // 如果在幻灯片编辑模式，自动创建文本框元素
            if (TextEditorPanel.Visibility == Visibility.Visible)
            {
                //#if DEBUG
                //Debug.WriteLine($" [圣经] 在幻灯片编辑模式，自动创建文本框元素");
                //#endif

                await HandleBibleVerseSelectionInSlideModeAsync(bookId, chapter, startVerse, endVerse);
                return;
            }

            // 否则，加载到投影记录（圣经浏览模式）
            //#if DEBUG
            //Debug.WriteLine($" [圣经] 在圣经浏览模式，加载到投影记录");
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
            //System.Diagnostics.Debug.WriteLine($"[加载经文] ========== 开始 ==========");
            //System.Diagnostics.Debug.WriteLine($"   参数: BookId={bookId}, Chapter={chapter}, Verse={startVerse}-{endVerse}");
            //#endif

            try
            {
                _currentBook = bookId;
                _currentChapter = chapter;
                _currentVerse = startVerse;

                // 使用新的智能方法，自动处理"-"节的情况
                var verses = await _bibleService.GetVerseRangeAsync(bookId, chapter, startVerse, endVerse);

                var book = BibleBookConfig.GetBook(bookId);
                // 如果开始节和结束节相同，只显示一个节号（如"18节"），否则显示范围（如"18-25节"）
                string verseText = (startVerse == endVerse) ? $"{startVerse}节" : $"{startVerse}-{endVerse}节";

                // ========================================
                // 统一数据源方案：始终更新 _mergedVerses
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

                // 非锁定模式：同步导航栏状态
                SyncNavigationToRecord(bookId, chapter, startVerse, endVerse);

                // 非锁定模式：清空并添加新经文到 _mergedVerses
                BibleChapterTitle.Text = $"{book?.Name}{chapter}章 {verseText}";
                ApplyBibleTitleDisplayMode(true);

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[非锁定模式] 加载经文: {verses.Count}条，{book?.Name}{chapter}:{startVerse}-{endVerse}");
                //#endif

                // 清空并添加新数据
                _mergedVerses.Clear();
                foreach (var verse in verses)
                {
                    _mergedVerses.Add(verse);
                }

                EnsureFirstVerseHighlightedByDefault();

                // 重置滚动条到顶部
                BibleVerseScrollViewer.ScrollToTop();

                // 设置主屏幕底部扩展空间（等于视口高度,支持底部内容向上拉）
                UpdateMainScreenBottomExtension();

                // 应用样式后再更新投影（确保高度计算完成）
                _ = Dispatcher.InvokeAsync(() =>
                {
                    ApplyBibleSettings();

                    // 样式应用完成后，更新投影
                    if (_isBibleMode && _projectionManager != null && _projectionManager.IsProjecting)
                    {
                        // 检查是否有锁定记录
                        if (_historySlots.Any(x => x.IsLocked))
                        {
                            // 锁定模式：加载新章节时，不更新投影（保持锁定记录投影）
                        }
                        else
                        {
                            // 非锁定模式：样式应用后更新投影
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
                // 如果开始节和结束节相同，只显示一个节号（如"3节"），否则显示范围（如"3-5节"）
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
                //  锁定的槽位不允许更新
                foreach (var slot in checkedSlots)
                {
                    // 跳过已锁定的槽位
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
        /// 同步导航栏状态到投影记录（非锁定模式）
        /// 功能：根据投影记录自动选择对应的书卷、章节、起始节和结束节，并将书卷滚动到第一位
        /// </summary>
        private void SyncNavigationToRecord(int bookId, int chapter, int startVerse, int endVerse)
        {
            try
            {
                var book = BibleBookConfig.GetBook(bookId);
                if (book == null) return;

                // 设置同步标志，防止触发选择事件
                _isNavigationSyncing = true;

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[圣经导航同步] 开始同步: {book.Name}{chapter}章{startVerse}-{endVerse}节");
                //#endif

                // 第1步：选择对应的分类（根据书卷所属分类）
                string targetCategory = book.Category;

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
                            
                            // 将选中的书卷滚动到第一位（顶部）
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
                            
                            // 将选中的章节滚动到第一位（顶部）
                            BibleChapterList.ScrollIntoView(targetChapter);
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[圣经导航同步] 已选择章节: {chapter}章，并滚动到顶部");
                            //#endif
                            
                            // 手动加载节号列表（因为选择事件被标志阻止了）
                            int verseCount = await _bibleService.GetVerseCountAsync(bookId, chapter);
                            if (verseCount > 0)
                            {
                                var verseNumbers = Enumerable.Range(1, verseCount).Select(v => v.ToString()).ToList();
                                BibleStartVerse.ItemsSource = verseNumbers;
                                BibleEndVerse.ItemsSource = verseNumbers;
                                
                                // 等待UI更新完成
                                await System.Threading.Tasks.Task.Delay(10);
                                
                                // 第4步：选择起始节和结束节（确保节号列表已加载）
                                var targetStartVerse = verseNumbers.FirstOrDefault(v => v == startVerse.ToString());
                                if (targetStartVerse != null)
                                {
                                    BibleStartVerse.SelectedItem = targetStartVerse;
                                    
                                    // 将选中的起始节滚动到第一位（顶部）
                                    BibleStartVerse.ScrollIntoView(targetStartVerse);
                                    
                                    //#if DEBUG
                                    //System.Diagnostics.Debug.WriteLine($"[圣经导航同步] 已选择起始节: {startVerse}节，并滚动到顶部");
                                    //#endif
                                }

                                var targetEndVerse = verseNumbers.FirstOrDefault(v => v == endVerse.ToString());
                                if (targetEndVerse != null)
                                {
                                    BibleEndVerse.SelectedItem = targetEndVerse;
                                    
                                    // 将选中的结束节滚动到第一位（顶部）
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
                //System.Diagnostics.Debug.WriteLine($" [圣经导航同步] 同步完成: {book.Name}{chapter}章{startVerse}-{endVerse}节");
                //#endif
            }
            catch (Exception)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [圣经导航同步] 同步失败");
                //#endif
            }
            finally
            {
                // 延迟重置同步标志，确保所有异步操作完成
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
                        int topOffset = Math.Clamp(_configManager?.BibleScrollTopOffset ?? 0, 0, 4);
                        double estimatedVerseHeight = EstimateBibleVerseHeight();
                        double extraForTopOffset = estimatedVerseHeight * topOffset;
                        BibleBottomExtension.Height = Math.Max(0, viewportHeight + extraForTopOffset);
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception)
            {
                // 忽略错误
            }
        }

        private void EnsureFirstVerseHighlightedByDefault()
        {
            if (_mergedVerses == null || _mergedVerses.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _mergedVerses.Count; i++)
            {
                _mergedVerses[i].IsHighlighted = false;
            }

            _mergedVerses[0].IsHighlighted = true;
        }

        private double EstimateBibleVerseHeight()
        {
            try
            {
                if (BibleVerseList == null || BibleVerseList.Items.Count == 0)
                {
                    return 110d;
                }

                double total = 0d;
                int count = 0;
                int sampleCount = Math.Min(8, BibleVerseList.Items.Count);
                for (int i = 0; i < sampleCount; i++)
                {
                    if (BibleVerseList.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container &&
                        container.ActualHeight > 1d)
                    {
                        total += container.ActualHeight;
                        count++;
                    }
                }

                if (count > 0)
                {
                    return total / count;
                }

                if (BibleVerseScrollViewer?.ViewportHeight > 0)
                {
                    return Math.Max(80d, BibleVerseScrollViewer.ViewportHeight / 8d);
                }
            }
            catch
            {
            }

            return 110d;
        }
        
        /// <summary>
        /// 圣经内容滚动事件（同步到投影）
        /// </summary>
        private void BibleVerseContentScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 如果投影已开启且在圣经模式，同步滚动位置
            if (_isBibleMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                // 直接偏移滚动同步：主屏幕和投影屏幕使用相同的渲染逻辑，内容高度一致，直接同步滚动偏移
                double mainScrollOffset = BibleVerseScrollViewer.VerticalOffset;
                
                //#if DEBUG
                //_debugScrollCount++;
                //bool shouldDebug = (_debugScrollCount % 10 == 0);
                //
                ////// 每隔10次输出一次详细信息（减少日志量）
                ////if (shouldDebug)
                ////{
                ////    Debug.WriteLine($"");
                ////    Debug.WriteLine($"===== 圣经滚动详细调试 =====");
                ////    
                ////    // 获取主屏幕DPI
                ////    var mainDpi = VisualTreeHelper.GetDpi(BibleVerseScrollViewer);
                ////    Debug.WriteLine($" [主屏幕] DPI: {mainDpi.PixelsPerInchX} x {mainDpi.PixelsPerInchY}");
                ////    Debug.WriteLine($" [主屏幕] DPI缩放: {mainDpi.DpiScaleX:F2} x {mainDpi.DpiScaleY:F2}");
                ////    
                ////    Debug.WriteLine($"[主屏幕] 滚动偏移: {mainScrollOffset:F2} (将传给投影)");
                ////    Debug.WriteLine($"[主屏幕] 可滚动高度: {BibleVerseScrollViewer.ScrollableHeight:F2}");
                ////    Debug.WriteLine($"[主屏幕] 视口高度: {BibleVerseScrollViewer.ViewportHeight:F2}");
                ////    Debug.WriteLine($"[主屏幕] 内容总高度: {BibleVerseScrollViewer.ExtentHeight:F2}");
                ////    
                ////    if (BibleChapterTitle != null)
                ////    {
                ////        Debug.WriteLine($"[主屏幕] 标题实际高度: {BibleChapterTitle.ActualHeight:F2}");
                ////        var titleBorder = BibleChapterTitle.Parent as Border;
                ////        if (titleBorder != null)
                ////        {
                ////            Debug.WriteLine($"[主屏幕] 标题Border总高度: {titleBorder.ActualHeight:F2} (含Padding)");
                ////        }
                ////    }
                ////    
                ////    if (BibleVerseList != null)
                ////    {
                ////        Debug.WriteLine($"[主屏幕] 经文列表高度: {BibleVerseList.ActualHeight:F2}");
                ////    }
                ////    
                ////    if (BibleBottomExtension != null)
                ////    {
                ////        Debug.WriteLine($"[主屏幕] 底部扩展高度: {BibleBottomExtension.ActualHeight:F2}");
                ////    }
                ////}
                //#endif

                // 圣经滚动同步：直接使用主屏滚动位置（与歌词投影完全一致）
                // 因为两者使用相同的渲染逻辑，内容高度一致，直接同步滚动偏移
                _projectionManager.SyncBibleScroll(BibleVerseScrollViewer);

                // 滚动后在渲染阶段补发一帧 NDI，避免“投影已滚动但 NDI 还没滚动”的时序差。
                if (_bibleNdiModule?.IsEnabled() == true && _configManager?.ProjectionNdiEnabled == true)
                {
                    long now = Environment.TickCount64;
                    if (now - _lastBibleNdiScrollPublishTick < 25)
                    {
                        return;
                    }

                    _lastBibleNdiScrollPublishTick = now;
                    if (_bibleNdiScrollPublishQueued)
                    {
                        return;
                    }

                    _bibleNdiScrollPublishQueued = true;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var verses = GetCurrentBibleVersesSnapshot();
                            if (verses.Count > 0)
                            {
                                PublishBibleFrameToNdi(verses);
                            }
                        }
                        finally
                        {
                            _bibleNdiScrollPublishQueued = false;
                        }
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }
            }
        }

        private void LoadBibleCopyStyleSetting()
        {
            try
            {
                var savedValue = DatabaseManagerService.GetUISetting(BibleCopyHeaderStyleSettingKey, "Short");
                _bibleCopyHeaderStyle = savedValue switch
                {
                    "Full" => BibleCopyHeaderStyle.Full,
                    "Chapter" => BibleCopyHeaderStyle.Chapter,
                    _ => BibleCopyHeaderStyle.Short
                };
            }
            catch
            {
                _bibleCopyHeaderStyle = BibleCopyHeaderStyle.Short;
            }

            UpdateBibleCopyStyleMenuChecks();
        }

        private void SaveBibleCopyStyleSetting()
        {
            try
            {
                DatabaseManagerService.SaveUISetting(BibleCopyHeaderStyleSettingKey, _bibleCopyHeaderStyle.ToString());
            }
            catch
            {
            }
        }

        private void UpdateBibleCopyStyleMenuChecks()
        {
            if (MenuBibleCopyStyleShort != null)
                MenuBibleCopyStyleShort.IsChecked = _bibleCopyHeaderStyle == BibleCopyHeaderStyle.Short;
            if (MenuBibleCopyStyleFull != null)
                MenuBibleCopyStyleFull.IsChecked = _bibleCopyHeaderStyle == BibleCopyHeaderStyle.Full;
            if (MenuBibleCopyStyleChapter != null)
                MenuBibleCopyStyleChapter.IsChecked = _bibleCopyHeaderStyle == BibleCopyHeaderStyle.Chapter;
        }

        /// <summary>
        /// 右键清屏
        /// </summary>
        private void ClearBibleVerses_Click(object sender, RoutedEventArgs e)
        {
            ClearBibleScreenFromContextMenu();
        }

        /// <summary>
        /// 右键复制经文（固定格式）
        /// [约翰福音3:16-18]
        /// 16 经文...
        /// 17 经文...
        /// </summary>
        private void CopyBibleVerses_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mergedVerses == null || _mergedVerses.Count == 0)
                {
                    ShowStatus("当前没有可复制的经文");
                    return;
                }

                // 始终复制当前显示的全部经文
                var verses = _mergedVerses.ToList();

                if (verses.Count == 0)
                {
                    ShowStatus("当前没有可复制的经文");
                    return;
                }

                verses = verses.OrderBy(v => v.Verse).ToList();

                var first = verses.First();
                var last = verses.Last();
                var bookConfig = BibleBookConfig.GetBook(first.Book);
                string shortName = bookConfig?.ShortName ?? first.BookName;

                string header = _bibleCopyHeaderStyle switch
                {
                    BibleCopyHeaderStyle.Full => first.Verse == last.Verse
                        ? $"[{first.BookName}{first.Chapter}:{first.Verse}]"
                        : $"[{first.BookName}{first.Chapter}:{first.Verse}-{last.Verse}]",
                    BibleCopyHeaderStyle.Chapter => first.Verse == last.Verse
                        ? $"[{first.BookName}{first.Chapter}章{first.Verse}节]"
                        : $"[{first.BookName}{first.Chapter}章{first.Verse}-{last.Verse}节]",
                    _ => first.Verse == last.Verse
                        ? $"[{shortName}{first.Chapter}:{first.Verse}]"
                        : $"[{shortName}{first.Chapter}:{first.Verse}-{last.Verse}]"
                };

                var lines = new List<string> { header };
                foreach (var verse in verses)
                {
                    var number = string.IsNullOrWhiteSpace(verse.DisplayVerseNumber)
                        ? verse.Verse.ToString()
                        : verse.DisplayVerseNumber;
                    lines.Add($"{number} {verse.Scripture}".Trim());
                }

                var text = string.Join(Environment.NewLine, lines);
                System.Windows.Clipboard.SetText(text);
                ShowStatus("经文已复制");
            }
            catch (Exception ex)
            {
                ShowStatus($"复制经文失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置圣经复制样式
        /// </summary>
        private void SetBibleCopyStyle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string tag)
            {
                return;
            }

            _bibleCopyHeaderStyle = tag switch
            {
                "Full" => BibleCopyHeaderStyle.Full,
                "Chapter" => BibleCopyHeaderStyle.Chapter,
                _ => BibleCopyHeaderStyle.Short
            };

            UpdateBibleCopyStyleMenuChecks();
            SaveBibleCopyStyleSetting();

            ShowStatus("复制样式已切换");
        }
        
//#if DEBUG
//        private int _debugScrollCount = 0;
//#endif

        #endregion

    }
}



