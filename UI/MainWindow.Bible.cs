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
                BibleVerseList.ItemsSource = verses;

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
            BibleChapterList.ItemsSource = null;

            #if DEBUG
            Debug.WriteLine($"[圣经] 加载了 {bookList.Count} 卷书");
            #endif
        }

        // 第2列:书卷选择事件
        private void BibleBook_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BibleBookList.SelectedItem is not BibleBook book)
            {
                BibleChapterList.ItemsSource = null;
                return;
            }

            #if DEBUG
            Debug.WriteLine($"[圣经] 选中书卷: {book.Name} (BookId={book.BookId})");
            #endif

            // 生成章列表到第3列
            var chapters = Enumerable.Range(1, book.ChapterCount).Select(c => $"{c}").ToList();
            BibleChapterList.ItemsSource = chapters;
            BibleChapterList.Tag = book.BookId; // 保存BookId供后续使用

            #if DEBUG
            Debug.WriteLine($"[圣经] 加载了 {chapters.Count} 章");
            #endif
        }

        // 第3列:章选择事件
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
                Debug.WriteLine($"[圣经] 节范围: 1-{verseCount}");
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
                BibleVerseList.ItemsSource = verses;

                #if DEBUG
                Debug.WriteLine($"[圣经] 加载经文范围: {book?.Name} {chapter}:{startVerse}-{endVerse}, 共 {verses.Count} 节");
                #endif
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
        /// 经文项点击事件
        /// </summary>
        private async void BibleVerseItem_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is BibleVerse verse)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经] 点击经文: {verse.Reference}");
                #endif

                _currentVerse = verse.Verse;

                // 投影当前经文
                await ProjectBibleVerseAsync(verse);
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
        /// 渲染经文到投影屏幕
        /// </summary>
        private SKBitmap RenderVerseToProjection(BibleVerse verse)
        {
            try
            {
                // 获取投影屏幕尺寸
                var (screenWidth, screenHeight) = _projectionManager.GetProjectionScreenSize();

                #if DEBUG
                Debug.WriteLine($"[圣经] 渲染投影: {screenWidth}x{screenHeight}, 经文: {verse.Reference}");
                #endif

                // 创建Canvas
                var canvas = new Canvas
                {
                    Width = screenWidth,
                    Height = screenHeight,
                    Background = WpfBrushes.Black
                };

                // 创建经文显示区域
                var stackPanel = new StackPanel
                {
                    Width = screenWidth - 120, // 左右各留60像素边距
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // 经文引用（书卷 章:节）
                var referenceText = new TextBlock
                {
                    Text = verse.Reference,
                    FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                    FontSize = 48,
                    FontWeight = FontWeights.Bold,
                    Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(255, 193, 7)), // 金色
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 30),
                    TextWrapping = TextWrapping.Wrap
                };

                // 经文内容
                var scriptureText = new TextBlock
                {
                    Text = verse.Scripture,
                    FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                    FontSize = 64,
                    FontWeight = FontWeights.Medium,
                    Foreground = WpfBrushes.White,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 90
                };

                stackPanel.Children.Add(referenceText);
                stackPanel.Children.Add(scriptureText);

                // 测量StackPanel实际高度
                stackPanel.Measure(new WpfSize(stackPanel.Width, double.PositiveInfinity));
                double contentHeight = stackPanel.DesiredSize.Height;

                // 计算垂直居中位置
                double topMargin = (screenHeight - contentHeight) / 2;
                if (topMargin < 0) topMargin = 40; // 如果内容太多，至少留40像素顶部边距

                Canvas.SetLeft(stackPanel, 60);
                Canvas.SetTop(stackPanel, topMargin);
                canvas.Children.Add(stackPanel);

                // 强制布局更新
                canvas.Measure(new WpfSize(screenWidth, screenHeight));
                canvas.Arrange(new Rect(0, 0, screenWidth, screenHeight));
                canvas.UpdateLayout();

                // 渲染到位图
                var renderBitmap = new RenderTargetBitmap(
                    (int)screenWidth,
                    (int)screenHeight,
                    96, 96,
                    WpfPixelFormats.Pbgra32);
                renderBitmap.Render(canvas);
                renderBitmap.Freeze();

                // 转换为SKBitmap
                return ConvertToSKBitmap(renderBitmap);
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
    }
}

