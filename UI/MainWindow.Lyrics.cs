using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using ImageColorChanger.UI.Modules;
using static ImageColorChanger.Core.Constants;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSize = System.Windows.Size;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的歌词编辑功能分部类
    /// </summary>
    public partial class MainWindow
    {
        private LyricsModuleController _lyricsModuleController;

        // ============================================
        // 字段
        // ============================================
        
        private bool _isLyricsMode = false; // 是否处于歌词模式
        private LyricsProject _currentLyricsProject = null; // 当前歌词项目
        private System.Windows.Threading.DispatcherTimer _lyricsAutoSaveTimer; // 自动保存计时器
        private int _lyricsSplitMode = (int)ViewSplitMode.Single; // 歌词分割模式
        private WpfTextBox _activeLyricsEditor = null; // 当前激活的歌词输入框
        private bool _lyricsSplitBorderVisible = true; // 是否显示分割线
        private bool _lyricsPagingMode = false; // 分页模式开关（仅分割模式下有效）
        private bool _isSyncingPagingEditor = false; // 防止分页区域切换时 TextChanged 误回写
        private LyricsProjectManager _lyricsProjectManager;
        private const string LyricsModeContentPrefix = "__LYRICS_MODE_V1__";
        private const string LyricsSplitContentPrefix = "__LYRICS_SPLIT_V1__";
        private const string LyricsPagesContentPrefix = "__LYRICS_PAGES_V2__";
        private const double DefaultLyricsFontSize = 88;
        private const double MinLyricsFontSize = 20;
        private const double MaxLyricsFontSize = 250;
        private const double LyricsFontWheelStep = 4;
        private const double LyricsFontWheelFastStep = 8;
        private readonly List<LyricsSplitContentData> _lyricsSplitPages = new();
        private int _lyricsCurrentPageIndex = 0;

        // ============================================
        // 公共属性
        // ============================================
        
        /// <summary>
        /// 是否处于歌词模式（供ProjectionManager访问）
        /// </summary>
        public bool IsInLyricsMode => _isLyricsMode;

        private sealed class LyricsSplitContentData
        {
            public int SplitMode { get; set; }
            public string[] Regions { get; set; } = new string[4];
            public LyricsSplitRegionStyle[] RegionStyles { get; set; } = new LyricsSplitRegionStyle[4];
        }

        private sealed class LyricsPagesContentData
        {
            public int CurrentPageIndex { get; set; } = 0;
            public List<LyricsSplitContentData> Pages { get; set; } = new();
        }

        private sealed class LyricsModeContentData
        {
            public string SingleContent { get; set; } = "";
            public LyricsSplitContentData SplitContent { get; set; } = new LyricsSplitContentData();
            public int ActiveMode { get; set; } = (int)ViewSplitMode.Single;
        }

        private sealed class LyricsSplitRegionStyle
        {
            public double FontSize { get; set; } = DefaultLyricsFontSize;
            public string TextAlign { get; set; } = "Center";
            public string ColorHex { get; set; } = "";
        }

        private IEnumerable<WpfTextBox> GetCurrentLyricsEditors()
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                yield return LyricsTextBox;
                yield break;
            }

            switch ((ViewSplitMode)_lyricsSplitMode)
            {
                case ViewSplitMode.Horizontal:
                case ViewSplitMode.Vertical:
                    yield return LyricsSplitTextBox1;
                    yield return LyricsSplitTextBox2;
                    break;
                case ViewSplitMode.TripleSplit:
                    yield return LyricsSplitTextBox1;
                    yield return LyricsSplitTextBox2;
                    yield return LyricsSplitTextBox3;
                    break;
                case ViewSplitMode.Quad:
                    yield return LyricsSplitTextBox1;
                    yield return LyricsSplitTextBox2;
                    yield return LyricsSplitTextBox3;
                    yield return LyricsSplitTextBox4;
                    break;
                default:
                    yield return LyricsSplitTextBox1;
                    break;
            }
        }

        private WpfTextBox GetActiveLyricsEditor()
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                return LyricsTextBox;
            }

            if (_activeLyricsEditor != null && GetCurrentLyricsEditors().Contains(_activeLyricsEditor))
            {
                return _activeLyricsEditor;
            }

            return GetCurrentLyricsEditors().FirstOrDefault() ?? LyricsSplitTextBox1;
        }

        private WpfTextBox GetSplitEditorByRegionIndex(int index)
        {
            return index switch
            {
                0 => LyricsSplitTextBox1,
                1 => LyricsSplitTextBox2,
                2 => LyricsSplitTextBox3,
                3 => LyricsSplitTextBox4,
                _ => LyricsSplitTextBox1
            };
        }

        private void SyncSplitRegionFromPagingEditor()
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single || !_lyricsPagingMode)
            {
                return;
            }

            var target = GetSplitEditorByRegionIndex(ClampPagingRegionIndex(_lyricsCurrentPageIndex));
            target.Text = LyricsTextBox.Text ?? "";
            target.FontSize = LyricsTextBox.FontSize;
            target.TextAlignment = LyricsTextBox.TextAlignment;
            target.Foreground = LyricsTextBox.Foreground;
            SaveCurrentSplitPageFromUi();
        }

        private void SyncPagingEditorFromSplitRegion()
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single || !_lyricsPagingMode)
            {
                return;
            }

            var source = GetSplitEditorByRegionIndex(ClampPagingRegionIndex(_lyricsCurrentPageIndex));
            try
            {
                _isSyncingPagingEditor = true;
                LyricsTextBox.Text = source.Text ?? "";
                LyricsTextBox.FontSize = source.FontSize;
                LyricsTextBox.TextAlignment = source.TextAlignment;
                LyricsTextBox.Foreground = source.Foreground;
                LyricsTextBox.Visibility = Visibility.Visible;
                LyricsSplitGrid.Visibility = Visibility.Collapsed;
                LyricsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                _activeLyricsEditor = LyricsTextBox;
                LyricsFontSizeDisplay.Text = LyricsTextBox.FontSize.ToString("0");
                UpdateAlignmentButtonsState(LyricsTextBox.TextAlignment);
                LyricsTextBox.Focus();
            }
            finally
            {
                _isSyncingPagingEditor = false;
            }
        }

        private void RestoreSplitEditorView()
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                return;
            }

            LyricsTextBox.Visibility = Visibility.Collapsed;
            LyricsSplitGrid.Visibility = Visibility.Visible;
            LyricsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _activeLyricsEditor = GetSplitEditorByRegionIndex(ClampPagingRegionIndex(_lyricsCurrentPageIndex));
            LyricsFontSizeDisplay.Text = _activeLyricsEditor.FontSize.ToString("0");
            UpdateAlignmentButtonsState(_activeLyricsEditor.TextAlignment);
            _activeLyricsEditor.Focus();
        }

        private void ApplyLyricsEditorStyleToCurrentMode(Action<WpfTextBox> action)
        {
            foreach (var textBox in GetCurrentLyricsEditors())
            {
                action(textBox);
            }
        }

        private void ApplyLyricsEditorStyleToActiveEditor(Action<WpfTextBox> action)
        {
            action(GetActiveLyricsEditor());
        }

        private TextAlignment ParseTextAlignmentOrDefault(string value, TextAlignment fallback = TextAlignment.Center)
        {
            if (Enum.TryParse<TextAlignment>(value, out var alignment))
            {
                return alignment;
            }
            return fallback;
        }

        private LyricsSplitContentData CreateDefaultSplitPage(ViewSplitMode mode, string seedText = "")
        {
            var page = new LyricsSplitContentData
            {
                SplitMode = (int)mode,
                Regions = new[] { seedText ?? "", "", "", "" },
                RegionStyles = Enumerable.Range(0, 4).Select(_ => new LyricsSplitRegionStyle()).ToArray()
            };
            return page;
        }

        private int GetSplitRegionCount(ViewSplitMode mode)
        {
            switch (mode)
            {
                case ViewSplitMode.Horizontal:
                case ViewSplitMode.Vertical:
                    return 2;
                case ViewSplitMode.TripleSplit:
                    return 3;
                case ViewSplitMode.Quad:
                    return 4;
                default:
                    return 1;
            }
        }

        private int GetCurrentSplitRegionCount()
        {
            return GetSplitRegionCount((ViewSplitMode)_lyricsSplitMode);
        }

        private int ClampPagingRegionIndex(int index)
        {
            return Math.Clamp(index, 0, Math.Max(0, GetCurrentSplitRegionCount() - 1));
        }

        private void NormalizeSplitPages()
        {
            if (_lyricsSplitPages.Count == 0)
            {
                _lyricsSplitPages.Add(CreateDefaultSplitPage((ViewSplitMode)_lyricsSplitMode));
            }

            var page = _lyricsSplitPages[0] ?? CreateDefaultSplitPage((ViewSplitMode)_lyricsSplitMode);
            if (_lyricsSplitPages.Count > 1)
            {
                _lyricsSplitPages.Clear();
                _lyricsSplitPages.Add(page);
                LogPagingDebug("[NormalizePages] collapsed to single split data");
            }

            if (page.Regions == null || page.Regions.Length < 4)
            {
                page.Regions = (page.Regions ?? Array.Empty<string>())
                    .Concat(Enumerable.Repeat(string.Empty, 4))
                    .Take(4)
                    .ToArray();
            }

            var styles = page.RegionStyles ?? Array.Empty<LyricsSplitRegionStyle>();
            if (styles.Length < 4)
            {
                styles = styles.Concat(Enumerable.Range(0, 4 - styles.Length).Select(_ => new LyricsSplitRegionStyle())).ToArray();
            }
            page.RegionStyles = styles.Take(4).Select(s => s ?? new LyricsSplitRegionStyle()).ToArray();
            page.SplitMode = _lyricsSplitMode;

            _lyricsCurrentPageIndex = ClampPagingRegionIndex(_lyricsCurrentPageIndex);
        }

        private LyricsSplitContentData GetCurrentSplitPage()
        {
            NormalizeSplitPages();
            return _lyricsSplitPages[0];
        }

        private void EnsureSplitPagesInitialized(ViewSplitMode mode, string seedText = "")
        {
            NormalizeSplitPages();
            var page = _lyricsSplitPages[0];
            page.SplitMode = (int)mode;
            if (string.IsNullOrWhiteSpace(page.Regions[0]) && !string.IsNullOrWhiteSpace(seedText))
            {
                page.Regions[0] = seedText;
            }
        }

        private void SaveCurrentSplitPageFromUi()
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                return;
            }

            var page = GetCurrentSplitPage();
            page.SplitMode = _lyricsSplitMode;
            page.Regions = new[]
            {
                LyricsSplitTextBox1.Text ?? "",
                LyricsSplitTextBox2.Text ?? "",
                LyricsSplitTextBox3.Text ?? "",
                LyricsSplitTextBox4.Text ?? ""
            };

            var splitEditors = new[] { LyricsSplitTextBox1, LyricsSplitTextBox2, LyricsSplitTextBox3, LyricsSplitTextBox4 };
            page.RegionStyles = splitEditors.Select(tb => new LyricsSplitRegionStyle
            {
                FontSize = tb.FontSize,
                TextAlign = tb.TextAlignment.ToString(),
                ColorHex = ColorToHex((tb.Foreground as SolidColorBrush)?.Color ?? HexToColor(_configManager.DefaultLyricsColor))
            }).ToArray();
        }

        private void LoadCurrentSplitPageToUi()
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                return;
            }

            var page = GetCurrentSplitPage();
            if (page.Regions == null || page.Regions.Length < 4)
            {
                page.Regions = (page.Regions ?? Array.Empty<string>())
                    .Concat(Enumerable.Repeat(string.Empty, 4))
                    .Take(4)
                    .ToArray();
            }

            var styles = page.RegionStyles ?? Array.Empty<LyricsSplitRegionStyle>();
            if (styles.Length < 4)
            {
                styles = styles.Concat(Enumerable.Range(0, 4 - styles.Length).Select(_ => new LyricsSplitRegionStyle())).ToArray();
            }

            var splitEditors = new[] { LyricsSplitTextBox1, LyricsSplitTextBox2, LyricsSplitTextBox3, LyricsSplitTextBox4 };
            LyricsSplitTextBox1.Text = page.Regions[0] ?? "";
            LyricsSplitTextBox2.Text = page.Regions[1] ?? "";
            LyricsSplitTextBox3.Text = page.Regions[2] ?? "";
            LyricsSplitTextBox4.Text = page.Regions[3] ?? "";

            for (int i = 0; i < splitEditors.Length; i++)
            {
                var style = styles[i] ?? new LyricsSplitRegionStyle();
                splitEditors[i].FontSize = style.FontSize > 0 ? style.FontSize : DefaultLyricsFontSize;
                splitEditors[i].TextAlignment = ParseTextAlignmentOrDefault(style.TextAlign, TextAlignment.Center);
                splitEditors[i].Foreground = new SolidColorBrush(HexToColor(
                    string.IsNullOrWhiteSpace(style.ColorHex) ? _configManager.DefaultLyricsColor : style.ColorHex));
            }

            _activeLyricsEditor = splitEditors[0];
            LyricsFontSizeDisplay.Text = _activeLyricsEditor.FontSize.ToString("0");
            UpdateAlignmentButtonsState(_activeLyricsEditor.TextAlignment);
        }

        private void ShowSplitPageStatus()
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                return;
            }

            if (_lyricsPagingMode)
            {
                ShowStatus($"📄 分页 {(_lyricsCurrentPageIndex + 1)}/{GetCurrentSplitRegionCount()}");
            }
            else
            {
                ShowStatus($"✨ 分割模式：{(ViewSplitMode)_lyricsSplitMode}");
            }
        }

        private void UpdatePagingNavVisibility()
        {
            if (LyricsPageNavPanel == null)
            {
                return;
            }

            bool visible = _lyricsSplitMode != (int)ViewSplitMode.Single;
            LyricsPageNavPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                return;
            }

            BtnLyricsPagingToggle.Content = "分页";
            BtnLyricsPagingToggle.Background = _lyricsPagingMode
                ? new SolidColorBrush(WpfColor.FromRgb(76, 175, 80))
                : new SolidColorBrush(WpfColor.FromRgb(44, 44, 44));
            BtnLyricsPagingToggle.BorderBrush = _lyricsPagingMode
                ? new SolidColorBrush(WpfColor.FromRgb(129, 199, 132))
                : new SolidColorBrush(WpfColor.FromRgb(68, 68, 68));
            BtnLyricsPagingToggle.Foreground = WpfBrushes.White;
            int total = GetCurrentSplitRegionCount();
            BtnLyricsPageUp.IsEnabled = true;
            BtnLyricsPageDown.IsEnabled = true;
            if (LyricsPagingStateText != null)
            {
                int current = _lyricsPagingMode
                    ? Math.Clamp(_lyricsCurrentPageIndex + 1, 1, total)
                    : 1;
                LyricsPagingStateText.Text = $"{current}/{total}";
            }
        }

        private void SetLyricsPagingMode(bool enabled)
        {
            LogPagingDebug($"[Toggle-Begin] enabled={enabled}, splitMode={_lyricsSplitMode}, paging={_lyricsPagingMode}, region={_lyricsCurrentPageIndex + 1}, totalRegion={GetCurrentSplitRegionCount()}");
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                _lyricsPagingMode = false;
                UpdatePagingNavVisibility();
                ShowStatus("⚠️ 请先选择分割模式，再进入分页");
                LogPagingDebug("[Toggle-End] blocked: split single");
                return;
            }

            SaveCurrentSplitPageFromUi();
            NormalizeSplitPages();
            _lyricsPagingMode = enabled;
            _lyricsCurrentPageIndex = enabled ? 0 : ClampPagingRegionIndex(_lyricsCurrentPageIndex);

            if (_lyricsPagingMode)
            {
                RestoreSplitEditorView();
                ShowStatus($"✅ 已进入分页（{_lyricsCurrentPageIndex + 1}/{GetCurrentSplitRegionCount()}）");
                SyncPagingEditorFromSplitRegion();
            }
            else
            {
                SyncSplitRegionFromPagingEditor();
                _lyricsCurrentPageIndex = 0;
                RestoreSplitEditorView();
                ShowStatus("✅ 已退出分页，恢复分割显示");
            }

            UpdatePagingNavVisibility();

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
            LogPagingDebug($"[Toggle-End] paging={_lyricsPagingMode}, region={_lyricsCurrentPageIndex + 1}, totalRegion={GetCurrentSplitRegionCount()}");
        }


        private void SetLyricsSplitMode(ViewSplitMode mode, bool keepTextBridge = false)
        {
            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                SaveCurrentSplitPageFromUi();
            }

            _lyricsSplitMode = (int)mode;

            if (mode == ViewSplitMode.Single)
            {
                _lyricsPagingMode = false;
                if (keepTextBridge)
                {
                    LyricsTextBox.Text = string.Join(
                        Environment.NewLine + Environment.NewLine,
                        new[] { LyricsSplitTextBox1.Text, LyricsSplitTextBox2.Text, LyricsSplitTextBox3.Text, LyricsSplitTextBox4.Text }
                            .Where(t => !string.IsNullOrWhiteSpace(t)));
                }

                LyricsTextBox.Visibility = Visibility.Visible;
                LyricsSplitGrid.Visibility = Visibility.Collapsed;
                LyricsSplitRegion1.Visibility = Visibility.Collapsed;
                LyricsSplitRegion2.Visibility = Visibility.Collapsed;
                LyricsSplitRegion3.Visibility = Visibility.Collapsed;
                LyricsSplitRegion4.Visibility = Visibility.Collapsed;
                _activeLyricsEditor = LyricsTextBox;
                LyricsFontSizeDisplay.Text = LyricsTextBox.FontSize.ToString("0");
                UpdateAlignmentButtonsState(LyricsTextBox.TextAlignment);
                LyricsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                UpdatePagingNavVisibility();
                return;
            }

            LyricsTextBox.Visibility = Visibility.Collapsed;
            LyricsSplitGrid.Visibility = Visibility.Visible;
            LyricsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            EnsureSplitPagesInitialized(mode, keepTextBridge ? LyricsTextBox.Text : "");
            GetCurrentSplitPage().SplitMode = (int)mode;
            _lyricsCurrentPageIndex = ClampPagingRegionIndex(_lyricsCurrentPageIndex);

            ConfigureSplitRegion(LyricsSplitRegion1, 0, 0, 1, 1, false);
            ConfigureSplitRegion(LyricsSplitRegion2, 0, 1, 1, 1, false);
            ConfigureSplitRegion(LyricsSplitRegion3, 1, 0, 1, 1, false);
            ConfigureSplitRegion(LyricsSplitRegion4, 1, 1, 1, 1, false);

            switch (mode)
            {
                case ViewSplitMode.Horizontal:
                    ConfigureSplitRegion(LyricsSplitRegion1, 0, 0, 2, 1, true);
                    ConfigureSplitRegion(LyricsSplitRegion2, 0, 1, 2, 1, true);
                    break;
                case ViewSplitMode.Vertical:
                    ConfigureSplitRegion(LyricsSplitRegion1, 0, 0, 1, 2, true);
                    ConfigureSplitRegion(LyricsSplitRegion2, 1, 0, 1, 2, true);
                    break;
                case ViewSplitMode.TripleSplit:
                    ConfigureSplitRegion(LyricsSplitRegion1, 0, 0, 1, 1, true);
                    ConfigureSplitRegion(LyricsSplitRegion2, 1, 0, 1, 1, true);
                    ConfigureSplitRegion(LyricsSplitRegion3, 0, 1, 2, 1, true);
                    break;
                case ViewSplitMode.Quad:
                    ConfigureSplitRegion(LyricsSplitRegion1, 0, 0, 1, 1, true);
                    ConfigureSplitRegion(LyricsSplitRegion2, 0, 1, 1, 1, true);
                    ConfigureSplitRegion(LyricsSplitRegion3, 1, 0, 1, 1, true);
                    ConfigureSplitRegion(LyricsSplitRegion4, 1, 1, 1, 1, true);
                    break;
            }

            LoadCurrentSplitPageToUi();
            if (_lyricsPagingMode)
            {
                SyncPagingEditorFromSplitRegion();
            }
            else
            {
                _activeLyricsEditor?.Focus();
            }
            ShowSplitPageStatus();
            UpdatePagingNavVisibility();
        }

        private void ConfigureSplitRegion(Border region, int row, int column, int rowSpan, int columnSpan, bool visible)
        {
            region.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetRow(region, row);
            Grid.SetColumn(region, column);
            Grid.SetRowSpan(region, rowSpan);
            Grid.SetColumnSpan(region, columnSpan);
            region.BorderThickness = visible && _lyricsSplitBorderVisible ? new Thickness(1) : new Thickness(0);
        }

        private void RefreshSplitBorders()
        {
            var visibleThickness = _lyricsSplitBorderVisible ? new Thickness(1) : new Thickness(0);
            if (LyricsSplitRegion1.Visibility == Visibility.Visible) LyricsSplitRegion1.BorderThickness = visibleThickness;
            if (LyricsSplitRegion2.Visibility == Visibility.Visible) LyricsSplitRegion2.BorderThickness = visibleThickness;
            if (LyricsSplitRegion3.Visibility == Visibility.Visible) LyricsSplitRegion3.BorderThickness = visibleThickness;
            if (LyricsSplitRegion4.Visibility == Visibility.Visible) LyricsSplitRegion4.BorderThickness = visibleThickness;
        }

        // ============================================
        // 进入/退出歌词模式
        // ============================================

        // 浮动歌词按钮已删除，通过右键菜单进入歌词模式

        /// <summary>
        /// 退出按钮点击事件
        /// </summary>
        private void BtnCloseLyricsEditor_Click(object sender, RoutedEventArgs e)
        {
            ExitLyricsMode();
        }

        /// <summary>
        /// 进入歌词编辑模式
        /// </summary>
        private void EnterLyricsMode()
        {
//#if DEBUG
//            Debug.WriteLine("[歌词] 进入歌词模式");
//#endif

            // 隐藏其他显示区域
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            VideoContainer.Visibility = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Collapsed;

            // 显示歌词编辑面板
            LyricsEditorPanel.Visibility = Visibility.Visible;

            // 加载或创建歌词项目
            LoadOrCreateLyricsProject();

            // 聚焦到文本框
            Dispatcher.InvokeAsync(() =>
            {
                GetActiveLyricsEditor().Focus();
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            // 🔧 隐藏合成播放按钮面板（歌词模式不需要）
            CompositePlaybackPanel.Visibility = Visibility.Collapsed;

            // 启动自动保存计时器（每30秒保存一次）
            StartAutoSaveTimer();

            // 🔧 如果投影已开启，先清空图片投影状态，再投影歌词
//#if DEBUG
//            Debug.WriteLine($"[歌词] 检查投影状态 - _projectionManager: {_projectionManager != null}, IsProjecting: {_projectionManager?.IsProjecting}");
//#endif

            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 投影已开启，先清空图片状态");
//#endif
                // 清空投影的图片状态（歌词模式不使用图片）
                _projectionManager.ClearImageState();
                
//#if DEBUG
//                Debug.WriteLine("[歌词] 准备渲染歌词");
//#endif
                RenderLyricsToProjection();
//#if DEBUG
//                Debug.WriteLine("[歌词] 进入模式时自动投影完成");
//#endif
            }
            else
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 投影未开启，跳过投影");
//#endif
            }

            _isLyricsMode = true;
        }

        /// <summary>
        /// 退出歌词编辑模式
        /// </summary>
        private void ExitLyricsMode()
        {
//#if DEBUG
//            Debug.WriteLine("[歌词] 退出歌词模式");
//#endif

            // 停止自动保存计时器
            StopAutoSaveTimer();

            // 保存当前内容
            SaveLyricsProject();

            // 隐藏歌词编辑面板
            LyricsEditorPanel.Visibility = Visibility.Collapsed;

            // 显示图片浏览区域
            ImageScrollViewer.Visibility = Visibility.Visible;

            // 🔧 先设置标志为false，再恢复合成播放按钮的显示状态
            _isLyricsMode = false;

            // 🔧 恢复合成播放按钮的显示状态
            UpdateFloatingCompositePlayButton();

            // 🔧 如果投影已开启，恢复图片投影（刷新当前图片）
            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 退出歌词模式，恢复图片投影");
//#endif
                UpdateProjection();
            }
        }

        // ============================================
        // 字号调整（鼠标滚轮）
        // ============================================

        /// <summary>
        /// 字号显示区域 - 鼠标滚轮调整字号
        /// </summary>
        private void LyricsFontSizeDisplay_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            AdjustLyricsFontSizeByWheel(e, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }

        private void AdjustLyricsFontSizeByWheel(MouseWheelEventArgs e, bool fastMode)
        {
            double step = fastMode ? LyricsFontWheelFastStep : LyricsFontWheelStep;
            double delta = e.Delta > 0 ? step : -step;
            AdjustLyricsFontSize(delta);
            e.Handled = true;
        }

        private void AdjustLyricsFontSize(double delta)
        {
            var activeEditor = GetActiveLyricsEditor();
            double currentSize = activeEditor.FontSize;
            double newSize = Math.Clamp(currentSize + delta, MinLyricsFontSize, MaxLyricsFontSize);
            if (Math.Abs(newSize - currentSize) < 0.001)
            {
                return;
            }

            ApplyLyricsEditorStyleToActiveEditor(tb => tb.FontSize = newSize);
            LyricsFontSizeDisplay.Text = newSize.ToString("0");

            if (_lyricsPagingMode && _lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                SyncSplitRegionFromPagingEditor();
            }

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        // ============================================
        // 文字颜色
        // ============================================

        /// <summary>
        /// 颜色按钮点击 - 打开颜色选择器
        /// </summary>
        private void BtnLyricsTextColor_Click(object sender, RoutedEventArgs e)
        {
            OpenLyricsCustomColorPicker();
        }

        /// <summary>
        /// 打开自定义颜色选择器
        /// </summary>
        private void OpenLyricsCustomColorPicker()
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();

            // 设置默认颜色为当前颜色
            var currentColor = (GetActiveLyricsEditor().Foreground as System.Windows.Media.SolidColorBrush)?.Color 
                ?? HexToColor(_configManager.DefaultLyricsColor);
            colorDialog.Color = System.Drawing.Color.FromArgb(
                currentColor.A, currentColor.R, currentColor.G, currentColor.B);

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = colorDialog.Color;
                SetLyricsColor(color.R, color.G, color.B);
                if (_lyricsSplitMode == (int)ViewSplitMode.Single)
                {
                    ShowStatus("✨ 全局歌词颜色已更新");
                }
                else
                {
                    ShowStatus("✨ 当前分区歌词颜色已更新");
                }

//#if DEBUG
//                Debug.WriteLine($"[歌词-全局] 自定义颜色: #{color.R:X2}{color.G:X2}{color.B:X2}");
//#endif
            }
        }

        /// <summary>
        /// 设置歌词颜色（全局设置，应用到所有歌词）
        /// </summary>
        private void SetLyricsColor(byte r, byte g, byte b)
        {
            // 转换为十六进制格式
            string hexColor = $"#{r:X2}{g:X2}{b:X2}";

            bool isSplitMode = _lyricsSplitMode != (int)ViewSplitMode.Single;
            if (!isSplitMode)
            {
                // 单画面仍然沿用全局默认颜色行为
                _configManager.DefaultLyricsColor = hexColor;
            }

//#if DEBUG
//            Debug.WriteLine($"[歌词-全局] 颜色更改为 {hexColor}");
//#endif

            // 更新当前UI显示
            var brush = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(r, g, b));
            if (isSplitMode)
            {
                ApplyLyricsEditorStyleToActiveEditor(tb => tb.Foreground = brush);
            }
            else
            {
                ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = brush);
            }
            if (_lyricsPagingMode && isSplitMode)
            {
                SyncSplitRegionFromPagingEditor();
            }

            // 颜色改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        // ============================================
        // 对齐方式
        // ============================================

        /// <summary>
        /// 左对齐
        /// </summary>
        private void BtnLyricsAlignLeft_Click(object sender, RoutedEventArgs e)
        {
            ApplyLyricsEditorStyleToActiveEditor(tb => tb.TextAlignment = TextAlignment.Left);
            if (_lyricsPagingMode && _lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                SyncSplitRegionFromPagingEditor();
            }
            UpdateAlignmentButtonsState(TextAlignment.Left);

//#if DEBUG
//            Debug.WriteLine("[歌词] 切换到左对齐");
//#endif

            // 对齐方式改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        /// <summary>
        /// 居中对齐
        /// </summary>
        private void BtnLyricsAlignCenter_Click(object sender, RoutedEventArgs e)
        {
            ApplyLyricsEditorStyleToActiveEditor(tb => tb.TextAlignment = TextAlignment.Center);
            if (_lyricsPagingMode && _lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                SyncSplitRegionFromPagingEditor();
            }
            UpdateAlignmentButtonsState(TextAlignment.Center);

//#if DEBUG
//            Debug.WriteLine("[歌词] 切换到居中对齐");
//#endif

            // 对齐方式改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        /// <summary>
        /// 右对齐
        /// </summary>
        private void BtnLyricsAlignRight_Click(object sender, RoutedEventArgs e)
        {
            ApplyLyricsEditorStyleToActiveEditor(tb => tb.TextAlignment = TextAlignment.Right);
            if (_lyricsPagingMode && _lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                SyncSplitRegionFromPagingEditor();
            }
            UpdateAlignmentButtonsState(TextAlignment.Right);

//#if DEBUG
//            Debug.WriteLine("[歌词] 切换到右对齐");
//#endif

            // 对齐方式改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        /// <summary>
        /// 更新对齐按钮的视觉状态
        /// </summary>
        private void UpdateAlignmentButtonsState(TextAlignment alignment)
        {
            // 🔧 重新设计的视觉反馈：使用深色背景+橙色高亮
            var normalBrush = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(44, 44, 44)); // 深灰色
            var normalBorder = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(68, 68, 68)); // 边框灰色
            var highlightBrush = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(255, 152, 0)); // 橙色高亮
            var highlightBorder = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(255, 183, 77)); // 亮橙色边框

            // 重置所有按钮
            BtnLyricsAlignLeft.Background = normalBrush;
            BtnLyricsAlignLeft.BorderBrush = normalBorder;
            BtnLyricsAlignCenter.Background = normalBrush;
            BtnLyricsAlignCenter.BorderBrush = normalBorder;
            BtnLyricsAlignRight.Background = normalBrush;
            BtnLyricsAlignRight.BorderBrush = normalBorder;

            // 高亮选中的按钮
            switch (alignment)
            {
                case TextAlignment.Left:
                    BtnLyricsAlignLeft.Background = highlightBrush;
                    BtnLyricsAlignLeft.BorderBrush = highlightBorder;
                    break;
                case TextAlignment.Center:
                    BtnLyricsAlignCenter.Background = highlightBrush;
                    BtnLyricsAlignCenter.BorderBrush = highlightBorder;
                    break;
                case TextAlignment.Right:
                    BtnLyricsAlignRight.Background = highlightBrush;
                    BtnLyricsAlignRight.BorderBrush = highlightBorder;
                    break;
            }
        }

        // ============================================
        // 清空和投影
        // ============================================

        /// <summary>
        /// 清空内容
        /// </summary>
        private void BtnLyricsClear_Click(object sender, RoutedEventArgs e)
        {
            var result = WpfMessageBox.Show(
                "确定要清空所有歌词内容吗？",
                "确认清空",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                ApplyLyricsEditorStyleToCurrentMode(tb => tb.Text = "");
                GetActiveLyricsEditor().Focus();

//#if DEBUG
//                Debug.WriteLine("[歌词] 清空内容");
//#endif
            }
        }


        // ============================================
        // 事件处理
        // ============================================

        /// <summary>
        /// 文本内容改变事件
        /// </summary>
        private void LyricsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender == LyricsTextBox && _lyricsPagingMode && _lyricsSplitMode != (int)ViewSplitMode.Single && !_isSyncingPagingEditor)
            {
                SyncSplitRegionFromPagingEditor();
            }

            // 内容改变时重置自动保存计时器
            if (_lyricsAutoSaveTimer != null && _lyricsAutoSaveTimer.IsEnabled)
            {
                _lyricsAutoSaveTimer.Stop();
                _lyricsAutoSaveTimer.Start();
            }

            // 如果投影已开启，自动更新投影
