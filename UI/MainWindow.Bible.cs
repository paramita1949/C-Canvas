using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private ObservableCollection<BibleHistoryItem> _historySlots = new ObservableCollection<BibleHistoryItem>(); // 10个历史槽位

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
                _bibleService = App.GetRequiredService<IBibleService>();

                #if DEBUG
                Debug.WriteLine("[圣经] 服务初始化成功");
                #endif

                // 检查数据库是否可用
                Task.Run(async () =>
                {
                    var available = await _bibleService.IsDatabaseAvailableAsync();

                    #if DEBUG
                    Debug.WriteLine($"[圣经] 数据库可用: {available}");
                    #endif

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
                #if DEBUG
                Debug.WriteLine($"[圣经] 服务初始化失败: {ex.Message}");
                #endif

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
        /// 圣经按钮点击事件
        /// </summary>
        private async void BtnShowBible_Click(object sender, RoutedEventArgs e)
        {
            #if DEBUG
            Debug.WriteLine($"[圣经] 切换到圣经视图, 当前模式: {_currentViewMode}, 圣经模式: {_isBibleMode}");
            #endif

            _isBibleMode = true;
            _currentViewMode = NavigationViewMode.Bible;  // 设置当前视图模式为圣经

            #if DEBUG
            Debug.WriteLine($"[圣经] 开始切换UI, ProjectTree当前可见性: {ProjectTree.Visibility}");
            #endif

            // 清空图片显示（包括合成播放按钮）
            ClearImageDisplay();

            // 隐藏ProjectTree，显示圣经导航面板
            ProjectTree.Visibility = Visibility.Collapsed;
            BibleNavigationPanel.Visibility = Visibility.Visible;

            #if DEBUG
            Debug.WriteLine($"[圣经] 导航切换完成, ProjectTree={ProjectTree.Visibility}, BiblePanel={BibleNavigationPanel.Visibility}");
            
            // 🔍 打印导航栏宽度信息（异步调试输出，不需要等待）
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (NavigationPanelColumn != null)
                {
                    Debug.WriteLine($"");
                    Debug.WriteLine($"🔍 ===== 圣经导航栏宽度信息 =====");
                    Debug.WriteLine($"📐 [导航栏] 设定宽度: {NavigationPanelColumn.Width}");
                    Debug.WriteLine($"📐 [导航栏] 实际宽度: {NavigationPanelColumn.ActualWidth:F2}");
                }
                
                if (BibleNavigationPanel != null)
                {
                    Debug.WriteLine($"📐 [圣经面板] 实际宽度: {BibleNavigationPanel.ActualWidth:F2}");
                }
                
                // 打印5列的宽度设置
                Debug.WriteLine($"📊 [表格列宽] 第1列(分类): 70");
                Debug.WriteLine($"📊 [表格列宽] 第2列(书卷): 120");
                Debug.WriteLine($"📊 [表格列宽] 第3列(章): 60");
                Debug.WriteLine($"📊 [表格列宽] 第4列(起始节): 60");
                Debug.WriteLine($"📊 [表格列宽] 第5列(结束节): 60");
                Debug.WriteLine($"📊 [表格列宽] 总计: 370");
                Debug.WriteLine($"⚠️  [结论] 导航栏宽度需要390以上才能完整显示5列！");
                Debug.WriteLine($"");
            }, System.Windows.Threading.DispatcherPriority.Loaded);
            #endif

            // 加载圣经数据
            await LoadBibleNavigationDataAsync();

            // 显示圣经视图区域，隐藏其他区域
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            VideoContainer.Visibility = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Collapsed;
            BibleVerseScrollViewer.Visibility = Visibility.Visible;

            #if DEBUG
            Debug.WriteLine($"[圣经] 圣经视图已显示, ImageScroll={ImageScrollViewer.Visibility}, BibleVerse={BibleVerseScrollViewer.Visibility}");
            #endif

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

            #if DEBUG
            Debug.WriteLine($"[圣经] 节点点击: {node.Name}, Type={node.Type}, Tag={node.Tag}");
            #endif

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
        /// </summary>
        private Task LoadBibleNavigationDataAsync()
        {
            try
            {
                #if DEBUG
                var sw = Stopwatch.StartNew();
                #endif

                // 初始化10个历史槽位
                InitializeHistorySlots();
                BibleHistoryList.ItemsSource = _historySlots;

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

                #if DEBUG
                sw.Stop();
                Debug.WriteLine($"[圣经] 导航数据加载完成: {sw.ElapsedMilliseconds}ms, 分类数: {categories.Count}");
                #endif

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 加载导航数据失败: {ex.Message}");
                #endif

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
                #if DEBUG
                var sw = Stopwatch.StartNew();
                #endif

                _currentBook = book;
                _currentChapter = chapter;
                _currentVerse = 1;

                var verses = await _bibleService.GetChapterVersesAsync(book, chapter);
                var bookInfo = BibleBookConfig.GetBook(book);
                
                BibleChapterTitle.Text = $"{bookInfo?.Name}{chapter}章";
                
                // 先隐藏列表，避免显示默认样式的闪烁
                BibleVerseList.Visibility = Visibility.Collapsed;
                BibleVerseList.ItemsSource = verses;
                
                // 重置滚动条到顶部
                BibleVerseScrollViewer.ScrollToTop();

                // 延迟应用样式并显示列表（等待ItemsControl生成容器）
                _ = Dispatcher.InvokeAsync(() =>
                {
                    ApplyBibleSettings();
                    BibleVerseList.Visibility = Visibility.Visible;
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                #if DEBUG
                sw.Stop();
                Debug.WriteLine($"[圣经] 加载章节 {book}:{chapter}, 耗时: {sw.ElapsedMilliseconds}ms, 经文数: {verses.Count}");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 加载章节失败: {ex.Message}");
                #endif

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

            #if DEBUG
            Debug.WriteLine($"[圣经] 选中分类: {category}");
            #endif

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

            #if DEBUG
            Debug.WriteLine($"[圣经] 加载了 {bookList.Count} 卷书，已清空选择");
            #endif
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

            #if DEBUG
            Debug.WriteLine($"[圣经] 选中书卷: {book.Name} (BookId={book.BookId})");
            #endif

            // 生成章列表到第3列
            var chapters = Enumerable.Range(1, book.ChapterCount).Select(c => $"{c}").ToList();
            BibleChapterList.ItemsSource = chapters;
            BibleChapterList.Tag = book.BookId; // 保存BookId供后续使用
            
            // 清空章节选择和起始/结束节列表
            BibleChapterList.SelectedIndex = -1;
            BibleStartVerse.ItemsSource = null;
            BibleEndVerse.ItemsSource = null;

            #if DEBUG
            Debug.WriteLine($"[圣经] 加载了 {chapters.Count} 章，已清空章节和节号选择");
            #endif
        }

        // 第3列:章选择事件（单击只加载节号列表，不显示经文）
        private async void BibleChapter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BibleChapterList.SelectedItem is not string chapterStr)
                return;

            if (!int.TryParse(chapterStr, out int chapter))
                return;

            if (BibleChapterList.Tag is not int bookId)
                return;

            #if DEBUG
            Debug.WriteLine($"[圣经] 选中章: BookId={bookId}, Chapter={chapter}");
            #endif

            // 查询该章的节数
            var verses = await _bibleService.GetChapterVersesAsync(bookId, chapter);
            int verseCount = verses?.Count ?? 0;
            
            if (verseCount > 0)
            {
                // 生成节号列表 1, 2, 3, ... verseCount
                var verseNumbers = Enumerable.Range(1, verseCount).Select(v => v.ToString()).ToList();
                
                BibleStartVerse.ItemsSource = verseNumbers;
                BibleEndVerse.ItemsSource = verseNumbers;
                
                // 清空起始节和结束节选择，要求用户手动选择
                BibleStartVerse.SelectedIndex = -1;
                BibleEndVerse.SelectedIndex = -1;
                
                // 清空经文显示
                BibleVerseList.ItemsSource = null;
                BibleChapterTitle.Text = "";

                #if DEBUG
                Debug.WriteLine($"[圣经] 已加载节号列表 1-{verseCount}，等待用户选择节范围");
                #endif
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

            #if DEBUG
            Debug.WriteLine($"[圣经] 双击章: BookId={bookId}, Chapter={chapter}，加载整章");
            #endif

            // 加载整章经文
            await LoadChapterVersesAsync(bookId, chapter);

            // 更新起始节和结束节的下拉列表
            var verses = BibleVerseList.ItemsSource as List<BibleVerse>;
            int verseCount = verses?.Count ?? 0;
            
            if (verseCount > 0)
            {
                // 生成节号列表 1, 2, 3, ... verseCount
                var verseNumbers = Enumerable.Range(1, verseCount).Select(v => v.ToString()).ToList();
                
                BibleStartVerse.ItemsSource = verseNumbers;
                BibleEndVerse.ItemsSource = verseNumbers;
                
                // 默认选中第1节和最后一节
                BibleStartVerse.SelectedIndex = 0;
                BibleEndVerse.SelectedIndex = verseCount - 1;

                #if DEBUG
                Debug.WriteLine($"[圣经] 双击加载整章，节范围: 1-{verseCount}");
                #endif
            }
        }

        // 第4列:起始节选择事件
        private async void BibleStartVerse_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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

            #if DEBUG
            Debug.WriteLine($"[圣经] 起始节改变: {startVerse}-{endVerse}");
            #endif

            // 重新加载指定范围的经文
            await LoadVerseRangeAsync(bookId, chapter, startVerse, endVerse);

            // 注意：不在这里添加历史记录，避免重复添加（在结束节改变时统一添加）
        }

        // 第5列:结束节选择事件
        private async void BibleEndVerse_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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

            #if DEBUG
            Debug.WriteLine($"[圣经] 结束节改变: {startVerse}-{endVerse}");
            #endif

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
            try
            {
                _currentBook = bookId;
                _currentChapter = chapter;
                _currentVerse = startVerse;

                var allVerses = await _bibleService.GetChapterVersesAsync(bookId, chapter);
                var verses = allVerses.Where(v => v.Verse >= startVerse && v.Verse <= endVerse).ToList();

                var book = BibleBookConfig.GetBook(bookId);
                BibleChapterTitle.Text = $"{book?.Name}{chapter}章 {startVerse}-{endVerse}节";
                
                #if DEBUG
                // 检查创世记1:26是否完整
                if (bookId == 1 && chapter == 1)
                {
                    var verse26 = verses.FirstOrDefault(v => v.Verse == 26);
                    if (verse26 != null)
                    {
                        Debug.WriteLine($"");
                        Debug.WriteLine($"🔍 [经文完整性检查] 创世记1:26");
                        Debug.WriteLine($"   经文内容: {verse26.Scripture}");
                        Debug.WriteLine($"   字符长度: {verse26.Scripture?.Length}");
                        Debug.WriteLine($"   应包含: '并地上所爬的一切昆虫' - {(verse26.Scripture?.Contains("并地上所爬的一切昆虫") == true ? "✅存在" : "❌缺失")}");
                        Debug.WriteLine($"");
                    }
                }
                #endif
                
                // 先隐藏列表，避免显示默认样式的闪烁
                BibleVerseList.Visibility = Visibility.Collapsed;
                BibleVerseList.ItemsSource = verses;
                
                // 重置滚动条到顶部
                BibleVerseScrollViewer.ScrollToTop();

                // 延迟应用样式并显示列表（等待ItemsControl生成容器）
                _ = Dispatcher.InvokeAsync(() =>
                {
                    ApplyBibleSettings();
                    BibleVerseList.Visibility = Visibility.Visible;
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                #if DEBUG
                Debug.WriteLine($"[圣经] 加载经文范围: {book?.Name} {chapter}:{startVerse}-{endVerse}, 共 {verses.Count} 节");
                
                // 🔍 输出主屏幕的字体参数（等待UI渲染完成后）
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (BibleVerseList.Items.Count > 0)
                    {
                        Debug.WriteLine($"🔍 [主屏幕] 标题字体: FontSize={BibleChapterTitle.FontSize}, Padding={(BibleChapterTitle.Parent as Border)?.Padding}");
                        
                        var firstItem = BibleVerseList.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                        if (firstItem != null)
                        {
                            // 查找Border的Padding
                            var border = FindVisualChild<Border>(firstItem);
                            if (border != null)
                            {
                                Debug.WriteLine($"🔍 [主屏幕] 经文Border: Padding={border.Padding}");
                            }
                            
                            // 查找经文TextBlock
                            var verseTexts = FindVisualChildren<TextBlock>(firstItem).ToList();
                            if (verseTexts.Count >= 2)
                            {
                                var numberText = verseTexts[0]; // 节号
                                var scriptureText = verseTexts[1]; // 经文
                                Debug.WriteLine($"🔍 [主屏幕] 节号字体: FontSize={numberText.FontSize}, FontWeight={numberText.FontWeight}, Margin={numberText.Margin}");
                                Debug.WriteLine($"🔍 [主屏幕] 经文字体: FontSize={scriptureText.FontSize}, LineHeight={scriptureText.LineHeight}, TextWrapping={scriptureText.TextWrapping}");
                            }
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                #endif
                
                // 🔧 设置主屏幕底部扩展空间（等于视口高度,支持底部内容向上拉）
                UpdateMainScreenBottomExtension();
                
                // 🆕 如果投影已开启，自动更新投影
                if (_isBibleMode && _projectionManager != null && _projectionManager.IsProjecting)
                {
#if DEBUG
                    Debug.WriteLine("[圣经] 检测到投影开启，自动更新投影内容");
#endif
                    RenderBibleToProjection();
                }
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经] 加载经文范围失败: {ex.Message}");
            }
#else
            catch (Exception)
            {
            }
#endif
        }

        /// <summary>
        /// 自动保存到勾选的槽位（只更新勾选的槽位，不创建新记录）
        /// </summary>
        private void AddToHistory(int bookId, int chapter, int startVerse, int endVerse)
        {
            try
            {
                var book = BibleBookConfig.GetBook(bookId);
                string displayText = $"{book?.Name}{chapter}章{startVerse}-{endVerse}节";

                // 找到所有勾选的槽位
                var checkedSlots = _historySlots.Where(s => s.IsChecked).ToList();

                if (checkedSlots.Count == 0)
                {
                    #if DEBUG
                    Debug.WriteLine("[圣经] 没有勾选任何槽位，不保存");
                    #endif
                    return;
                }

                // 只更新勾选的槽位（可能有多个）
                foreach (var slot in checkedSlots)
                {
                    slot.BookId = bookId;
                    slot.Chapter = chapter;
                    slot.StartVerse = startVerse;
                    slot.EndVerse = endVerse;
                    slot.DisplayText = displayText;

                    #if DEBUG
                    Debug.WriteLine($"[圣经] 更新槽位{slot.Index}: {displayText}");
                    #endif
                }

                // 刷新列表显示
                BibleHistoryList.Items.Refresh();
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经] 保存到历史槽位失败: {ex.Message}");
            }
#else
            catch (Exception)
            {
            }
#endif
        }


        /// <summary>
        /// 加载单节经文
        /// </summary>
        private async Task<BibleVerse> LoadVerseAsync(int book, int chapter, int verse)
        {
            try
            {
                var verseData = await _bibleService.GetVerseAsync(book, chapter, verse);

                #if DEBUG
                Debug.WriteLine($"[圣经] 加载经文: {verseData?.Reference} - {verseData?.Scripture}");
                #endif

                return verseData;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经] 加载经文失败: {ex.Message}");
                return null;
            }
#else
            catch (Exception)
            {
                return null;
            }
#endif
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
                        
                        #if DEBUG
                        Debug.WriteLine($"🔧 [主屏扩展] 设置底部扩展高度: {viewportHeight:F2}");
                        Debug.WriteLine($"🔧 [主屏扩展] 说明: 主屏幕和投影的底部扩展高度必须一致(=屏幕/视口高度)，以确保顶部对齐");
                        #endif
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
                
                #if DEBUG
                _debugScrollCount++;
                bool shouldDebug = (_debugScrollCount % 10 == 0);
                
                // 每隔10次输出一次详细信息（减少日志量）
                if (shouldDebug)
                {
                    Debug.WriteLine($"");
                    Debug.WriteLine($"🔍 ===== 圣经滚动详细调试 =====");
                    
                    // 获取主屏幕DPI
                    var mainDpi = VisualTreeHelper.GetDpi(BibleVerseScrollViewer);
                    Debug.WriteLine($"📐 [主屏幕] DPI: {mainDpi.PixelsPerInchX} x {mainDpi.PixelsPerInchY}");
                    Debug.WriteLine($"📐 [主屏幕] DPI缩放: {mainDpi.DpiScaleX:F2} x {mainDpi.DpiScaleY:F2}");
                    
                    Debug.WriteLine($"📊 [主屏幕] 滚动偏移: {mainScrollOffset:F2} (将传给投影)");
                    Debug.WriteLine($"📊 [主屏幕] 可滚动高度: {BibleVerseScrollViewer.ScrollableHeight:F2}");
                    Debug.WriteLine($"📊 [主屏幕] 视口高度: {BibleVerseScrollViewer.ViewportHeight:F2}");
                    Debug.WriteLine($"📊 [主屏幕] 内容总高度: {BibleVerseScrollViewer.ExtentHeight:F2}");
                    
                    if (BibleChapterTitle != null)
                    {
                        Debug.WriteLine($"📊 [主屏幕] 标题实际高度: {BibleChapterTitle.ActualHeight:F2}");
                        var titleBorder = BibleChapterTitle.Parent as Border;
                        if (titleBorder != null)
                        {
                            Debug.WriteLine($"📊 [主屏幕] 标题Border总高度: {titleBorder.ActualHeight:F2} (含Padding)");
                        }
                    }
                    
                    if (BibleVerseList != null)
                    {
                        Debug.WriteLine($"📊 [主屏幕] 经文列表高度: {BibleVerseList.ActualHeight:F2}");
                    }
                    
                    if (BibleBottomExtension != null)
                    {
                        Debug.WriteLine($"📊 [主屏幕] 底部扩展高度: {BibleBottomExtension.ActualHeight:F2}");
                    }
                }
                #endif

                // 🔧 圣经滚动同步：直接使用主屏滚动位置（与歌词投影完全一致）
                // 因为两者使用相同的渲染逻辑，内容高度一致，直接同步滚动偏移
                _projectionManager.SyncBibleScroll(BibleVerseScrollViewer);
            }
        }
        
#if DEBUG
        private int _debugScrollCount = 0;
#endif

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
                #if DEBUG
                Debug.WriteLine("[圣经] 已经是第一节");
                #endif
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
                    #if DEBUG
                    Debug.WriteLine("[圣经] 已经是最后一节");
                    #endif
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
                #if DEBUG
                Debug.WriteLine($"[圣经] 导航到: {verse.Reference}");
                #endif

                // 自动投影
                await ProjectBibleVerseAsync(verse);
            }
        }

        #endregion

        #region 圣经投影

        /// <summary>
        /// 投影当前经文
        /// </summary>
        private async Task ProjectBibleVerseAsync(BibleVerse verse)
        {
            if (verse == null)
                return;

            try
            {
                #if DEBUG
                var sw = Stopwatch.StartNew();
                #endif

                // 渲染经文到投影屏幕
                var skBitmap = RenderVerseToProjection(verse);
                if (skBitmap != null)
                {
                    _projectionManager?.UpdateProjectionText(skBitmap);
                    skBitmap.Dispose();

                    #if DEBUG
                    sw.Stop();
                    Debug.WriteLine($"[圣经] 投影经文成功: {verse.Reference}, 耗时: {sw.ElapsedMilliseconds}ms");
                    #endif
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 投影失败: {ex.Message}");
                #endif

                WpfMessageBox.Show(
                    $"投影失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 投影经文范围（多节）
        /// </summary>
        private async Task ProjectBibleVerseRangeAsync(int bookId, int chapter, int startVerse, int endVerse)
        {
            try
            {
                #if DEBUG
                var sw = Stopwatch.StartNew();
                Debug.WriteLine($"[圣经] 开始投影范围: {bookId} {chapter}:{startVerse}-{endVerse}");
                #endif

                // 加载经文范围
                var verses = new List<BibleVerse>();
                for (int verse = startVerse; verse <= endVerse; verse++)
                {
                    var verseData = await _bibleService.GetVerseAsync(bookId, chapter, verse);
                    if (verseData != null)
                    {
                        verses.Add(verseData);
                    }
                }

                if (verses.Count == 0)
                {
                    #if DEBUG
                    Debug.WriteLine($"[圣经] 没有加载到任何经文");
                    #endif
                    return;
                }

                // 渲染多节经文到投影
                var skBitmap = RenderVersesToProjection(verses);
                if (skBitmap != null)
                {
                    _projectionManager?.UpdateProjectionText(skBitmap);
                    skBitmap.Dispose();

                    #if DEBUG
                    sw.Stop();
                    Debug.WriteLine($"[圣经] 投影范围成功: {verses.Count}节, 耗时: {sw.ElapsedMilliseconds}ms");
                    #endif
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 投影范围失败: {ex.Message}");
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
        /// 渲染多节经文到投影屏幕（标题通过固定层显示，内容可滚动）
        /// </summary>
        /// <summary>
        /// 渲染圣经经文到投影（完全按照歌词投影的方式）
        /// </summary>
        private SKBitmap RenderVersesToProjection(List<BibleVerse> verses)
        {
            if (verses == null || verses.Count == 0)
                return null;

            try
            {
                // 🔧 获取投影屏幕的实际尺寸（考虑DPI缩放）
                var (screenWidth, screenHeight) = _projectionManager.GetProjectionScreenSize();

                #if DEBUG
                Debug.WriteLine($"📐 [圣经渲染] 投影屏幕实际尺寸: {screenWidth}x{screenHeight}");
                Debug.WriteLine($"📐 [圣经渲染] 经文数量: {verses.Count}");
                #endif

                // 从配置中获取样式设置
                var backgroundColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleBackgroundColor);
                var titleColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleTitleColor);
                var textColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleTextColor);
                var verseNumberColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleVerseNumberColor);
                var fontFamily = new WpfFontFamily(_configManager.BibleFontFamily);

                // 创建Canvas容器
                var canvas = new Canvas
                {
                    Width = screenWidth,
                    Height = screenHeight, // 先设置屏幕高度，后续会根据内容调整
                    Background = new WpfSolidColorBrush(backgroundColor)
                };

                double actualHeight = screenHeight;

                // 🔧 获取章节标题文本
                string chapterTitle = "";
                if (Dispatcher.CheckAccess())
                {
                    chapterTitle = BibleChapterTitle.Text;
                }
                else
                {
                    chapterTitle = Dispatcher.Invoke(() => BibleChapterTitle.Text);
                }
                
                // 创建内容容器（包含标题和经文）
                var mainStackPanel = new StackPanel
                {
                    Width = screenWidth,
                    Orientation = System.Windows.Controls.Orientation.Vertical
                };

                // 1. 添加章节标题
                var titleBorder = new Border
                {
                    Width = screenWidth,
                    Background = new WpfSolidColorBrush(WpfColor.FromRgb(28, 28, 28)), // #1C1C1C
                    Padding = new Thickness(20, 15, 20, 15)
                };
                
                var titleText = new TextBlock
                {
                    Text = chapterTitle,
                    FontFamily = fontFamily,
                    FontSize = _configManager.BibleTitleFontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = new WpfSolidColorBrush(titleColor)
                };
                
                titleBorder.Child = titleText;
                mainStackPanel.Children.Add(titleBorder);

                // 2. 添加顶部边距
                var topPadding = new Border
                {
                    Height = 20,
                    Width = screenWidth
                };
                mainStackPanel.Children.Add(topPadding);

                // 3. 渲染每一节经文
                foreach (var verse in verses)
                {
                    var verseBorder = new Border
                    {
                        Background = WpfBrushes.Transparent,
                        Margin = new Thickness(0, _configManager.BibleVerseSpacing / 2, 0, _configManager.BibleVerseSpacing / 2),
                        Padding = new Thickness(2)
                    };
                    
                    // 使用 Grid 布局替代 StackPanel，确保经文可以换行
                    var verseContainer = new Grid
                    {
                        Margin = new Thickness(_configManager.BibleMargin, 0, _configManager.BibleMargin, 0)
                    };
                    
                    // 定义两列：节号列（自动宽度）和经文列（填充剩余空间）
                    verseContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    verseContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var verseNumber = new TextBlock
                    {
                        Text = $"{verse.Verse}",
                        FontFamily = fontFamily,
                        FontSize = _configManager.BibleVerseNumberFontSize,
                        FontWeight = FontWeights.Bold,
                        Foreground = new WpfSolidColorBrush(verseNumberColor),
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    Grid.SetColumn(verseNumber, 0);

                    var scriptureText = new TextBlock
                    {
                        Text = verse.Scripture,
                        FontFamily = fontFamily,
                        FontSize = _configManager.BibleFontSize,
                        FontWeight = FontWeights.Normal,
                        Foreground = new WpfSolidColorBrush(textColor),
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    Grid.SetColumn(scriptureText, 1);

                    verseContainer.Children.Add(verseNumber);
                    verseContainer.Children.Add(scriptureText);
                    verseBorder.Child = verseContainer;
                    mainStackPanel.Children.Add(verseBorder);
                }

                // 4. 添加底部边距
                var bottomPadding = new Border
                {
                    Height = 20,
                    Width = screenWidth
                };
                mainStackPanel.Children.Add(bottomPadding);

                // 🔧 5. 添加底部扩展空间（与主屏幕一致，支持底部内容向上拉）
                // 扩展高度 = 屏幕高度，使得最后一节经文可以滚动到顶部
                var bottomExtension = new Border
                {
                    Height = screenHeight,
                    Width = screenWidth,
                    Background = WpfBrushes.Transparent
                };
                mainStackPanel.Children.Add(bottomExtension);

                // 6. 测量内容实际高度（包含底部扩展）
                mainStackPanel.Measure(new WpfSize(screenWidth, double.PositiveInfinity));
                double contentHeight = mainStackPanel.DesiredSize.Height;

                #if DEBUG
                Debug.WriteLine($"📐 [圣经渲染] 内容实际高度: {contentHeight:F2}, 屏幕高度: {screenHeight:F2}");
                Debug.WriteLine($"📐 [圣经渲染] 底部扩展高度: {screenHeight:F2} (与主屏幕一致)");
                #endif

                // 7. 如果内容超过屏幕高度，调整Canvas高度（与歌词完全一致）
                if (contentHeight > screenHeight)
                {
                    actualHeight = contentHeight;
                    canvas.Height = actualHeight;
                    #if DEBUG
                    Debug.WriteLine($"📐 [圣经渲染] 内容超出屏幕，Canvas高度调整为: {actualHeight:F2}");
                    #endif
                }

                // 8. 将内容添加到Canvas
                Canvas.SetLeft(mainStackPanel, 0);
                Canvas.SetTop(mainStackPanel, 0);
                canvas.Children.Add(mainStackPanel);

                // 9. 渲染到图片（固定使用96 DPI，确保逻辑像素=物理像素）
                canvas.Measure(new WpfSize(screenWidth, actualHeight));
                canvas.Arrange(new Rect(0, 0, screenWidth, actualHeight));
                canvas.UpdateLayout();

                #if DEBUG
                Debug.WriteLine($"📐 [圣经渲染] Canvas最终尺寸: {screenWidth:F0}x{actualHeight:F0}");
                Debug.WriteLine($"📐 [圣经渲染] 使用DPI: 96x96 (固定，确保像素对齐)");
                #endif

                // 🔧 关键：固定使用96 DPI，确保渲染的图片逻辑像素=物理像素
                // 如果使用高DPI（如192），WPF会按DPI缩放显示，导致滚动不对齐
                var renderBitmap = new RenderTargetBitmap(
                    (int)screenWidth, (int)Math.Ceiling(actualHeight), 96, 96, WpfPixelFormats.Pbgra32);
                renderBitmap.Render(canvas);
                renderBitmap.Freeze();

                // 转换为SKBitmap并返回
                var skBitmap = ConvertToSKBitmap(renderBitmap);
                
                #if DEBUG
                Debug.WriteLine($"📐 [圣经渲染] SKBitmap转换结果: {skBitmap != null}, 尺寸: {skBitmap?.Width}x{skBitmap?.Height}");
                #endif

                return skBitmap;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经] 渲染失败: {ex.Message}");
                return null;
            }
#else
            catch (Exception)
            {
                return null;
            }
#endif
        }

        // ConvertToSKBitmap方法已在MainWindow.Lyrics.cs中定义，此处复用

        /// <summary>
        /// 渲染圣经经文到投影（参考歌词渲染逻辑）
        /// </summary>
        private void RenderBibleToProjection()
        {
#if DEBUG
            Debug.WriteLine($"[圣经] 开始渲染投影 - 经文数量: {BibleVerseList.Items.Count}");
#endif

            try
            {
                // 如果没有经文，不投影
                if (BibleVerseList.ItemsSource == null || BibleVerseList.Items.Count == 0)
                {
#if DEBUG
                    Debug.WriteLine("[圣经] 没有经文可投影");
#endif
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
#if DEBUG
                    Debug.WriteLine("[圣经] 没有有效的经文数据");
#endif
                    return;
                }

                // 🔧 使用统一的渲染方法
                var skBitmap = RenderVersesToProjection(versesList);
                if (skBitmap != null)
                {
                    _projectionManager?.UpdateProjectionText(skBitmap);
                    skBitmap.Dispose();

#if DEBUG
                    Debug.WriteLine($"[圣经] 投影渲染完成，共{versesList.Count}节");
#endif
                }
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经] 渲染投影失败: {ex.Message}\n{ex.StackTrace}");
            }
#else
            catch (Exception)
            {
            }
#endif
        }

        /// <summary>
        /// 投影状态改变时的回调（供主窗口调用）
        /// 当投影开启时，如果在圣经模式，自动投影圣经
        /// </summary>
        public void OnBibleProjectionStateChanged(bool isProjecting)
        {
#if DEBUG
            Debug.WriteLine($"[圣经] 投影状态改变 - IsProjecting: {isProjecting}, _isBibleMode: {_isBibleMode}");
#endif

            if (isProjecting && _isBibleMode)
            {
#if DEBUG
                Debug.WriteLine("[圣经] 投影开启且在圣经模式，触发投影");
#endif
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
#if DEBUG
                    Debug.WriteLine("[圣经] 延迟后开始投影圣经");
#endif
                    RenderBibleToProjection();
                };
                timer.Start();
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
                #if DEBUG
                var sw = Stopwatch.StartNew();
                #endif

                var results = await _bibleService.SearchVersesAsync(keyword);

                #if DEBUG
                sw.Stop();
                Debug.WriteLine($"[圣经] 搜索 '{keyword}': {sw.ElapsedMilliseconds}ms, 结果数: {results.Count}");
                #endif

                // TODO: 显示搜索结果
                // ShowBibleSearchResults(results);
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 搜索失败: {ex.Message}");
                #endif

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
        /// 初始化历史槽位（1-10号）
        /// </summary>
        private void InitializeHistorySlots()
        {
            _historySlots.Clear();
            
            // 创建10个空槽位
            for (int i = 1; i <= 10; i++)
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
            
            #if DEBUG
            Debug.WriteLine("[圣经] 初始化10个历史槽位，默认勾选槽位1");
            #endif
        }

        /// <summary>
        /// 历史记录列表选择事件
        /// </summary>
        private async void BibleHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BibleHistoryList.SelectedItem is BibleHistoryItem item && item.BookId > 0)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 点击槽位{item.Index}: {item.DisplayText}");
                #endif

                // 加载该槽位的经文
                await LoadVerseRangeAsync(item.BookId, item.Chapter, item.StartVerse, item.EndVerse);

                // 🔧 如果投影已开启，自动投影该范围的经文
                if (_projectionManager != null && _projectionManager.IsProjecting)
                {
                    await ProjectBibleVerseRangeAsync(item.BookId, item.Chapter, item.StartVerse, item.EndVerse);
                }
            }
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

            #if DEBUG
            Debug.WriteLine("[圣经] 全选历史槽位");
            #endif
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

            #if DEBUG
            Debug.WriteLine("[圣经] 全不选历史槽位");
            #endif
        }

        /// <summary>
        /// 清空勾选的历史记录
        /// </summary>
        private void BtnHistoryClearSelected_Click(object sender, RoutedEventArgs e)
        {
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
            Debug.WriteLine($"[圣经] 清除了 {checkedItems.Count} 个勾选的槽位");
            #endif
        }

        #endregion

        #region 圣经设置


        /// <summary>
        /// 圣经导航面板设置按钮点击事件（悬浮在按钮右侧）
        /// </summary>
        private void BtnBibleSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 创建设置窗口，传递回调函数以实现实时更新
                var settingsWindow = new BibleSettingsWindow(_configManager, () =>
                {
                    // 设置改变时立即应用
                    ApplyBibleSettings();

                    // 如果投影已开启，重新渲染投影
                    if (_projectionManager != null && _projectionManager.IsProjecting)
                    {
                        RenderBibleToProjection();
                    }
                })
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                // 优先使用保存的窗口位置，如果没有则自动计算
                if (_configManager.BibleSettingsWindowLeft.HasValue && _configManager.BibleSettingsWindowTop.HasValue)
                {
                    // 使用保存的位置
                    settingsWindow.Left = _configManager.BibleSettingsWindowLeft.Value;
                    settingsWindow.Top = _configManager.BibleSettingsWindowTop.Value;
                    
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 使用保存的位置: Left={settingsWindow.Left}, Top={settingsWindow.Top}");
                    #endif
                }
                else if (BibleNavigationPanel != null)
                {
                    // 获取面板左上角和右上角的屏幕坐标
                    var panelTopLeft = BibleNavigationPanel.PointToScreen(new System.Windows.Point(0, 0));
                    var panelTopRight = BibleNavigationPanel.PointToScreen(
                        new System.Windows.Point(BibleNavigationPanel.ActualWidth, 0));
                    
                    // 获取屏幕工作区域
                    var screen = System.Windows.Forms.Screen.FromPoint(
                        new System.Drawing.Point((int)panelTopLeft.X, (int)panelTopLeft.Y));
                    var workingArea = screen.WorkingArea;
                    
                    // 计算窗口位置：
                    // 水平：面板右边缘内侧，留出35像素边距
                    // 垂直：面板顶部向下7像素
                    double windowLeft = panelTopRight.X - settingsWindow.Width - 35;
                    double windowTop = panelTopLeft.Y + 7;
                    
                    // 确保窗口不超出屏幕左边界
                    if (windowLeft < workingArea.Left)
                    {
                        windowLeft = workingArea.Left + 10;
                    }
                    
                    // 确保窗口不超出屏幕下边界
                    if (windowTop + settingsWindow.Height > workingArea.Bottom)
                    {
                        windowTop = workingArea.Bottom - settingsWindow.Height - 10;
                    }
                    
                    // 确保窗口不超出屏幕上边界
                    if (windowTop < workingArea.Top)
                    {
                        windowTop = workingArea.Top + 10;
                    }
                    
                    settingsWindow.Left = windowLeft;
                    settingsWindow.Top = windowTop;
                    
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 面板左上角: X={panelTopLeft.X}, Y={panelTopLeft.Y}");
                    Debug.WriteLine($"[圣经设置] 面板右边缘: X={panelTopRight.X}");
                    Debug.WriteLine($"[圣经设置] 面板大小: Width={BibleNavigationPanel.ActualWidth}, Height={BibleNavigationPanel.ActualHeight}");
                    Debug.WriteLine($"[圣经设置] 屏幕工作区: {workingArea}");
                    Debug.WriteLine($"[圣经设置] 窗口大小: Width={settingsWindow.Width}, Height={settingsWindow.Height}");
                    Debug.WriteLine($"[圣经设置] 计算位置: Left={windowLeft:F1}, Top={windowTop:F1}");
                    Debug.WriteLine($"[圣经设置] 最终位置: Left={settingsWindow.Left}, Top={settingsWindow.Top}");
                    #endif
                }

                // 显示设置窗口（设置已通过回调实时应用，无需等待窗口关闭）
                settingsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 打开设置窗口失败: {ex.Message}");
                #endif

                WpfMessageBox.Show(
                    $"打开设置失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 应用圣经设置到界面
        /// </summary>
        private void ApplyBibleSettings()
        {
            try
            {
                // 应用背景色
                var backgroundColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleBackgroundColor);
                BibleVerseScrollViewer.Background = new WpfSolidColorBrush(backgroundColor);

                // 应用标题样式
                BibleChapterTitle.FontFamily = new WpfFontFamily(_configManager.BibleFontFamily);
                BibleChapterTitle.FontSize = _configManager.BibleTitleFontSize;
                var titleColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleTitleColor);
                BibleChapterTitle.Foreground = new WpfSolidColorBrush(titleColor);

                // 应用经文样式到已生成的项
                ApplyVerseStyles();

                #if DEBUG
                Debug.WriteLine("[圣经] 界面样式已更新");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 应用设置失败: {ex.Message}");
                #endif
            }
        }

        /// <summary>
        /// 应用经文样式到列表项
        /// </summary>
        private void ApplyVerseStyles()
        {
            try
            {
                if (BibleVerseList.Items.Count == 0)
                    return;

                var textColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleTextColor);
                var verseNumberColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(_configManager.BibleVerseNumberColor);
                var fontFamily = new WpfFontFamily(_configManager.BibleFontFamily);

                // 遍历所有已生成的容器
                for (int i = 0; i < BibleVerseList.Items.Count; i++)
                {
                    var container = BibleVerseList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (container == null)
                        continue;

                    // 查找节号和经文TextBlock
                    var textBlocks = FindVisualChildren<TextBlock>(container).ToList();
                    if (textBlocks.Count >= 2)
                    {
                        // 第一个是节号
                        var verseNumberBlock = textBlocks[0];
                        verseNumberBlock.FontFamily = fontFamily;
                        verseNumberBlock.FontSize = _configManager.BibleVerseNumberFontSize;
                        verseNumberBlock.Foreground = new WpfSolidColorBrush(verseNumberColor);

                        // 第二个是经文
                        var scriptureBlock = textBlocks[1];
                        scriptureBlock.FontFamily = fontFamily;
                        scriptureBlock.FontSize = _configManager.BibleFontSize;
                        scriptureBlock.Foreground = new WpfSolidColorBrush(textColor);
                    }
                    
                    // 设置Border的Margin（节间距）
                    var border = FindVisualChild<Border>(container);
                    if (border != null)
                    {
                        border.Margin = new Thickness(0, _configManager.BibleVerseSpacing / 2, 0, _configManager.BibleVerseSpacing / 2);
                        
                        #if DEBUG
                        if (i == 0) // 只输出第一个经文的调试信息
                        {
                            Debug.WriteLine($"");
                            Debug.WriteLine($"🔧 [圣经样式应用]");
                            Debug.WriteLine($"   字体大小: {_configManager.BibleFontSize}px");
                            Debug.WriteLine($"   节间距配置: {_configManager.BibleVerseSpacing}px");
                            Debug.WriteLine($"   Border Margin: {border.Margin} (上下各{_configManager.BibleVerseSpacing / 2}px)");
                            Debug.WriteLine($"   说明: 节间距控制经文之间的间距");
                            Debug.WriteLine($"");
                        }
                        #endif
                    }
                }

                // 更新边距
                BibleVerseList.Margin = new Thickness(_configManager.BibleMargin, 0, _configManager.BibleMargin, 0);

                #if DEBUG
                Debug.WriteLine($"[圣经] 已应用样式到 {BibleVerseList.Items.Count} 个经文项");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 应用经文样式失败: {ex.Message}");
                #endif
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
        
        #endregion
    }
}

