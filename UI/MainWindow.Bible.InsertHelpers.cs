using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models.Bible;
using SkiaSharp;
using WpfMessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Bible Insert Helpers
    /// </summary>
    public partial class MainWindow
    {
        private const int BiblePopupAutoHideSeconds = 180;
        private DispatcherTimer _mainBiblePopupTimer;
        private bool _isBiblePopupOverlayVisible;
        private string _biblePopupOverlayReference = string.Empty;
        private string _biblePopupOverlayContent = string.Empty;
        private BibleTextInsertConfig _biblePopupOverlayConfig = new();
        private double _biblePopupOverlayVerseScrollOffset;
        private double _biblePopupOverlayVerseMaxScroll;
        private List<double> _biblePopupOverlayVerseAnchors = new();
        private DispatcherTimer _biblePopupOverlayAnimationTimer;
        private DateTime _biblePopupOverlayAnimationStartUtc;
        private double _biblePopupOverlayEnterProgress = 1.0;
        private Rect _biblePopupOverlayLastRect = Rect.Empty;
        private Rect _biblePopupOverlayLastVerseViewportRect = Rect.Empty;
        private bool _suppressNextProjectionAnimation;

        private bool HasAnyBibleVersePopupVisible()
        {
            return _isBiblePopupOverlayVisible;
        }

        /// <summary>
        /// 幻灯片编辑状态下处理经文选择（先确认，再按投影状态分流）。
        /// </summary>
        private async Task HandleBibleVerseSelectionInSlideModeAsync(int bookId, int chapter, int startVerse, int endVerse)
        {
            var book = BibleBookConfig.GetBook(bookId);
            if (book == null)
            {
                return;
            }

            string reference = startVerse == endVerse
                ? $"{book.Name}{chapter}章{startVerse}节"
                : $"{book.Name}{chapter}章{startVerse}-{endVerse}节";

            bool isProjectionActive = _projectionManager?.IsProjectionActive == true;
            string actionText = isProjectionActive ? "弹窗显示" : "插入到幻灯片";
            var confirm = WpfMessageBox.Show(
                $"已选中经文：{reference}\n\n是否{actionText}？",
                "经文投影确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            if (isProjectionActive)
            {
                await ShowBibleVersePopupAsync(bookId, chapter, startVerse, endVerse, reference);
                return;
            }

            await CreateBibleTextElements(bookId, chapter, startVerse, endVerse);
        }

        private async Task ShowBibleVersePopupAsync(int bookId, int chapter, int startVerse, int endVerse, string reference)
        {
            var verses = await _bibleService.GetVerseRangeAsync(bookId, chapter, startVerse, endVerse);
            if (verses == null || verses.Count == 0)
            {
                return;
            }

            var config = LoadBibleInsertConfigFromDatabase();
            string content = FormatVerseWithNumbers(verses);
            ShowMainBibleVersePopup(reference, content, config, BiblePopupAutoHideSeconds);
        }

        private void HideBibleVersePopupIfVisible()
        {
            HideMainBibleVersePopup();
            // 兼容旧路径：若历史版本仍显示投影端独立弹窗，强制关闭。
            _projectionManager?.HideBibleVersePopup();
        }

        private void ShowMainBibleVersePopup(string reference, string content, BibleTextInsertConfig config, int autoHideSeconds)
        {
            if (MainBiblePopupOverlayImage == null || MainBiblePopupOverlayCloseButton == null)
            {
                return;
            }

            config ??= new BibleTextInsertConfig();

            Dispatcher.Invoke(() =>
            {
                MainBiblePopupReferenceText.Text = reference ?? string.Empty;
                MainBiblePopupContentText.Text = content ?? string.Empty;
                ApplyMainBibleVersePopupStyle(config);
                UpdateBiblePopupOverlayState(reference, content, config, true);
                MainBiblePopupBorder.Visibility = Visibility.Collapsed;
                RefreshMainBiblePopupOverlayPreview();
                StartMainBiblePopupAutoHide(autoHideSeconds);
                RefreshProjectionForBiblePopupOverlay();
            });
        }

        private void ApplyMainBibleVersePopupStyle(BibleTextInsertConfig config)
        {
            MainBiblePopupReferenceText.FontFamily = new System.Windows.Media.FontFamily(config.FontFamily);
            MainBiblePopupReferenceText.FontSize = config.TitleStyle.FontSize;
            MainBiblePopupReferenceText.FontWeight = config.TitleStyle.IsBold ? FontWeights.Bold : FontWeights.Normal;
            MainBiblePopupReferenceText.Foreground = BuildMainPopupBrush(config.TitleStyle.ColorHex, 100);

            MainBiblePopupContentText.FontFamily = new System.Windows.Media.FontFamily(config.FontFamily);
            MainBiblePopupContentText.FontSize = config.VerseStyle.FontSize;
            MainBiblePopupContentText.FontWeight = config.VerseStyle.IsBold ? FontWeights.Bold : FontWeights.Normal;
            MainBiblePopupContentText.Foreground = BuildMainPopupBrush(config.VerseStyle.ColorHex, 100);
            MainBiblePopupContentText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            MainBiblePopupContentText.LineHeight = config.VerseStyle.FontSize * Math.Max(1.0, config.VerseStyle.VerseSpacing);

            MainBiblePopupBorder.Background = BuildMainPopupBrush(
                config.PopupBackgroundColorHex,
                Math.Clamp(config.PopupBackgroundOpacity, 0, 100));
            MainBiblePopupBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 255, 255));

            switch (config.PopupPosition)
            {
                case BiblePopupPosition.Top:
                    MainBiblePopupBorder.VerticalAlignment = VerticalAlignment.Top;
                    MainBiblePopupBorder.Margin = new Thickness(30, 0, 30, 0);
                    break;
                case BiblePopupPosition.Center:
                    MainBiblePopupBorder.VerticalAlignment = VerticalAlignment.Center;
                    MainBiblePopupBorder.Margin = new Thickness(30, 0, 30, 0);
                    break;
                default:
                    MainBiblePopupBorder.VerticalAlignment = VerticalAlignment.Bottom;
                    MainBiblePopupBorder.Margin = new Thickness(30, 0, 30, 40);
                    break;
            }

            if (MainBiblePopupContentScrollViewer != null)
            {
                // 弹窗不再使用滚动交互：隐藏滚动条，保证视觉与幻灯片一致。
                MainBiblePopupContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                MainBiblePopupContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                MainBiblePopupContentScrollViewer.CanContentScroll = false;
                MainBiblePopupContentScrollViewer.ScrollToVerticalOffset(0);
            }

            if (_isBiblePopupOverlayVisible)
            {
                RefreshMainBiblePopupOverlayPreview();
            }
        }

        private static SolidColorBrush BuildMainPopupBrush(string hex, int opacityPercent)
        {
            System.Windows.Media.Color baseColor;
            try
            {
                baseColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex ?? "#000000");
            }
            catch
            {
                baseColor = System.Windows.Media.Color.FromRgb(0, 0, 0);
            }

            byte alpha = (byte)Math.Clamp((int)Math.Round(opacityPercent * 2.55), 0, 255);
            return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        }

        private void StartMainBiblePopupAutoHide(int autoHideSeconds)
        {
            int safeSeconds = Math.Max(1, autoHideSeconds);
            if (_mainBiblePopupTimer == null)
            {
                _mainBiblePopupTimer = new DispatcherTimer();
                _mainBiblePopupTimer.Tick += (_, __) => HideBibleVersePopupIfVisible();
            }

            _mainBiblePopupTimer.Stop();
            _mainBiblePopupTimer.Interval = TimeSpan.FromSeconds(safeSeconds);
            _mainBiblePopupTimer.Start();
        }

        private void HideMainBibleVersePopup()
        {
            _mainBiblePopupTimer?.Stop();
            UpdateBiblePopupOverlayState(string.Empty, string.Empty, null, false);
            if (MainBiblePopupBorder != null)
            {
                MainBiblePopupBorder.Visibility = Visibility.Collapsed;
            }
            if (MainBiblePopupOverlayImage != null)
            {
                MainBiblePopupOverlayImage.Visibility = Visibility.Collapsed;
                MainBiblePopupOverlayImage.Source = null;
            }
            if (MainBiblePopupOverlayCloseButton != null)
            {
                MainBiblePopupOverlayCloseButton.Visibility = Visibility.Collapsed;
            }
            RefreshProjectionForBiblePopupOverlay();
        }

        private void MainBiblePopupClose_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            HideBibleVersePopupIfVisible();
        }

        private void UpdateBiblePopupOverlayState(string reference, string content, BibleTextInsertConfig config, bool visible)
        {
            _isBiblePopupOverlayVisible = visible;
            _biblePopupOverlayReference = reference ?? string.Empty;
            _biblePopupOverlayContent = content ?? string.Empty;
            _biblePopupOverlayConfig = config ?? new BibleTextInsertConfig();
            if (visible)
            {
                _biblePopupOverlayVerseScrollOffset = 0;
                _biblePopupOverlayVerseAnchors.Clear();
                StartBiblePopupOverlayEnterAnimation();
            }
            else
            {
                _biblePopupOverlayVerseScrollOffset = 0;
                _biblePopupOverlayVerseMaxScroll = 0;
                _biblePopupOverlayVerseAnchors.Clear();
                StopBiblePopupOverlayEnterAnimation(resetProgress: true);
                _biblePopupOverlayLastRect = Rect.Empty;
                _biblePopupOverlayLastVerseViewportRect = Rect.Empty;
            }
        }

        private void RefreshProjectionForBiblePopupOverlay()
        {
            if (_projectionManager?.IsProjectionActive != true)
            {
                return;
            }

            if (TextEditorPanel?.Visibility != Visibility.Visible)
            {
                return;
            }
            _suppressNextProjectionAnimation = true;
            UpdateProjectionFromCanvas();
        }

        private double GetBiblePopupOverlayLineHeight()
        {
            var cfg = _biblePopupOverlayConfig ?? new BibleTextInsertConfig();
            return Math.Max(16.0, cfg.VerseStyle.FontSize * Math.Max(1.0, cfg.VerseStyle.VerseSpacing));
        }

        internal bool ConsumeSuppressNextProjectionAnimation()
        {
            if (!_suppressNextProjectionAnimation)
            {
                return false;
            }

            _suppressNextProjectionAnimation = false;
            return true;
        }

        private bool HandleBiblePopupOverlayMouseWheel(MouseWheelEventArgs e)
        {
            if (!_isBiblePopupOverlayVisible || e == null)
            {
                return false;
            }

            if (_biblePopupOverlayVerseMaxScroll <= 0)
            {
                return false;
            }

            var hitContainer = EditorCanvasContainer as IInputElement;
            if (hitContainer == null)
            {
                return false;
            }

            var pt = e.GetPosition(hitContainer);
            if (!_biblePopupOverlayLastRect.Contains(pt))
            {
                return false;
            }

            double next = GetNextBiblePopupVerseAnchorOffset(e.Delta);
            if (Math.Abs(next - _biblePopupOverlayVerseScrollOffset) < 0.5)
            {
                return true;
            }

            _biblePopupOverlayVerseScrollOffset = next;
            RefreshMainBiblePopupOverlayPreview();
            RefreshProjectionForBiblePopupOverlay();
            return true;
        }

        private void StartBiblePopupOverlayEnterAnimation()
        {
            if (!_biblePopupAnimationEnabled)
            {
                StopBiblePopupOverlayEnterAnimation(resetProgress: true);
                return;
            }

            _biblePopupOverlayAnimationStartUtc = DateTime.UtcNow;
            _biblePopupOverlayEnterProgress = 0.0;

            if (_biblePopupOverlayAnimationTimer == null)
            {
                _biblePopupOverlayAnimationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _biblePopupOverlayAnimationTimer.Tick += (_, __) => OnBiblePopupOverlayAnimationTick();
            }

            _biblePopupOverlayAnimationTimer.Stop();
            _biblePopupOverlayAnimationTimer.Start();
        }

        private void StopBiblePopupOverlayEnterAnimation(bool resetProgress)
        {
            _biblePopupOverlayAnimationTimer?.Stop();
            if (resetProgress)
            {
                _biblePopupOverlayEnterProgress = 1.0;
            }
        }

        private void OnBiblePopupOverlayAnimationTick()
        {
            if (!_isBiblePopupOverlayVisible)
            {
                StopBiblePopupOverlayEnterAnimation(resetProgress: true);
                return;
            }

            double durationMs = Math.Clamp(_biblePopupAnimationDuration, 100, 3000);
            double elapsedMs = (DateTime.UtcNow - _biblePopupOverlayAnimationStartUtc).TotalMilliseconds;
            _biblePopupOverlayEnterProgress = Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);

            RefreshMainBiblePopupOverlayPreview();
            RefreshProjectionForBiblePopupOverlay();

            if (_biblePopupOverlayEnterProgress >= 1.0)
            {
                StopBiblePopupOverlayEnterAnimation(resetProgress: false);
            }
        }

        /// <summary>
        /// 弹窗动画参数变更后立即生效：当前弹窗可见时重置进入动画并刷新主屏/投影。
        /// </summary>
        private void ApplyBiblePopupAnimationSettingsImmediately()
        {
            if (!_isBiblePopupOverlayVisible)
            {
                return;
            }

            if (_biblePopupAnimationEnabled)
            {
                StartBiblePopupOverlayEnterAnimation();
            }
            else
            {
                StopBiblePopupOverlayEnterAnimation(resetProgress: true);
            }

            RefreshMainBiblePopupOverlayPreview();
            RefreshProjectionForBiblePopupOverlay();
        }

        private bool TryOpenBibleToolbarForPopupOverlay()
        {
            if (!_isBiblePopupOverlayVisible || BibleToolbar == null)
            {
                return false;
            }

            ShowBibleFloatingToolbar();
            return true;
        }

        private double GetNextBiblePopupVerseAnchorOffset(int wheelDelta)
        {
            if (_biblePopupOverlayVerseAnchors == null || _biblePopupOverlayVerseAnchors.Count == 0)
            {
                return _biblePopupOverlayVerseScrollOffset;
            }

            const double epsilon = 0.5;
            double current = Math.Clamp(_biblePopupOverlayVerseScrollOffset, 0, _biblePopupOverlayVerseMaxScroll);
            var anchors = _biblePopupOverlayVerseAnchors;

            if (wheelDelta < 0)
            {
                for (int i = 0; i < anchors.Count; i++)
                {
                    if (anchors[i] > current + epsilon)
                    {
                        return anchors[i];
                    }
                }

                return _biblePopupOverlayVerseMaxScroll;
            }

            for (int i = anchors.Count - 1; i >= 0; i--)
            {
                if (anchors[i] < current - epsilon)
                {
                    return anchors[i];
                }
            }

            return 0;
        }

        private void RefreshMainBiblePopupOverlayPreview()
        {
            if (!_isBiblePopupOverlayVisible || MainBiblePopupOverlayImage == null)
            {
                return;
            }

            int width = (int)Math.Round(EditorCanvas?.ActualWidth > 1 ? EditorCanvas.ActualWidth : 1600);
            int height = (int)Math.Round(EditorCanvas?.ActualHeight > 1 ? EditorCanvas.ActualHeight : 900);
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            using var bitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
            using (var canvas = new SkiaSharp.SKCanvas(bitmap))
            {
                canvas.Clear(SkiaSharp.SKColors.Transparent);
                DrawBiblePopupOverlayToCanvas(canvas, width, height);
            }

            MainBiblePopupOverlayImage.Source = ConvertSkBitmapToBitmapSource(bitmap);
            MainBiblePopupOverlayImage.Visibility = Visibility.Visible;
            UpdateMainBiblePopupCloseButtonLayout(width, height);
        }

        private void UpdateMainBiblePopupCloseButtonLayout(double canvasWidth, double canvasHeight)
        {
            if (MainBiblePopupOverlayCloseButton == null)
            {
                return;
            }

            // 直接使用本帧实际绘制矩形，避免居中/底部时估算误差导致按钮“固定错位”。
            Rect rect = _biblePopupOverlayLastRect;
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            {
                var cfg = _biblePopupOverlayConfig ?? new BibleTextInsertConfig();
                double popupWidth = Math.Max(480.0, Math.Min(canvasWidth - 60.0, 1500.0));
                double popupX = Math.Max(0.0, (canvasWidth - popupWidth) / 2.0);
                var popupHeight = EstimateBiblePopupHeight(canvasWidth, canvasHeight, cfg);
                double popupY = cfg.PopupPosition switch
                {
                    BiblePopupPosition.Top => 0.0,
                    BiblePopupPosition.Center => (canvasHeight - popupHeight) / 2.0,
                    _ => canvasHeight - popupHeight - 40.0
                };
                if (popupY < 0.0) popupY = 0.0;
                rect = new Rect(popupX, popupY, popupWidth, popupHeight);
            }

            double closeX = rect.Left + rect.Width - 36.0 - 8.0;
            double closeY = rect.Top + 8.0;

            MainBiblePopupOverlayCloseButton.Margin = new Thickness(closeX, closeY, 0, 0);
            MainBiblePopupOverlayCloseButton.Visibility = Visibility.Visible;
        }

        private double EstimateBiblePopupHeight(double canvasWidth, double canvasHeight, BibleTextInsertConfig cfg)
        {
            double popupWidth = Math.Max(480.0, Math.Min(canvasWidth - 60.0, 1500.0));
            float textMaxWidth = (float)Math.Max(1.0, popupWidth - 72.0);

            using var titlePaint = new SkiaSharp.SKPaint
            {
                IsAntialias = true,
                Typeface = SkiaSharp.SKTypeface.FromFamilyName(cfg.FontFamily ?? "Microsoft YaHei UI"),
                TextSize = Math.Max(16f, cfg.TitleStyle.FontSize)
            };
            using var versePaint = new SkiaSharp.SKPaint
            {
                IsAntialias = true,
                Typeface = SkiaSharp.SKTypeface.FromFamilyName(cfg.FontFamily ?? "Microsoft YaHei UI"),
                TextSize = Math.Max(16f, cfg.VerseStyle.FontSize)
            };

            var titleLines = WrapTextByWidth(_biblePopupOverlayReference ?? string.Empty, titlePaint, textMaxWidth);
            var verseLines = WrapTextByWidth(_biblePopupOverlayContent ?? string.Empty, versePaint, textMaxWidth);

            float titleHeight = titlePaint.TextSize * 1.25f;
            float lineHeight = Math.Max(versePaint.TextSize * (float)Math.Max(1.0, cfg.VerseStyle.VerseSpacing), versePaint.TextSize * 1.2f);
            float contentHeight = Math.Max(1, titleLines.Count) * titleHeight + 10f + Math.Max(1, verseLines.Count) * lineHeight;
            return 24f + contentHeight + 24f;
        }

        private static BitmapSource ConvertSkBitmapToBitmapSource(SkiaSharp.SKBitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);

            var source = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);
            source.Freeze();
            return source;
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
                //Debug.WriteLine($"[圣经插入] 开始创建圣经文本元素: BookId={bookId}, Chapter={chapter}, StartVerse={startVerse}, EndVerse={endVerse}");
                //#endif

                // 1. 获取经文内容
                var verses = await _bibleService.GetVerseRangeAsync(bookId, chapter, startVerse, endVerse);

                //#if DEBUG
                //Debug.WriteLine($"[圣经插入] 获取到经文数量: {verses?.Count ?? 0}");
                //if (verses != null && verses.Count > 0)
                //{
                //    foreach (var v in verses)
                //    {
                //        Debug.WriteLine($"[圣经插入] Verse={v.Verse}, DisplayVerseNumber={v.DisplayVerseNumber}, Scripture={v.Scripture?.Substring(0, Math.Min(20, v.Scripture?.Length ?? 0))}...");
                //    }
                //}
                //else
                //{
                //    Debug.WriteLine($" [圣经插入] 经文列表为空或null");
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
                //Debug.WriteLine($" [圣经创建] 开始创建文本框元素");
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
                    //Debug.WriteLine($" [圣经创建] 已自动隐藏圣经导航栏，切换到幻灯片模式");
                    //#endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [圣经创建] 创建文本框元素失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
                
                WpfMessageBox.Show("创建经文元素失败", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 格式化经文（带节号）
        /// 支持"-"节的合并显示（使用DisplayVerseNumber）
        /// </summary>
        private string FormatVerseWithNumbers(List<BibleVerse> verses)
        {
            var lines = new List<string>();
            foreach (var verse in verses)
            {
                // 优先使用DisplayVerseNumber（处理"-"节合并后的节号，如"10、11"）
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
                Debug.WriteLine($" [圣经创建] 当前没有选中的幻灯片");
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
                await _textProjectService.AddElementAsync(textElement);
                
                // 在 UI 线程上创建 DraggableTextBox 并添加到画布
                await Dispatcher.InvokeAsync(() =>
                {
                    var textBox = new UI.Controls.DraggableTextBox(textElement);
                    AddTextBoxToCanvas(textBox);
                    
                    // 标记内容已修改
                    MarkContentAsModified();
                    
                    //#if DEBUG
                    //Debug.WriteLine($" [圣经创建] 文本框已添加到画布");
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
                Debug.WriteLine($" [圣经创建] 创建单个文本框失败: {ex.Message}");
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
            //Debug.WriteLine($"[CreateRichTextVerseElement] 开始创建富文本经文元素");
            //Debug.WriteLine($"   参数: verses.Count={verses?.Count ?? 0}, x={x}, y={y}");
            //Debug.WriteLine($"   配置: FontFamily={config?.FontFamily}, VerseSize={config?.VerseStyle?.FontSize}, VerseColor={config?.VerseStyle?.ColorHex}");
            //Debug.WriteLine($"   配置: NumberSize={config?.VerseNumberStyle?.FontSize}, NumberColor={config?.VerseNumberStyle?.ColorHex}");
            //Debug.WriteLine($"   当前幻灯片: {(_currentSlide != null ? $"ID={_currentSlide.Id}" : "null")}");
            //#endif

            if (_currentSlide == null || verses == null || verses.Count == 0)
            {
                #if DEBUG
                Debug.WriteLine($" [圣经创建] 当前没有选中的幻灯片或经文为空");
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
                //Debug.WriteLine($"[CreateRichTextVerseElement] 行间距={lineSpacing:F1}");
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
                await _textProjectService.AddElementAsync(textElement);

                // 创建富文本片段（RichTextSpan）
                var richTextSpans = new List<Database.Models.RichTextSpan>();
                int spanOrder = 0;

                //#if DEBUG
                //Debug.WriteLine($"[CreateRichTextVerseElement] 开始创建富文本片段，经文数量: {verses.Count}");
                //#endif

                foreach (var verse in verses)
                {
                    //#if DEBUG
                    //Debug.WriteLine($"   处理第 {verse.Verse} 节: {verse.Scripture?.Substring(0, Math.Min(20, verse.Scripture?.Length ?? 0))}...");
                    //#endif

                    // 节号片段（优先使用DisplayVerseNumber，支持"-"节合并显示）
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
                //Debug.WriteLine($"[CreateRichTextVerseElement] 保存 {richTextSpans.Count} 个富文本片段到数据库");
                //#endif

                foreach (var span in richTextSpans)
                {
                    await _textProjectService.AddRichTextSpanAsync(span);
                }

                //#if DEBUG
                //Debug.WriteLine($" [CreateRichTextVerseElement] 富文本片段保存完成");
                //#endif

                // 将富文本片段关联到文本元素
                textElement.RichTextSpans = richTextSpans;

                //#if DEBUG
                //Debug.WriteLine($"[CreateRichTextVerseElement] 创建 DraggableTextBox 并添加到画布");
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
                    //Debug.WriteLine($" [CreateRichTextVerseElement] 文本框已添加到画布");
                    //#endif
                });
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [圣经创建] 创建富文本经文元素失败: {ex.Message}");
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

                await _textProjectService.AddElementAsync(textElement);

                // 创建富文本片段
                var richTextSpans = new List<Database.Models.RichTextSpan>();
                int spanOrder = 0;

                foreach (var verse in verses)
                {
                    // 节号（优先使用DisplayVerseNumber，支持"-"节合并显示）
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
                    await _textProjectService.AddRichTextSpanAsync(span);
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
                Debug.WriteLine($" [圣经创建] 创建富文本经文+标题元素失败: {ex.Message}");
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
            HideBibleFloatingToolbar();

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
                HideBibleFloatingToolbar();

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
                //Debug.WriteLine($"[智能插入] Canvas为空，插入到左上角: ({margin}, {margin})");
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
                    //Debug.WriteLine($"[智能插入] 在最后元素下方: ({newX:F0}, {newY:F0})");
                    //Debug.WriteLine($"   最后元素位置: ({lastX:F0}, {lastY:F0}), 高度: {lastHeight:F0}");
                    //#endif
                    
                    return new System.Windows.Point(newX, newY);
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [智能插入] 计算位置失败: {ex.Message}，使用默认位置");
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
            var dbManager = DatabaseManagerService;

            // 从数据库加载配置
            config.Style = (BibleTextInsertStyle)int.Parse(dbManager.GetBibleInsertConfigValue("style", "0"));
            config.FontFamily = dbManager.GetBibleInsertConfigValue("font_family", "DengXian");

            config.TitleStyle.ColorHex = dbManager.GetBibleInsertConfigValue("title_color", "#FF0000");
            config.TitleStyle.FontSize = float.Parse(dbManager.GetBibleInsertConfigValue("title_size", "50"));
            config.TitleStyle.IsBold = dbManager.GetBibleInsertConfigValue("title_bold", "1") == "1";

            config.VerseStyle.ColorHex = dbManager.GetBibleInsertConfigValue("verse_color", "#FF9A35");
            config.VerseStyle.FontSize = float.Parse(dbManager.GetBibleInsertConfigValue("verse_size", "40"));
            config.VerseStyle.IsBold = dbManager.GetBibleInsertConfigValue("verse_bold", "0") == "1";
            config.VerseStyle.VerseSpacing = float.Parse(dbManager.GetBibleInsertConfigValue("verse_spacing", "1.2"));

            config.VerseNumberStyle.ColorHex = dbManager.GetBibleInsertConfigValue("verse_number_color", "#FFFF00");
            config.VerseNumberStyle.FontSize = float.Parse(dbManager.GetBibleInsertConfigValue("verse_number_size", "40"));
            config.VerseNumberStyle.IsBold = dbManager.GetBibleInsertConfigValue("verse_number_bold", "1") == "1";

            config.AutoHideNavigationAfterInsert = dbManager.GetBibleInsertConfigValue("auto_hide_navigation", "1") == "1";
            var popupPosition = dbManager.GetBibleInsertConfigValue("popup_position", "Bottom");
            config.PopupPosition = popupPosition switch
            {
                "Top" => BiblePopupPosition.Top,
                "Center" => BiblePopupPosition.Center,
                _ => BiblePopupPosition.Bottom
            };
            config.PopupBackgroundColorHex = dbManager.GetBibleInsertConfigValue("popup_bg_color", "#000000");
            if (!int.TryParse(dbManager.GetBibleInsertConfigValue("popup_bg_opacity", "100"), out var popupOpacity))
            {
                popupOpacity = 100;
            }
            config.PopupBackgroundOpacity = Math.Clamp(popupOpacity, 0, 100);
            
            //#if DEBUG
            //Debug.WriteLine($"[圣经插入] 从数据库加载配置");
            //Debug.WriteLine($"   字体: {config.FontFamily}");
            //Debug.WriteLine($"   标题字体大小（实际值 = 显示值×2）: {config.TitleStyle.FontSize}");
            //Debug.WriteLine($"   经文字体大小（实际值 = 显示值×2）: {config.VerseStyle.FontSize}");
            //#endif
            
            return config;
        }

    }
}