//#if DEBUG
//            Debug.WriteLine($"[歌词] TextChanged - _isLyricsMode: {_isLyricsMode}, _projectionManager: {_projectionManager != null}, IsProjecting: {_projectionManager?.IsProjecting}");
//#endif
            
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 文字改变，触发投影更新");
//#endif
                RenderLyricsToProjection();
            }
        }

        private void ClearCurrentLyricsRegion()
        {
            GetActiveLyricsEditor().Text = string.Empty;
            SaveLyricsProject();

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        private void ClearAllLyricsRegions()
        {
            var result = WpfMessageBox.Show(
                "确定要清空全部分区歌词内容吗？",
                "确认清空",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            foreach (var editor in GetCurrentLyricsEditors())
            {
                editor.Text = string.Empty;
            }

            SaveLyricsProject();

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        private void LyricsEditor_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is WpfTextBox textBox)
            {
                _activeLyricsEditor = textBox;
                LyricsFontSizeDisplay.Text = textBox.FontSize.ToString("0");
                UpdateAlignmentButtonsState(textBox.TextAlignment);
            }
        }

        private void BtnLyricsPageUp_Click(object sender, RoutedEventArgs e)
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                return;
            }
            if (!_lyricsPagingMode)
            {
                ShowStatus("⚠️ 请先点击“分页”");
                LogPagingDebug("[PageUp] blocked: paging=false");
                return;
            }
            LogPagingDebug($"[PageUp] currentRegion={_lyricsCurrentPageIndex + 1}, totalRegion={GetCurrentSplitRegionCount()}");
            GoToSplitPage(_lyricsCurrentPageIndex - 1);
        }

        private void BtnLyricsPagingToggle_Click(object sender, RoutedEventArgs e)
        {
            LogPagingDebug($"[BtnPagingClick] before={_lyricsPagingMode}");
            SetLyricsPagingMode(!_lyricsPagingMode);
        }

        private void BtnLyricsPageDown_Click(object sender, RoutedEventArgs e)
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                LogPagingDebug("[PageDown] blocked: split single");
                return;
            }
            if (!_lyricsPagingMode)
            {
                ShowStatus("⚠️ 请先点击“分页”");
                LogPagingDebug("[PageDown] blocked: paging=false");
                return;
            }

            LogPagingDebug($"[PageDown] currentRegion={_lyricsCurrentPageIndex + 1}, totalRegion={GetCurrentSplitRegionCount()}");
            GoToSplitPage(_lyricsCurrentPageIndex + 1);
        }


        /// <summary>
        /// 键盘事件处理
        /// </summary>
        private void LyricsTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Ctrl+S 保存
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveLyricsProject();
                ShowToast("歌词已保存");
                e.Handled = true;
                return;
            }

            if (_lyricsSplitMode != (int)ViewSplitMode.Single && _lyricsPagingMode && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.PageDown)
                {
                    GoToSplitPage(_lyricsCurrentPageIndex + 1);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.PageUp)
                {
                    GoToSplitPage(_lyricsCurrentPageIndex - 1);
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>
        /// 鼠标滚轮事件（用于滚动）
        /// </summary>
        private void LyricsTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                AdjustLyricsFontSizeByWheel(e, fastMode: true);
            }
        }

        /// <summary>
        /// 歌词滚动事件 - 同步到投影
        /// </summary>
        private void LyricsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 如果投影已开启且在歌词模式，同步滚动位置
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine($"[歌词] 滚动位置改变: {e.VerticalOffset:F2}");
//#endif
                // 🔧 同步投影滚动位置（传入歌词ScrollViewer）
                _projectionManager.SyncLyricsScroll(LyricsScrollViewer);
            }
        }

        /// <summary>
        /// 歌词区域右键菜单
        /// </summary>
        private void LyricsScrollViewer_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 创建右键菜单
            var contextMenu = new ContextMenu();
            
            // 应用自定义样式
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");

            var splitMenuItem = new MenuItem
            {
                Header = "分割",
                Height = 36
            };

            AddSplitMenuItem(splitMenuItem, "单画面", ViewSplitMode.Single);
            AddSplitMenuItem(splitMenuItem, "左右分割", ViewSplitMode.Horizontal);
            AddSplitMenuItem(splitMenuItem, "上下分割", ViewSplitMode.Vertical);
            AddSplitMenuItem(splitMenuItem, "三分割", ViewSplitMode.TripleSplit);
            AddSplitMenuItem(splitMenuItem, "四分割", ViewSplitMode.Quad);
            splitMenuItem.Items.Add(new Separator());

            var borderToggleItem = new MenuItem
            {
                Header = "显示分割线",
                IsCheckable = true,
                IsChecked = _lyricsSplitBorderVisible,
                Height = 36
            };
            borderToggleItem.Click += (s, args) =>
            {
                _lyricsSplitBorderVisible = borderToggleItem.IsChecked;
                RefreshSplitBorders();
                ShowStatus(_lyricsSplitBorderVisible ? "✨ 已显示分割线" : "✨ 已隐藏分割线");
            };
            splitMenuItem.Items.Add(borderToggleItem);

            contextMenu.Items.Add(splitMenuItem);
            
            // 颜色菜单（第一位）
            var colorMenuItem = new MenuItem 
            { 
                Header = "颜色",
                Height = 36
            };

            // 获取当前颜色
            var currentColor = (GetActiveLyricsEditor().Foreground as System.Windows.Media.SolidColorBrush)?.Color 
                ?? System.Windows.Media.Colors.White;

            // 预设颜色
            var builtInPresets = new List<Core.ColorPreset>
            {
                new Core.ColorPreset { Name = "纯黄", R = 255, G = 255, B = 0 },
                new Core.ColorPreset { Name = "秋麒麟", R = 218, G = 165, B = 32 },
                new Core.ColorPreset { Name = "纯白", R = 255, G = 255, B = 255 }
            };

            foreach (var preset in builtInPresets)
            {
                var colorItem = new MenuItem
                {
                    Header = preset.Name,
                    IsCheckable = true,
                    IsChecked = currentColor.R == preset.R && 
                               currentColor.G == preset.G && 
                               currentColor.B == preset.B,
                    Height = 36
                };

                var currentPreset = preset;
                colorItem.Click += (s, args) =>
                {
                    SetLyricsColor(currentPreset.R, currentPreset.G, currentPreset.B);
                    if (_lyricsSplitMode == (int)ViewSplitMode.Single)
                    {
                        ShowStatus($"✨ 歌词颜色: {currentPreset.Name}");
                    }
                    else
                    {
                        ShowStatus($"✨ 当前分区颜色: {currentPreset.Name}");
                    }
                };

                colorMenuItem.Items.Add(colorItem);
            }

            // 添加分隔线
            colorMenuItem.Items.Add(new Separator());

            // 自定义颜色
            var customColorItem = new MenuItem 
            { 
                Header = "自定义颜色...",
                Height = 36
            };
            customColorItem.Click += (s, args) => OpenLyricsCustomColorPicker();
            colorMenuItem.Items.Add(customColorItem);

            contextMenu.Items.Add(colorMenuItem);

            contextMenu.Items.Add(new Separator());

            var clearCurrentRegionItem = new MenuItem
            {
                Header = _lyricsSplitMode == (int)ViewSplitMode.Single ? "清空歌词" : "清空当前分区",
                Height = 36
            };
            clearCurrentRegionItem.Click += (s, args) => ClearCurrentLyricsRegion();
            contextMenu.Items.Add(clearCurrentRegionItem);

            var clearAllRegionsItem = new MenuItem
            {
                Header = _lyricsSplitMode == (int)ViewSplitMode.Single ? "清空歌词(确认)" : "清空全部分区",
                Height = 36
            };
            clearAllRegionsItem.Click += (s, args) => ClearAllLyricsRegions();
            contextMenu.Items.Add(clearAllRegionsItem);
            
            // 退出歌词模式选项
            var exitLyricsItem = new MenuItem 
            { 
                Header = "退出歌词",
                Height = 36
            };
            exitLyricsItem.Click += (s, args) => ExitLyricsMode();
            contextMenu.Items.Add(exitLyricsItem);
            
            // 显示菜单
            contextMenu.PlacementTarget = LyricsScrollViewer;
            contextMenu.IsOpen = true;
            
            e.Handled = true;
        }

        private void AddSplitMenuItem(MenuItem parent, string title, ViewSplitMode mode)
        {
            var item = new MenuItem
            {
                Header = title,
                IsCheckable = true,
                IsChecked = _lyricsSplitMode == (int)mode,
                Height = 36
            };

            item.Click += (s, e) =>
            {
                SetLyricsSplitMode(mode, keepTextBridge: false);
                SaveLyricsProject();
                ShowStatus($"✨ 歌词分割: {title}");

                if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
                {
                    RenderLyricsToProjection();
                }
            };

            parent.Items.Add(item);
        }

        private void GoToSplitPage(int index)
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single || !_lyricsPagingMode)
            {
                LogPagingDebug($"[GoPage] blocked split={_lyricsSplitMode}, paging={_lyricsPagingMode}");
                return;
            }

            int total = GetCurrentSplitRegionCount();
            LogPagingDebug($"[GoPage-Begin] target={index + 1}, current={_lyricsCurrentPageIndex + 1}, totalRegion={total}");
            SyncSplitRegionFromPagingEditor();
            _lyricsCurrentPageIndex = Math.Clamp(index, 0, total - 1);
            SyncPagingEditorFromSplitRegion();
            UpdatePagingNavVisibility();
            ShowStatus($"📄 当前第 {_lyricsCurrentPageIndex + 1}/{total} 页");

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
            LogPagingDebug($"[GoPage-End] current={_lyricsCurrentPageIndex + 1}, totalRegion={total}");
        }

        [Conditional("DEBUG")]
        private void LogPagingDebug(string message)
        {
            _ = message;
        }


        // ============================================
        // 数据管理
        // ============================================

        /// <summary>
        /// 加载或创建歌词项目
        /// </summary>
        private void LoadOrCreateLyricsProject()
        {
            try
            {
                EnsureLyricsProjectManager();

                // 获取当前图片ID（从主窗口）
                int currentImageId = _currentImageId;
                
//#if DEBUG
//                Debug.WriteLine($"[歌词-加载] 当前图片ID: {currentImageId}");
//#endif
                
                if (currentImageId == 0)
                {
//#if DEBUG
//                    Debug.WriteLine("[歌词] 当前无图片，无法加载歌词");
//#endif
                    // 创建临时项目（不关联图片）
                    CreateTempLyricsProject();
                    return;
                }

                // 🔧 强制刷新数据库上下文（确保查询到最新数据）
                _lyricsProjectManager.ClearTracking();
                
//#if DEBUG
//                Debug.WriteLine($"[歌词-加载] 开始查询，条件：ImageId == {currentImageId}");
//                // 显示数据库中所有歌词项目
//                var allProjects = new List<LyricsProject>();
//                Debug.WriteLine($"[歌词-加载] 数据库中共有 {allProjects.Count} 个歌词项目：");
//                foreach (var proj in allProjects)
//                {
//                    Debug.WriteLine($"  - ID: {proj.Id}, 名称: {proj.Name}, 关联图片ID: {proj.ImageId}, 内容长度: {(proj.Content ?? "").Length}");
//                }
//#endif
                
                // 尝试加载当前图片对应的歌词项目
                _currentLyricsProject = _lyricsProjectManager.FindByImageId(currentImageId);
                    
//#if DEBUG
//                Debug.WriteLine($"[歌词-加载] 查询结果: {(_currentLyricsProject != null ? $"找到 - {_currentLyricsProject.Name}" : "未找到，将创建新项目")}");
//#endif

                if (_currentLyricsProject != null)
                {
                    // 加载现有项目
//#if DEBUG
//                    Debug.WriteLine($"[歌词-加载] 项目ID: {_currentLyricsProject.Id}, 名称: {_currentLyricsProject.Name}");
//                    Debug.WriteLine($"[歌词-加载] 关联图片ID: {_currentLyricsProject.ImageId}");
//                    Debug.WriteLine($"[歌词-加载] 内容长度: {(_currentLyricsProject.Content ?? "").Length}");
//                    Debug.WriteLine($"[歌词-加载] 内容完整: {_currentLyricsProject.Content ?? "(空)"}");
//#endif

                    // 🔧 自动升级旧项目：对齐方式
                    if (_currentLyricsProject.TextAlign == "Left")
                    {
                        _currentLyricsProject.TextAlign = "Center";
                        _lyricsProjectManager.Save(_currentLyricsProject);
//#if DEBUG
//                        Debug.WriteLine($"[歌词-升级] 对齐从左对齐更新为居中");
//#endif
                    }

                    string content = _currentLyricsProject.Content ?? "";
                    _lyricsSplitPages.Clear();
                    _lyricsCurrentPageIndex = 0;

                    if (TryParseModeLyricsContent(content, out var modeData))
                    {
                        LyricsTextBox.Text = modeData.SingleContent ?? "";
                        _lyricsSplitPages.Add(modeData.SplitContent ?? CreateDefaultSplitPage(ViewSplitMode.Horizontal));
                        _lyricsCurrentPageIndex = 0;
                        SetLyricsSplitMode((ViewSplitMode)modeData.ActiveMode, keepTextBridge: false);
                    }
                    else if (TryParsePagesLyricsContent(content, out var pagesData))
                    {
                        _lyricsSplitPages.Add(pagesData.Pages[0]);
                        _lyricsCurrentPageIndex = 0;
                        var loadedMode = (ViewSplitMode)_lyricsSplitPages[0].SplitMode;
                        SetLyricsSplitMode(loadedMode, keepTextBridge: false);
                        LogPagingDebug("[Load] legacy pages content detected, collapsed to single split data");
                    }
                    else if (TryParseSplitLyricsContent(content, out var splitData))
                    {
                        _lyricsSplitPages.Add(splitData);
                        _lyricsCurrentPageIndex = 0;
                        SetLyricsSplitMode((ViewSplitMode)splitData.SplitMode, keepTextBridge: false);
                    }
                    else
                    {
                        SetLyricsSplitMode(ViewSplitMode.Single, keepTextBridge: false);
                        LyricsTextBox.Text = content;
                        LyricsSplitTextBox1.Text = "";
                        LyricsSplitTextBox2.Text = "";
                        LyricsSplitTextBox3.Text = "";
                        LyricsSplitTextBox4.Text = "";
                    }

                    if (_lyricsSplitMode == (int)ViewSplitMode.Single)
                    {
                        ApplyLyricsEditorStyleToCurrentMode(tb => tb.FontSize = _currentLyricsProject.FontSize);
                        LyricsFontSizeDisplay.Text = _currentLyricsProject.FontSize.ToString("0");
                    }
                    else
                    {
                        LyricsFontSizeDisplay.Text = GetActiveLyricsEditor().FontSize.ToString("0");
                    }

                    // 始终使用全局默认颜色（不从数据库读取）
                    var textColor = new System.Windows.Media.SolidColorBrush(HexToColor(_configManager.DefaultLyricsColor));
                    if (_lyricsSplitMode == (int)ViewSplitMode.Single)
                    {
                        ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = textColor);
                    }
//#if DEBUG
//                    Debug.WriteLine($"[歌词-颜色] 使用全局默认颜色: {_configManager.DefaultLyricsColor}");
//#endif

                    // 恢复对齐方式
                    if (_lyricsSplitMode == (int)ViewSplitMode.Single)
                    {
                        var alignment = ParseTextAlignmentOrDefault(_currentLyricsProject.TextAlign, TextAlignment.Center);
                        ApplyLyricsEditorStyleToCurrentMode(tb => tb.TextAlignment = alignment);
                        UpdateAlignmentButtonsState(alignment);
                    }
                    else
                    {
                        UpdateAlignmentButtonsState(GetActiveLyricsEditor().TextAlignment);
                    }

//#if DEBUG
//                    Debug.WriteLine($"[歌词] 加载项目完成: {_currentLyricsProject.Name}");
//                    Debug.WriteLine($"[歌词] TextBox当前文本长度: {LyricsTextBox.Text.Length}");
//#endif
                }
                else
                {
                    // 获取当前图片文件名（用于项目命名）
                    var currentImagePath = _imageProcessor?.CurrentImagePath ?? "";
                    var imageName = string.IsNullOrEmpty(currentImagePath) 
                        ? "未命名" 
                        : System.IO.Path.GetFileNameWithoutExtension(currentImagePath);

                    // 创建新项目（关联到当前图片）
                    _currentLyricsProject = new LyricsProject
                    {
                        Name = $"歌词_{imageName}",
                        ImageId = currentImageId,
                        CreatedTime = DateTime.Now,
                        FontSize = DefaultLyricsFontSize,
                        TextAlign = "Center"
                    };

                    _lyricsProjectManager.Add(_currentLyricsProject);
                    
                    // 🔧 清空歌词内容（新项目没有歌词）
                    _lyricsSplitPages.Clear();
                    _lyricsCurrentPageIndex = 0;
                    SetLyricsSplitMode(ViewSplitMode.Single, keepTextBridge: false);
                    LyricsTextBox.Text = "";
                    LyricsSplitTextBox1.Text = "";
                    LyricsSplitTextBox2.Text = "";
                    LyricsSplitTextBox3.Text = "";
                    LyricsSplitTextBox4.Text = "";
                    ApplyLyricsEditorStyleToCurrentMode(tb => tb.FontSize = DefaultLyricsFontSize);
                    LyricsFontSizeDisplay.Text = DefaultLyricsFontSize.ToString("0");
                    ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = new System.Windows.Media.SolidColorBrush(HexToColor(_configManager.DefaultLyricsColor)));
                    ApplyLyricsEditorStyleToCurrentMode(tb => tb.TextAlignment = TextAlignment.Center);

                    // 初始化对齐按钮状态
                    UpdateAlignmentButtonsState(TextAlignment.Center);

//#if DEBUG
//                    Debug.WriteLine($"[歌词] 创建新项目: {_currentLyricsProject.Name}, 关联图片ID: {currentImageId}");
//                    Debug.WriteLine($"[歌词] TextBox已清空");
//#endif
                }
            }
            catch (Exception)
            {
//#if DEBUG
//                Debug.WriteLine($"[歌词] 加载项目出错: {ex.Message}");
//#endif
                CreateTempLyricsProject();
            }
        }

        /// <summary>
        /// 创建临时歌词项目（不关联图片）
        /// </summary>
        private void CreateTempLyricsProject()
        {
            _currentLyricsProject = new LyricsProject
            {
                Name = $"歌词_临时_{DateTime.Now:yyyyMMdd_HHmmss}",
                ImageId = null,
                CreatedTime = DateTime.Now,
                FontSize = DefaultLyricsFontSize,
                TextAlign = "Center"
            };

            _lyricsSplitPages.Clear();
            _lyricsCurrentPageIndex = 0;
            SetLyricsSplitMode(ViewSplitMode.Single, keepTextBridge: false);
            LyricsTextBox.Text = "";
            LyricsSplitTextBox1.Text = "";
            LyricsSplitTextBox2.Text = "";
            LyricsSplitTextBox3.Text = "";
            LyricsSplitTextBox4.Text = "";
            ApplyLyricsEditorStyleToCurrentMode(tb => tb.FontSize = DefaultLyricsFontSize);
            ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = new SolidColorBrush(HexToColor(_configManager.DefaultLyricsColor)));
            ApplyLyricsEditorStyleToCurrentMode(tb => tb.TextAlignment = TextAlignment.Center);
            LyricsFontSizeDisplay.Text = DefaultLyricsFontSize.ToString("0");
            
            // 初始化对齐按钮状态
            UpdateAlignmentButtonsState(TextAlignment.Center);
        }

        /// <summary>
        /// 保存歌词项目
        /// </summary>
        internal void SaveLyricsProject()
        {
            if (_currentLyricsProject == null)
                return;

            try
            {
                var activeEditor = GetActiveLyricsEditor();

                // 更新内容（单画面与分割画面分别保存）
                if (_lyricsSplitMode != (int)ViewSplitMode.Single)
                {
                    if (_lyricsPagingMode)
                    {
                        SyncSplitRegionFromPagingEditor();
                    }
                    SaveCurrentSplitPageFromUi();
                }

                NormalizeSplitPages();
                var splitSnapshot = _lyricsSplitPages.Count > 0
                    ? _lyricsSplitPages[0]
                    : CreateDefaultSplitPage(ViewSplitMode.Horizontal);

                var modeData = new LyricsModeContentData
                {
                    SingleContent = LyricsTextBox.Text ?? "",
                    SplitContent = splitSnapshot,
                    ActiveMode = _lyricsSplitMode
                };
                _currentLyricsProject.Content = LyricsModeContentPrefix + JsonSerializer.Serialize(modeData);

                _currentLyricsProject.FontSize = activeEditor.FontSize;
                _currentLyricsProject.TextAlign = activeEditor.TextAlignment.ToString();
                _currentLyricsProject.ModifiedTime = DateTime.Now;

                // 保存到数据库
                _lyricsProjectManager.Save(_currentLyricsProject);

//#if DEBUG
//                Debug.WriteLine($"[歌词] 保存成功: {_currentLyricsProject.Name}");
//#endif
            }
            catch (Exception ex)
            {
//#if DEBUG
//                Debug.WriteLine($"[歌词] 保存出错: {ex.Message}");
//#endif

                WpfMessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnsureLyricsProjectManager()
        {
            _lyricsProjectManager ??= new LyricsProjectManager(_dbContext);
        }

        /// <summary>
        /// 颜色转十六进制字符串
        /// </summary>
        private string ColorToHex(System.Windows.Media.Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        /// <summary>
        /// 十六进制字符串转颜色
        /// </summary>
        private System.Windows.Media.Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return System.Windows.Media.Colors.White;

            hex = hex.Replace("#", "");
            if (hex.Length == 6)
            {
                return System.Windows.Media.Color.FromRgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }

            return System.Windows.Media.Colors.White;
        }

        // ============================================
        // 自动保存
        // ============================================

        /// <summary>
        /// 启动自动保存计时器
        /// </summary>
        private void StartAutoSaveTimer()
        {
            if (_lyricsAutoSaveTimer == null)
            {
                _lyricsAutoSaveTimer = new System.Windows.Threading.DispatcherTimer();
                _lyricsAutoSaveTimer.Interval = TimeSpan.FromSeconds(30); // 每30秒保存一次
                _lyricsAutoSaveTimer.Tick += (s, e) =>
                {
                    SaveLyricsProject();
//#if DEBUG
//                    Debug.WriteLine("[歌词] 自动保存");
//#endif
                };
            }

            _lyricsAutoSaveTimer.Start();

//#if DEBUG
//            Debug.WriteLine("[歌词] 自动保存计时器已启动");
//#endif
        }

        /// <summary>
        /// 停止自动保存计时器
        /// </summary>
        private void StopAutoSaveTimer()
        {
            if (_lyricsAutoSaveTimer != null && _lyricsAutoSaveTimer.IsEnabled)
            {
                _lyricsAutoSaveTimer.Stop();

//#if DEBUG
//                Debug.WriteLine("[歌词] 自动保存计时器已停止");
//#endif
            }
        }

        // ============================================
        // 公共方法（供主窗口调用）
        // ============================================

        // 浮动歌词按钮已删除
    }
}

