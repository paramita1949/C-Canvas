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
        private DispatcherTimer _mainBiblePopupTimer;
        private bool _isBiblePopupOverlayVisible;
        private string _biblePopupOverlayReference = string.Empty;
        private string _biblePopupOverlayContent = string.Empty;
        private string _biblePopupOverlayBookName = string.Empty;
        private int _biblePopupOverlayChapter;
        private int _biblePopupOverlayStartVerse;
        private int _biblePopupOverlayEndVerse;
        private BibleTextInsertConfig _biblePopupOverlayConfig = new();
        private double _biblePopupOverlayVerseScrollOffset;
        private double _biblePopupOverlayVerseMaxScroll;
        private List<double> _biblePopupOverlayVerseAnchors = new();
        private List<double> _biblePopupOverlayVerseHeights = new();
        private int _biblePopupOverlayHighlightedVerseIndex = -1;
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
                await ShowBibleVersePopupAsync(bookId, chapter, startVerse, endVerse);
                return;
            }

            await CreateBibleTextElements(bookId, chapter, startVerse, endVerse);
        }

        private async Task ShowBibleVersePopupAsync(int bookId, int chapter, int startVerse, int endVerse)
        {
            var verses = await _bibleService.GetVerseRangeAsync(bookId, chapter, startVerse, endVerse);
            if (verses == null || verses.Count == 0)
            {
                return;
            }

            var config = LoadBibleInsertConfigFromDatabase();
            var book = BibleBookConfig.GetBook(bookId);
            if (book == null)
            {
                return;
            }

            string reference = BuildBibleReference(book.Name, chapter, startVerse, endVerse, config.PopupTitleFormat);
            string content = FormatVerseWithNumbers(verses);
            int popupAutoHideSeconds = config.PopupDurationMinutes * 60;
            _biblePopupOverlayBookName = book.Name ?? string.Empty;
            _biblePopupOverlayChapter = chapter;
            _biblePopupOverlayStartVerse = startVerse;
            _biblePopupOverlayEndVerse = endVerse;
            ShowMainBibleVersePopup(reference, content, config, popupAutoHideSeconds);
        }

        private static string BuildBibleReference(string bookName, int chapter, int startVerse, int endVerse, BiblePopupTitleFormat format)
        {
            string safeBook = string.IsNullOrWhiteSpace(bookName) ? "未知书卷" : bookName.Trim();
            bool single = startVerse == endVerse;

            return format switch
            {
                BiblePopupTitleFormat.ColonFormat => single
                    ? $"{safeBook} {chapter}:{startVerse}"
                    : $"{safeBook} {chapter}:{startVerse}-{endVerse}",
                BiblePopupTitleFormat.ChapterVerse => single
                    ? $"{safeBook} {chapter}章{startVerse}节"
                    : $"{safeBook} {chapter}章{startVerse}-{endVerse}节",
                _ => single
                    ? $"{safeBook} · {chapter}章{startVerse}节"
                    : $"{safeBook} · {chapter}章{startVerse}-{endVerse}节"
            };
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
            MainBiblePopupReferenceText.FontFamily = new System.Windows.Media.FontFamily(config.PopupFontFamily ?? config.FontFamily);
            MainBiblePopupReferenceText.FontSize = config.PopupTitleStyle.FontSize;
            MainBiblePopupReferenceText.FontWeight = config.PopupTitleStyle.IsBold ? FontWeights.Bold : FontWeights.Normal;
            MainBiblePopupReferenceText.Foreground = BuildMainPopupBrush(config.PopupTitleStyle.ColorHex, 0);

            MainBiblePopupContentText.FontFamily = new System.Windows.Media.FontFamily(config.PopupFontFamily ?? config.FontFamily);
            MainBiblePopupContentText.FontSize = config.PopupVerseStyle.FontSize;
            MainBiblePopupContentText.FontWeight = config.PopupVerseStyle.IsBold ? FontWeights.Bold : FontWeights.Normal;
            MainBiblePopupContentText.Foreground = BuildMainPopupBrush(config.PopupVerseStyle.ColorHex, 0);
            MainBiblePopupContentText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            MainBiblePopupContentText.LineHeight = config.PopupVerseStyle.FontSize * Math.Max(1.0, config.PopupVerseStyle.VerseSpacing);

            bool hideBorder = BiblePopupOpacity.ShouldHideBorder(config.PopupBackgroundOpacity);
            MainBiblePopupBorder.Background = BuildMainPopupBrush(
                config.PopupBackgroundColorHex,
                Math.Clamp(config.PopupBackgroundOpacity, 0, 100));
            MainBiblePopupBorder.BorderThickness = hideBorder ? new Thickness(0) : new Thickness(1);
            MainBiblePopupBorder.BorderBrush = hideBorder
                ? System.Windows.Media.Brushes.Transparent
                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 255, 255));

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

        private static SolidColorBrush BuildMainPopupBrush(string hex, int transparencyPercent)
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

            byte alpha = BiblePopupOpacity.ToAlphaFromTransparencyPercent(transparencyPercent);
            return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        }

        private void ApplyVisibleBiblePopupStyleImmediately()
        {
            if (!_isBiblePopupOverlayVisible)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                var refreshedConfig = LoadBibleInsertConfigFromDatabase();
                _biblePopupOverlayConfig = refreshedConfig;
                if (!string.IsNullOrWhiteSpace(_biblePopupOverlayBookName) && _biblePopupOverlayChapter > 0)
                {
                    string refreshedReference = BuildBibleReference(
                        _biblePopupOverlayBookName,
                        _biblePopupOverlayChapter,
                        _biblePopupOverlayStartVerse,
                        _biblePopupOverlayEndVerse,
                        refreshedConfig.PopupTitleFormat);
                    _biblePopupOverlayReference = refreshedReference;
                    if (MainBiblePopupReferenceText != null)
                    {
                        MainBiblePopupReferenceText.Text = refreshedReference;
                    }
                }
                ApplyMainBibleVersePopupStyle(refreshedConfig);
                StartMainBiblePopupAutoHide(refreshedConfig.PopupDurationMinutes * 60);
                RefreshMainBiblePopupOverlayPreview();
                RefreshProjectionForBiblePopupOverlay();
            });
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
                _biblePopupOverlayVerseHeights.Clear();
                _biblePopupOverlayHighlightedVerseIndex = -1;
                StartBiblePopupOverlayEnterAnimation();
            }
            else
            {
                _biblePopupOverlayVerseScrollOffset = 0;
                _biblePopupOverlayVerseMaxScroll = 0;
                _biblePopupOverlayVerseAnchors.Clear();
                _biblePopupOverlayVerseHeights.Clear();
                _biblePopupOverlayHighlightedVerseIndex = -1;
                StopBiblePopupOverlayEnterAnimation(resetProgress: true);
                _biblePopupOverlayLastRect = Rect.Empty;
                _biblePopupOverlayLastVerseViewportRect = Rect.Empty;
                _biblePopupOverlayBookName = string.Empty;
                _biblePopupOverlayChapter = 0;
                _biblePopupOverlayStartVerse = 0;
                _biblePopupOverlayEndVerse = 0;
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
            return Math.Max(16.0, cfg.PopupVerseStyle.FontSize * Math.Max(1.0, cfg.PopupVerseStyle.VerseSpacing));
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

            double previousOffset = _biblePopupOverlayVerseScrollOffset;
            int previousIndex = _biblePopupOverlayHighlightedVerseIndex;
            if (!TryAdvanceBiblePopupHighlightedVerse(e.Delta, out double next))
            {
                return false;
            }

            _biblePopupOverlayVerseScrollOffset = next;
            bool changed =
                Math.Abs(previousOffset - _biblePopupOverlayVerseScrollOffset) >= 0.5 ||
                previousIndex != _biblePopupOverlayHighlightedVerseIndex;
            if (!changed)
            {
                return true;
            }

            RefreshMainBiblePopupOverlayPreview();
            RefreshProjectionForBiblePopupOverlay();
            return true;
        }

        private bool HandleBiblePopupOverlayClick(MouseButtonEventArgs e)
        {
            if (!_isBiblePopupOverlayVisible || e == null)
            {
                return false;
            }

            var hitContainer = EditorCanvasContainer as IInputElement;
            if (hitContainer == null || _biblePopupOverlayVerseAnchors.Count == 0 || _biblePopupOverlayVerseHeights.Count == 0)
            {
                return false;
            }

            var pt = e.GetPosition(hitContainer);
            if (!_biblePopupOverlayLastVerseViewportRect.Contains(pt))
            {
                return false;
            }

            double relativeY = pt.Y - _biblePopupOverlayLastVerseViewportRect.Top + _biblePopupOverlayVerseScrollOffset;
            int verseCount = Math.Min(_biblePopupOverlayVerseAnchors.Count, _biblePopupOverlayVerseHeights.Count);
            if (verseCount <= 0)
            {
                return false;
            }

            int targetIndex = -1;
            for (int i = 0; i < verseCount; i++)
            {
                double top = _biblePopupOverlayVerseAnchors[i];
                double bottom = top + Math.Max(1.0, _biblePopupOverlayVerseHeights[i]);
                if (relativeY >= top && relativeY < bottom)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex < 0)
            {
                targetIndex = Math.Clamp((int)Math.Round(relativeY / Math.Max(1.0, GetBiblePopupOverlayLineHeight())), 0, verseCount - 1);
            }

            _biblePopupOverlayHighlightedVerseIndex = targetIndex;
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

        private bool TryAdvanceBiblePopupHighlightedVerse(int wheelDelta, out double nextOffset)
        {
            nextOffset = _biblePopupOverlayVerseScrollOffset;
            if (_biblePopupOverlayVerseAnchors == null || _biblePopupOverlayVerseAnchors.Count == 0)
            {
                return false;
            }

            int verseCount = _biblePopupOverlayVerseAnchors.Count;
            if (verseCount <= 0)
            {
                return false;
            }

            int currentIndex = _biblePopupOverlayHighlightedVerseIndex;
            if (currentIndex < 0 || currentIndex >= verseCount)
            {
                const double epsilon = 0.5;
                double currentOffset = Math.Clamp(_biblePopupOverlayVerseScrollOffset, 0, _biblePopupOverlayVerseMaxScroll);
                currentIndex = 0;
                for (int i = 0; i < verseCount; i++)
                {
                    if (_biblePopupOverlayVerseAnchors[i] <= currentOffset + epsilon)
                    {
                        currentIndex = i;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            int delta = wheelDelta < 0 ? 1 : -1;
            int targetIndex = Math.Clamp(currentIndex + delta, 0, verseCount - 1);
            if (targetIndex == currentIndex)
            {
                return false;
            }

            _biblePopupOverlayHighlightedVerseIndex = targetIndex;
            nextOffset = Math.Clamp(_biblePopupOverlayVerseAnchors[targetIndex], 0, _biblePopupOverlayVerseMaxScroll);
            return true;
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

            using var titleFont = new SkiaSharp.SKFont
            {
                Typeface = SkiaSharp.SKTypeface.FromFamilyName(cfg.PopupFontFamily ?? cfg.FontFamily ?? "Microsoft YaHei UI"),
                Size = Math.Max(16f, cfg.PopupTitleStyle.FontSize),
                Subpixel = true,
                Edging = SkiaSharp.SKFontEdging.Antialias
            };
            using var verseFont = new SkiaSharp.SKFont
            {
                Typeface = SkiaSharp.SKTypeface.FromFamilyName(cfg.PopupFontFamily ?? cfg.FontFamily ?? "Microsoft YaHei UI"),
                Size = Math.Max(16f, cfg.PopupVerseStyle.FontSize),
                Subpixel = true,
                Edging = SkiaSharp.SKFontEdging.Antialias
            };
            using var titlePaint = new SkiaSharp.SKPaint { IsAntialias = true };
            using var versePaint = new SkiaSharp.SKPaint { IsAntialias = true };

            var titleLines = WrapTextByWidth(_biblePopupOverlayReference ?? string.Empty, titleFont, titlePaint, textMaxWidth);
            var verseLines = WrapTextByWidth(_biblePopupOverlayContent ?? string.Empty, verseFont, versePaint, textMaxWidth);

            float titleHeight = titleFont.Size * 1.25f;
            float lineHeight = Math.Max(verseFont.Size * (float)Math.Max(1.0, cfg.PopupVerseStyle.VerseSpacing), verseFont.Size * 1.2f);
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
                
                // 2. 加载样式配置（从数据库）
                var config = LoadBibleInsertConfigFromDatabase();

                // 3. 生成引用
                var book = BibleBookConfig.GetBook(bookId);
                if (book == null)
                {
                    return;
                }
                string reference = (startVerse == endVerse)
                    ? $"{book.Name}{chapter}章{startVerse}节"
                    : $"{book.Name}{chapter}章{startVerse}-{endVerse}节";
                
                // 4. 格式化经文（带节号）
                string verseContent = FormatVerseWithNumbers(verses);
                
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
                        double titleAvailableTop = Math.Min(220, GetAvailableInsertHeight(startY));
                        double titleHeightTop = await CreateSingleTextElement(
                            content: $"[{reference}]",
                            x: startX,
                            y: startY,
                            fontFamily: config.FontFamily,
                            fontSize: config.TitleStyle.FontSize,
                            color: config.TitleStyle.ColorHex,
                            isBold: config.TitleStyle.IsBold,
                            maxHeightHint: titleAvailableTop
                        );

                        // 使用富文本方式创建经文（节号+经文内容）
                        double verseYTop = startY + titleHeightTop + 20;
                        await CreateRichTextVerseElement(
                            verses: verses,
                            x: startX,
                            y: verseYTop,
                            config: config,
                            maxHeightHint: GetAvailableInsertHeight(verseYTop)
                        );
                        break;
                        
                    case BibleTextInsertStyle.TitleAtBottom:
                        // 经文在上，标题在下
                        // 使用富文本方式创建经文（节号+经文内容）
                        double estimatedTitleContentHeight = EstimateBibleInsertContentHeight(
                            $"[{reference}]",
                            config.FontFamily,
                            config.TitleStyle.FontSize,
                            1.2,
                            (float)Math.Max(80.0, GetBibleInsertPreferredWidth() - 24.0));
                        double estimatedTitleBoxHeight = estimatedTitleContentHeight + 18.0;
                        double verseAvailableBottom = Math.Max(80.0, GetAvailableInsertHeight(startY) - estimatedTitleBoxHeight - 20.0);

                        double verseHeightBottom = await CreateRichTextVerseElement(
                            verses: verses,
                            x: startX,
                            y: startY,
                            config: config,
                            maxHeightHint: verseAvailableBottom
                        );

                        double titleYBottom = startY + verseHeightBottom + 20;
                        await CreateSingleTextElement(
                            content: $"[{reference}]",
                            x: startX,
                            y: titleYBottom,
                            fontFamily: config.FontFamily,
                            fontSize: config.TitleStyle.FontSize,
                            color: config.TitleStyle.ColorHex,
                            isBold: config.TitleStyle.IsBold,
                            maxHeightHint: GetAvailableInsertHeight(titleYBottom)
                        );
                        break;

                    case BibleTextInsertStyle.InlineAtEnd:
                        // 标注在末尾（使用富文本：节号+经文+标题）
                        await CreateRichTextVerseWithTitleElement(
                            verses: verses,
                            reference: reference,
                            x: startX,
                            y: startY,
                            config: config,
                            maxHeightHint: GetAvailableInsertHeight(startY)
                        );
                        break;
                        
                    default:
                        // 默认：标题在上
                        double titleAvailableDefault = Math.Min(220, GetAvailableInsertHeight(startY));
                        double titleHeightDefault = await CreateSingleTextElement(
                            content: $"[{reference}]",
                            x: startX,
                            y: startY,
                            fontFamily: config.FontFamily,
                            fontSize: config.TitleStyle.FontSize,
                            color: config.TitleStyle.ColorHex,
                            isBold: config.TitleStyle.IsBold,
                            maxHeightHint: titleAvailableDefault
                        );

                        double verseYDefault = startY + titleHeightDefault + 20;
                        await CreateSingleTextElement(
                            content: verseContent,
                            x: startX,
                            y: verseYDefault,
                            fontFamily: config.FontFamily,
                            fontSize: config.VerseStyle.FontSize,
                            color: config.VerseStyle.ColorHex,
                            isBold: config.VerseStyle.IsBold,
                            maxHeightHint: GetAvailableInsertHeight(verseYDefault)
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
        private async Task<double> CreateSingleTextElement(
            string content, 
            double x, 
            double y, 
            string fontFamily, 
            float fontSize, 
            string color, 
            bool isBold,
            double? maxHeightHint = null)
        {
            if (_currentSlide == null)
            {
                #if DEBUG
                Debug.WriteLine($" [圣经创建] 当前没有选中的幻灯片");
                #endif
                return 0;
            }
            
            try
            {
                // 计算最大ZIndex，新文本在最上层
                int maxZIndex = 0;
                if (_textBoxes.Count > 0)
                {
                    maxZIndex = _textBoxes.Max(tb => tb.Data.ZIndex);
                }
                
                var bounds = ComputeBibleInsertTextBounds(
                    content,
                    x,
                    y,
                    fontFamily,
                    fontSize,
                    lineSpacing: 1.2,
                    maxHeightHint: maxHeightHint);

                var textElement = new Database.Models.TextElement
                {
                    SlideId = _currentSlide.Id,
                    Content = content,
                    X = bounds.X,
                    Y = bounds.Y,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    FontFamily = fontFamily,
                    FontSize = bounds.AppliedFontSize,
                    FontColor = color,
                    IsBold = isBold ? 1 : 0,
                    ZIndex = maxZIndex + 1
                };
                
                // 保存到数据库
                await _textProjectService.AddElementAsync(textElement);
                
                // 在 UI 线程上创建 DraggableTextBox 并添加到画布
                double actualHeight = bounds.Height;
                await Dispatcher.InvokeAsync(() =>
                {
                    var textBox = new UI.Controls.DraggableTextBox(textElement);
                    AddTextBoxToCanvas(textBox);
                    actualHeight = AutoFitTextBoxHeightToContent(textBox, bounds.AppliedFontSize);
                    
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

                return actualHeight;
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [圣经创建] 创建单个文本框失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }

            return 0;
        }

        /// <summary>
        /// 创建富文本经文元素（节号+经文内容，使用 RichTextSpan）
        /// </summary>
        private async Task<double> CreateRichTextVerseElement(
            List<BibleVerse> verses,
            double x,
            double y,
            BibleTextInsertConfig config,
            double? maxHeightHint = null)
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
                return 0;
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

                float baseSize = Math.Max(config.VerseStyle.FontSize, config.VerseNumberStyle.FontSize);
                var bounds = ComputeBibleInsertTextBounds(
                    fullContent,
                    x,
                    y,
                    config.FontFamily,
                    baseSize,
                    lineSpacing,
                    maxHeightHint: maxHeightHint);

                float scale = baseSize > 0 ? bounds.AppliedFontSize / baseSize : 1f;
                float verseFontSize = Math.Max(10f, config.VerseStyle.FontSize * scale);
                float verseNumberFontSize = Math.Max(10f, config.VerseNumberStyle.FontSize * scale);

                //#if DEBUG
                //Debug.WriteLine($"[CreateRichTextVerseElement] 行间距={lineSpacing:F1}");
                //#endif

                // 创建文本元素
                var textElement = new Database.Models.TextElement
                {
                    SlideId = _currentSlide.Id,
                    Content = fullContent,
                    X = bounds.X,
                    Y = bounds.Y,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    FontFamily = config.FontFamily,
                    FontSize = verseFontSize,
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
                        FontSize = verseNumberFontSize,
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
                        FontSize = verseFontSize,
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
                        FontSize = verseFontSize,
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
                            FontSize = verseFontSize,
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
                double actualHeight = bounds.Height;
                await Dispatcher.InvokeAsync(() =>
                {
                    var textBox = new UI.Controls.DraggableTextBox(textElement);
                    AddTextBoxToCanvas(textBox);
                    actualHeight = AutoFitTextBoxHeightToContent(textBox, bounds.AppliedFontSize);
                    MarkContentAsModified();

                    //#if DEBUG
                    //Debug.WriteLine($" [CreateRichTextVerseElement] 文本框已添加到画布");
                    //#endif
                });

                return actualHeight;
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [圣经创建] 创建富文本经文元素失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }

            return 0;
        }

        /// <summary>
        /// 创建富文本经文+标题元素（节号+经文内容+标题，使用 RichTextSpan）
        /// </summary>
        private async Task<double> CreateRichTextVerseWithTitleElement(
            List<BibleVerse> verses,
            string reference,
            double x,
            double y,
            BibleTextInsertConfig config,
            double? maxHeightHint = null)
        {
            if (_currentSlide == null || verses == null || verses.Count == 0)
            {
                return 0;
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

                float baseSize = Math.Max(config.TitleStyle.FontSize, Math.Max(config.VerseStyle.FontSize, config.VerseNumberStyle.FontSize));
                var bounds = ComputeBibleInsertTextBounds(
                    fullContent,
                    x,
                    y,
                    config.FontFamily,
                    baseSize,
                    Math.Max(1.0, config.VerseStyle.VerseSpacing),
                    maxHeightHint: maxHeightHint);

                float scale = baseSize > 0 ? bounds.AppliedFontSize / baseSize : 1f;
                float verseFontSize = Math.Max(10f, config.VerseStyle.FontSize * scale);
                float verseNumberFontSize = Math.Max(10f, config.VerseNumberStyle.FontSize * scale);
                float titleFontSize = Math.Max(10f, config.TitleStyle.FontSize * scale);

                var textElement = new Database.Models.TextElement
                {
                    SlideId = _currentSlide.Id,
                    Content = fullContent,
                    X = bounds.X,
                    Y = bounds.Y,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    FontFamily = config.FontFamily,
                    FontSize = verseFontSize,
                    FontColor = config.VerseStyle.ColorHex,
                    IsBold = config.VerseStyle.IsBold ? 1 : 0,
                    LineSpacing = config.VerseStyle.VerseSpacing,
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
                        FontSize = verseNumberFontSize,
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
                        FontSize = verseFontSize,
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
                        FontSize = verseFontSize,
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
                            FontSize = verseFontSize,
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
                    FontSize = titleFontSize,
                    FontColor = config.TitleStyle.ColorHex,
                    IsBold = config.TitleStyle.IsBold ? 1 : 0
                });

                // 保存富文本片段
                foreach (var span in richTextSpans)
                {
                    await _textProjectService.AddRichTextSpanAsync(span);
                }

                textElement.RichTextSpans = richTextSpans;

                double actualHeight = bounds.Height;
                await Dispatcher.InvokeAsync(() =>
                {
                    var textBox = new UI.Controls.DraggableTextBox(textElement);
                    AddTextBoxToCanvas(textBox);
                    actualHeight = AutoFitTextBoxHeightToContent(textBox, bounds.AppliedFontSize);
                    MarkContentAsModified();
                });

                return actualHeight;
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [圣经创建] 创建富文本经文+标题元素失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }

            return 0;
        }

        private (double X, double Y, double Width, double Height, float AppliedFontSize) ComputeBibleInsertTextBounds(
            string content,
            double preferredX,
            double preferredY,
            string fontFamily,
            float fontSize,
            double lineSpacing,
            double? maxHeightHint = null)
        {
            const double margin = 20.0;
            const double minWidth = 360.0;
            var layoutProfile = ImageColorChanger.Services.TextEditor.Models.TextLayoutProfile.Default;
            double chromeHorizontal =
                layoutProfile.RichTextBoxPadding.Left + layoutProfile.RichTextBoxPadding.Right +
                layoutProfile.DocumentPagePadding.Left + layoutProfile.DocumentPagePadding.Right;
            double chromeVertical =
                layoutProfile.RichTextBoxPadding.Top + layoutProfile.RichTextBoxPadding.Bottom +
                layoutProfile.DocumentPagePadding.Top + layoutProfile.DocumentPagePadding.Bottom;
            double horizontalPadding = Math.Max(24.0, chromeHorizontal + 6.0);
            double verticalPadding = Math.Max(18.0, chromeVertical + 8.0);

            double canvasWidth = EditorCanvas?.ActualWidth > 1 ? EditorCanvas.ActualWidth : 1600;
            double canvasHeight = EditorCanvas?.ActualHeight > 1 ? EditorCanvas.ActualHeight : 900;

            double maxWidth = Math.Max(240.0, canvasWidth - margin * 2);
            double width = Math.Min(maxWidth, Math.Max(minWidth, canvasWidth * 0.9));

            float textMaxWidth = (float)Math.Max(80.0, width - horizontalPadding);
            double maxHeightByCanvas = Math.Max(80.0, canvasHeight - margin * 2);
            double maxHeight = Math.Min(maxHeightByCanvas, maxHeightHint ?? maxHeightByCanvas);
            maxHeight = Math.Max(80.0, maxHeight);

            float minFontSize = Math.Max(10f, fontSize * 0.45f);
            double fitSafety = Math.Max(10.0, fontSize * 0.40);
            float appliedFontSize = ComputeAutoFitFontSize(
                content,
                fontFamily,
                Math.Max(12f, fontSize),
                lineSpacing,
                textMaxWidth,
                Math.Max(1.0, maxHeight - verticalPadding - fitSafety),
                minFontSize);

            double contentHeight = EstimateBibleInsertContentHeight(
                content,
                fontFamily,
                appliedFontSize,
                lineSpacing,
                textMaxWidth);

            double minHeight = Math.Max(52.0, appliedFontSize * 1.8);
            double clippingSafety = Math.Max(12.0, appliedFontSize * 0.45);
            double height = Math.Max(minHeight, contentHeight + verticalPadding + clippingSafety);
            height = Math.Min(height, maxHeight);

            double maxX = Math.Max(margin, canvasWidth - width - margin);
            double maxY = Math.Max(margin, canvasHeight - height - margin);
            double x = Math.Clamp(preferredX, margin, maxX);
            double y = Math.Clamp(preferredY, margin, maxY);

            return (x, y, width, height, appliedFontSize);
        }

        private double GetBibleInsertPreferredWidth()
        {
            double canvasWidth = EditorCanvas?.ActualWidth > 1 ? EditorCanvas.ActualWidth : 1600;
            const double margin = 20.0;
            const double minWidth = 360.0;
            double maxWidth = Math.Max(240.0, canvasWidth - margin * 2);
            return Math.Min(maxWidth, Math.Max(minWidth, canvasWidth * 0.9));
        }

        private double GetAvailableInsertHeight(double startY)
        {
            const double margin = 20.0;
            double canvasHeight = EditorCanvas?.ActualHeight > 1 ? EditorCanvas.ActualHeight : 900;
            return Math.Max(80.0, canvasHeight - margin - Math.Max(margin, startY));
        }

        private static double EstimateBibleInsertContentHeight(
            string content,
            string fontFamily,
            float fontSize,
            double lineSpacing,
            float maxWidth)
        {
            string normalized = string.IsNullOrWhiteSpace(content)
                ? " "
                : content.Replace("\r\n", "\n");

            using var font = new SKFont
            {
                Typeface = SKTypeface.FromFamilyName(string.IsNullOrWhiteSpace(fontFamily) ? "Microsoft YaHei UI" : fontFamily),
                Size = Math.Max(12f, fontSize),
                Subpixel = true,
                Edging = SKFontEdging.Antialias
            };
            using var paint = new SKPaint { IsAntialias = true };

            float effectiveLineHeight = Math.Max(
                font.Size * (float)Math.Max(1.0, lineSpacing),
                font.Size * 1.2f);

            int totalLines = 0;
            foreach (var paragraph in normalized.Split('\n'))
            {
                string text = string.IsNullOrEmpty(paragraph) ? " " : paragraph;
                var wrapped = WrapTextByWidth(text, font, paint, maxWidth);
                totalLines += Math.Max(1, wrapped.Count);
            }

            return Math.Max(1, totalLines) * effectiveLineHeight;
        }

        private static float ComputeAutoFitFontSize(
            string content,
            string fontFamily,
            float preferredFontSize,
            double lineSpacing,
            float maxWidth,
            double maxContentHeight,
            float minFontSize)
        {
            float preferred = Math.Max(10f, preferredFontSize);
            float minSize = Math.Clamp(minFontSize, 8f, preferred);
            double targetHeight = Math.Max(1.0, maxContentHeight);

            double preferredHeight = EstimateBibleInsertContentHeight(content, fontFamily, preferred, lineSpacing, maxWidth);
            if (preferredHeight <= targetHeight)
            {
                return preferred;
            }

            double minHeight = EstimateBibleInsertContentHeight(content, fontFamily, minSize, lineSpacing, maxWidth);
            if (minHeight >= targetHeight)
            {
                return minSize;
            }

            float low = minSize;
            float high = preferred;
            for (int i = 0; i < 14; i++)
            {
                float mid = (low + high) / 2f;
                double midHeight = EstimateBibleInsertContentHeight(content, fontFamily, mid, lineSpacing, maxWidth);
                if (midHeight <= targetHeight)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        private double AutoFitTextBoxHeightToContent(UI.Controls.DraggableTextBox textBox, float referenceFontSize)
        {
            if (textBox?.RichTextBox == null || textBox.Data == null)
            {
                return textBox?.Data?.Height ?? 0;
            }

            textBox.UpdateLayout();
            textBox.RichTextBox.UpdateLayout();

            var richTextBox = textBox.RichTextBox;
            var doc = richTextBox.Document;
            var richPadding = richTextBox.Padding;
            var docPadding = doc?.PagePadding ?? new Thickness(0);

            // 优先使用字符矩形差值，获得更紧凑的真实文字高度。
            double contentHeight = 0;
            if (doc != null)
            {
                var startRect = doc.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
                var endRect = doc.ContentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
                contentHeight = Math.Max(0, endRect.Bottom - startRect.Top);
            }

            // 回退到 ExtentHeight（去掉 PagePadding）避免某些极端情况下字符矩形不可用。
            if (contentHeight <= 0.1 || double.IsNaN(contentHeight) || double.IsInfinity(contentHeight))
            {
                contentHeight = richTextBox.ExtentHeight;
                if (contentHeight > 0.1)
                {
                    contentHeight = Math.Max(0, contentHeight - docPadding.Top - docPadding.Bottom);
                }
            }

            if (contentHeight <= 0.1 || double.IsNaN(contentHeight) || double.IsInfinity(contentHeight))
            {
                contentHeight = Math.Max(12.0, referenceFontSize * 1.2);
            }

            // 保留很小的安全边距，减少“框偏大”。
            double safety = Math.Max(2.0, referenceFontSize * 0.06);
            double desired = contentHeight +
                             richPadding.Top + richPadding.Bottom +
                             docPadding.Top + docPadding.Bottom +
                             safety;

            double minHeight = Math.Max(28.0, referenceFontSize * 0.9 + richPadding.Top + richPadding.Bottom);

            double canvasHeight = EditorCanvas?.ActualHeight > 1 ? EditorCanvas.ActualHeight : 900;
            double maxHeight = Math.Max(minHeight, canvasHeight - Math.Max(20.0, textBox.Data.Y) - 20.0);
            double finalHeight = Math.Clamp(desired, minHeight, maxHeight);

            if (Math.Abs(textBox.Data.Height - finalHeight) > 0.5)
            {
                textBox.Data.Height = finalHeight;
                textBox.Height = finalHeight;
            }

            return finalHeight;
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
            var popupPosition = dbManager.GetBibleInsertConfigValue("popup_position", "Top");
            config.PopupPosition = popupPosition switch
            {
                "Top" => BiblePopupPosition.Top,
                "Center" => BiblePopupPosition.Center,
                _ => BiblePopupPosition.Bottom
            };
            config.PopupFontFamily = dbManager.GetBibleInsertConfigValue("popup_font_family", "Microsoft YaHei");
            if (!int.TryParse(dbManager.GetBibleInsertConfigValue("popup_title_format", "0"), out var popupTitleFormat))
            {
                popupTitleFormat = 0;
            }
            config.PopupTitleFormat = Enum.IsDefined(typeof(BiblePopupTitleFormat), popupTitleFormat)
                ? (BiblePopupTitleFormat)popupTitleFormat
                : BiblePopupTitleFormat.DotChapterVerse;
            config.PopupTitleStyle.ColorHex = dbManager.GetBibleInsertConfigValue("popup_title_color", config.TitleStyle.ColorHex);
            config.PopupTitleStyle.FontSize = float.Parse(dbManager.GetBibleInsertConfigValue("popup_title_size", config.TitleStyle.FontSize.ToString()));
            config.PopupTitleStyle.IsBold = dbManager.GetBibleInsertConfigValue("popup_title_bold", config.TitleStyle.IsBold ? "1" : "0") == "1";
            config.PopupVerseStyle.ColorHex = dbManager.GetBibleInsertConfigValue("popup_verse_color", config.VerseStyle.ColorHex);
            config.PopupVerseStyle.FontSize = float.Parse(dbManager.GetBibleInsertConfigValue("popup_verse_size", config.VerseStyle.FontSize.ToString()));
            config.PopupVerseStyle.IsBold = dbManager.GetBibleInsertConfigValue("popup_verse_bold", config.VerseStyle.IsBold ? "1" : "0") == "1";
            config.PopupVerseStyle.VerseSpacing = float.Parse(dbManager.GetBibleInsertConfigValue("popup_verse_spacing", config.VerseStyle.VerseSpacing.ToString("F1")));
            config.PopupVerseNumberStyle.ColorHex = dbManager.GetBibleInsertConfigValue("popup_verse_number_color", config.VerseNumberStyle.ColorHex);
            config.PopupVerseNumberStyle.FontSize = float.Parse(dbManager.GetBibleInsertConfigValue("popup_verse_number_size", config.VerseNumberStyle.FontSize.ToString()));
            config.PopupVerseNumberStyle.IsBold = dbManager.GetBibleInsertConfigValue("popup_verse_number_bold", config.VerseNumberStyle.IsBold ? "1" : "0") == "1";
            config.PopupBackgroundColorHex = dbManager.GetBibleInsertConfigValue("popup_bg_color", "#1C2740");
            if (!int.TryParse(dbManager.GetBibleInsertConfigValue("popup_bg_opacity", "0"), out var popupOpacity))
            {
                popupOpacity = 0;
            }
            config.PopupBackgroundOpacity = Math.Clamp(popupOpacity, 0, 100);

            if (!int.TryParse(dbManager.GetBibleInsertConfigValue("popup_duration_minutes", "3"), out var popupDurationMinutes))
            {
                popupDurationMinutes = 3;
            }
            config.PopupDurationMinutes = popupDurationMinutes;

            if (!int.TryParse(dbManager.GetBibleInsertConfigValue("popup_verse_count", "4"), out var popupVerseCount))
            {
                popupVerseCount = 4;
            }
            config.PopupVerseCount = popupVerseCount;

            if (!int.TryParse(dbManager.GetBibleInsertConfigValue("slide_pinyin_quick_locate_action", "1"), out var quickLocateAction))
            {
                quickLocateAction = 1;
            }
            config.QuickLocateSlideAction = Enum.IsDefined(typeof(BibleQuickLocateSlideAction), quickLocateAction)
                ? (BibleQuickLocateSlideAction)quickLocateAction
                : BibleQuickLocateSlideAction.DirectInsert;

            config.PopupHideSlideContent = dbManager.GetBibleInsertConfigValue("popup_hide_slide_content", "0") == "1";
            
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
