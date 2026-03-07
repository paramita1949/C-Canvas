using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using ImageColorChanger.UI.Modules;
using Microsoft.EntityFrameworkCore;
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
        private int _currentLyricsProjectId = 0; // 当前歌词项目ID（独立歌曲入口）
        private System.Windows.Threading.DispatcherTimer _lyricsAutoSaveTimer; // 自动保存计时器
        private System.Windows.Threading.DispatcherTimer _lyricsTypingSaveTimer; // 输入防抖自动保存（兼容旧逻辑，当前未启用）
        private int _lyricsSplitMode = (int)ViewSplitMode.Single; // 歌词分割模式
        private WpfTextBox _activeLyricsEditor = null; // 当前激活的歌词输入框
        private bool _lyricsSplitBorderVisible = true; // 是否显示分割线
        private bool _lyricsPagingMode = false; // 分页模式开关（仅分割模式下有效）
        private bool _lyricsSliceModeEnabled = true; // 单画面切片模式（单画面默认开启）
        private int _lyricsSliceLinesPerPage = 0; // 0=默认单画面；1-4=按行切片
        private bool _lyricsSliceRuleFromCustom = false; // 当前切片规则是否来自自定义滚动
        private readonly HashSet<int> _lyricsSliceCutPoints = new(); // 自由切片切点（内容行号，1-based）
        private readonly ObservableCollection<LyricsSliceViewItem> _lyricsSliceItems = new();
        private int _lyricsCurrentSliceIndex = 0; // 当前切片索引
        private bool _isUpdatingLyricsSliceList = false;
        private bool _isUpdatingSliceFromCaret = false; // 防止主屏光标联动切片时递归
        private double _lyricsProjectionFontSize = DefaultLyricsFontSize;
        private LyricsProjectManager _lyricsProjectManager;
        private const string LyricsModeContentPrefix = "__LYRICS_MODE_V1__";
        private const string LyricsSplitContentPrefix = "__LYRICS_SPLIT_V1__";
        private const string LyricsPagesContentPrefix = "__LYRICS_PAGES_V2__";
        private const double DefaultLyricsFontSize = 60;
        private const double DefaultMainLyricsFontSize = 40;
        private const double MinMainLyricsFontSize = 20;
        private const double MaxMainLyricsFontSize = 120;
        private const double MinLyricsFontSize = 20;
        private const double MaxLyricsFontSize = 250;
        private const double DefaultLyricsTextWatermarkFontSize = 60;
        private const double MinLyricsTextWatermarkFontSize = 20;
        private const double MaxLyricsTextWatermarkFontSize = 250;
        private const double LyricsFontWheelStep = 4;
        private const double LyricsFontWheelFastStep = 8;
        private const int LyricsTypingSaveDelayMs = 800;
        private const string LyricsWatermarkRelativeDirectory = "data\\watermarks";
        private readonly List<LyricsSplitContentData> _lyricsSplitPages = new();
        private int _lyricsCurrentPageIndex = 0;
        private bool _isLoadingLyricsProject = false; // 防止加载过程触发即时保存
        private bool _isNormalizingLyricsWhitespaceLine = false; // 防止清理空白行时触发递归 TextChanged
        private bool _isApplyingSliceVisualSpacing = false; // 防止切片视觉空行应用时递归触发
        private bool _lyricsWhitespaceNormalizeQueued = false; // 防止空白行规范化重复排队
        private bool _lyricsSliceRegenerateQueued = false; // 防止切片重算重复排队
        private bool _lyricsProjectionRenderQueued = false; // 防止文本输入时重复触发投影渲染
        private string _lyricsSingleRawText = string.Empty; // 单画面原文快照
        private bool _lyricsSingleRawTextInitialized = false;
        private string _lyricsClipboardFallbackText = string.Empty; // 系统剪贴板不可用时的兜底缓冲
        private string _lyricsPendingClipboardText = string.Empty;
        private bool _lyricsClipboardUpdateQueued;
        private readonly double[] _lyricsSplitProjectionFontSizes = new[] { DefaultLyricsFontSize, DefaultLyricsFontSize, DefaultLyricsFontSize, DefaultLyricsFontSize };
        private double _lyricsMainScreenFontSize = DefaultMainLyricsFontSize;
        private double _lyricsTextWatermarkFontSize = DefaultLyricsTextWatermarkFontSize;
        private string _lyricsThemeName = "黑色";
        private string _lyricsThemeBackgroundHex = "#000000";

        // ============================================
        // 公共属性
        // ============================================
        
        /// <summary>
        /// 是否处于歌词模式（供ProjectionManager访问）
        /// </summary>
        public bool IsInLyricsMode => _isLyricsMode;

        internal void SetLyricsEntryBySong(int lyricsProjectId)
        {
            _currentLyricsProjectId = lyricsProjectId;
        }

        public sealed class LyricsSplitContentData
        {
            public int SplitMode { get; set; }
            public string[] Regions { get; set; } = new string[4];
            public LyricsSplitRegionStyle[] RegionStyles { get; set; } = new LyricsSplitRegionStyle[4];
        }

        public sealed class LyricsPagesContentData
        {
            public int CurrentPageIndex { get; set; } = 0;
            public List<LyricsSplitContentData> Pages { get; set; } = new();
        }

        public sealed class LyricsModeContentData
        {
            public string SingleContent { get; set; } = "";
            public string SingleColorHex { get; set; } = "";
            public LyricsSplitContentData SplitContent { get; set; } = new LyricsSplitContentData();
            public List<LyricsSplitContentData> SplitContents { get; set; } = new();
            public int ActiveMode { get; set; } = (int)ViewSplitMode.Single;
            public bool SliceModeEnabled { get; set; } = false;
            public int SliceLinesPerPage { get; set; } = 0;
            public bool SliceRuleFromCustom { get; set; } = false;
            public bool SliceUseFreeCutPoints { get; set; } = false;
            public List<int> SliceCutPoints { get; set; } = new();
            public int SliceCurrentIndex { get; set; } = 0;
            public double MainScreenFontSize { get; set; } = DefaultMainLyricsFontSize;
            public int ThemeMode { get; set; } = 0; // 0=黑底白字, 1=白底黑字
            public string ThemeName { get; set; } = "黑色";
            public string ThemeBackgroundHex { get; set; } = "#000000";
        }

        public sealed class LyricsSplitRegionStyle
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
            LyricsTextBox.Text = source.Text ?? "";
            LyricsTextBox.FontSize = _lyricsMainScreenFontSize;
            LyricsTextBox.TextAlignment = source.TextAlignment;
            LyricsTextBox.Foreground = source.Foreground;
            LyricsTextBox.Visibility = Visibility.Visible;
            LyricsSplitGrid.Visibility = Visibility.Collapsed;
            LyricsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _activeLyricsEditor = LyricsTextBox;
            UpdateLyricsProjectionFontSizeDisplay();
            UpdateAlignmentButtonsState(LyricsTextBox.TextAlignment);
            LyricsTextBox.Focus();
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
            UpdateLyricsProjectionFontSizeDisplay();
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

        private void EnforceMainLyricsEditorFontSize()
        {
            foreach (var textBox in new[] { LyricsTextBox, LyricsSplitTextBox1, LyricsSplitTextBox2, LyricsSplitTextBox3, LyricsSplitTextBox4 })
            {
                if (textBox != null)
                {
                    textBox.FontSize = _lyricsMainScreenFontSize;
                }
            }
        }

        private void ApplyLyricsTheme(string themeName, string backgroundHex, bool showStatus = true)
        {
            string safeName = string.IsNullOrWhiteSpace(themeName) ? "黑色" : themeName.Trim();
            string safeHex = string.IsNullOrWhiteSpace(backgroundHex) ? "#000000" : backgroundHex.Trim();
            WpfColor backgroundColor;
            try
            {
                backgroundColor = HexToColor(safeHex);
            }
            catch
            {
                safeHex = "#000000";
                backgroundColor = HexToColor(safeHex);
            }

            _lyricsThemeName = safeName;
            _lyricsThemeBackgroundHex = safeHex.ToUpperInvariant();

            var backgroundBrush = new SolidColorBrush(backgroundColor);

            if (LyricsEditorPanel != null)
            {
                LyricsEditorPanel.Background = backgroundBrush;
            }

            if (LyricsScrollViewer != null)
            {
                LyricsScrollViewer.Background = backgroundBrush;
            }

            if (LyricsSplitGrid != null)
            {
                LyricsSplitGrid.Background = backgroundBrush;
            }

            // 主题只负责背景，不改歌词文字颜色（Foreground 由“颜色”功能单独控制）。
            ApplyLyricsEditorStyleToCurrentMode(tb =>
            {
                tb.Background = WpfBrushes.Transparent;
            });

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }

            if (_isLyricsMode && _currentLyricsProject != null && !_isLoadingLyricsProject)
            {
                SaveLyricsProject("ThemeChanged", suppressUserError: true);
            }

            if (showStatus)
            {
                ShowStatus($"主题: {safeName}");
            }
        }

        private static bool IsDarkColor(WpfColor color)
        {
            double luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
            return luminance < 140;
        }

        private WpfColor GetCurrentLyricsThemeBackgroundColor()
        {
            try
            {
                return HexToColor(_lyricsThemeBackgroundHex);
            }
            catch
            {
                return WpfColor.FromRgb(0, 0, 0);
            }
        }

        private List<Core.LyricsThemePreset> GetLyricsThemePresets()
        {
            if (_configManager == null)
            {
                return new List<Core.LyricsThemePreset>
                {
                    new Core.LyricsThemePreset { Name = "黑色", BackgroundHex = "#000000" },
                    new Core.LyricsThemePreset { Name = "白色", BackgroundHex = "#FFFFFF" },
                    new Core.LyricsThemePreset { Name = "深绿", BackgroundHex = "#0B3D2E" },
                    new Core.LyricsThemePreset { Name = "深蓝", BackgroundHex = "#102A43" }
                };
            }

            return _configManager.GetAllLyricsThemePresets();
        }

        private void AddLyricsThemePreset()
        {
            var colorDialog = new System.Windows.Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                Color = System.Drawing.Color.Black
            };

            if (colorDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            string hex = $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
            string defaultName = $"主题{DateTime.Now:HHmmss}";

            var inputDialog = new Window
            {
                Title = "新增主题",
                Width = 380,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var stackPanel = new StackPanel { Margin = new Thickness(15) };
            var label = new TextBlock
            {
                Text = $"请输入主题名称\n背景色: {hex}",
                Margin = new Thickness(0, 0, 0, 10)
            };
            var textBox = new System.Windows.Controls.TextBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 14,
                Text = defaultName
            };
            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var okButton = new System.Windows.Controls.Button
            {
                Content = "确定",
                Width = 70,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 70,
                Height = 30,
                IsCancel = true
            };

            bool? dialogResult = null;
            okButton.Click += (_, _) => { dialogResult = true; inputDialog.Close(); };
            cancelButton.Click += (_, _) => { dialogResult = false; inputDialog.Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(label);
            stackPanel.Children.Add(textBox);
            stackPanel.Children.Add(buttonPanel);
            inputDialog.Content = stackPanel;
            inputDialog.Loaded += (_, _) => textBox.Focus();
            inputDialog.ShowDialog();

            if (dialogResult != true)
            {
                return;
            }

            string name = textBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowStatus("主题名称不能为空");
                return;
            }

            if (_configManager == null || !_configManager.AddCustomLyricsThemePreset(name, hex))
            {
                ShowStatus("主题名称已存在或保存失败");
                return;
            }

            ApplyLyricsTheme(name, hex);
            ShowStatus($"已新增主题: {name}");
        }

        private double ResolveMainLyricsFontSize()
        {
            double configured = _configManager?.LyricsMainScreenFontSize ?? DefaultMainLyricsFontSize;
            return Math.Clamp(configured, MinMainLyricsFontSize, MaxMainLyricsFontSize);
        }

        private double ResolveMainLyricsFontSizeForProject(double persistedValue)
        {
            if (persistedValue > 0)
            {
                return Math.Clamp(persistedValue, MinMainLyricsFontSize, MaxMainLyricsFontSize);
            }

            return ResolveMainLyricsFontSize();
        }

        private double ResolveLyricsTextWatermarkFontSize()
        {
            double configured = _configManager?.LyricsTextWatermarkFontSize ?? DefaultLyricsTextWatermarkFontSize;
            return Math.Clamp(configured, MinLyricsTextWatermarkFontSize, MaxLyricsTextWatermarkFontSize);
        }

        private void SetLyricsTextWatermarkFontSize(double value, bool showStatus = true)
        {
            double next = Math.Clamp(value, MinLyricsTextWatermarkFontSize, MaxLyricsTextWatermarkFontSize);
            if (Math.Abs(next - _lyricsTextWatermarkFontSize) < 0.001)
            {
                return;
            }

            _lyricsTextWatermarkFontSize = next;
            if (_configManager != null)
            {
                _configManager.LyricsTextWatermarkFontSize = next;
            }

            if (showStatus)
            {
                ShowStatus($"文字水印大小: {next:0}");
            }

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        private void ShowLyricsTextWatermarkFontSizeDialog()
        {
            string input = PromptTextDialog(
                "文字水印大小",
                $"请输入文字水印大小（{MinLyricsTextWatermarkFontSize:0} - {MaxLyricsTextWatermarkFontSize:0}）",
                _lyricsTextWatermarkFontSize.ToString("0"));

            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            if (!double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out double size)
                && !double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            {
                ShowStatus("文字水印大小格式无效");
                return;
            }

            SetLyricsTextWatermarkFontSize(size);
        }

        private void SetMainLyricsFontSize(double value)
        {
            double next = Math.Clamp(value, MinMainLyricsFontSize, MaxMainLyricsFontSize);
            if (Math.Abs(next - _lyricsMainScreenFontSize) < 0.001)
            {
                return;
            }

            _lyricsMainScreenFontSize = next;
            if (_configManager != null)
            {
                _configManager.LyricsMainScreenFontSize = next;
            }

            EnforceMainLyricsEditorFontSize();
            ShowStatus($"主屏字号: {next:0}");

            if (_isLyricsMode && _currentLyricsProject != null && !_isLoadingLyricsProject)
            {
                SaveLyricsProject("MainScreenFontSizeChanged", suppressUserError: true);
            }
        }

        private void UpdateLyricsProjectionFontSizeDisplay()
        {
            if (LyricsFontSizeDisplay != null)
            {
                if (_lyricsSplitMode != (int)ViewSplitMode.Single)
                {
                    int index = GetActiveSplitRegionIndex();
                    double local = GetSplitProjectionFontSizeForRegion(index);
                    LyricsFontSizeDisplay.Text = local.ToString("0");
                }
                else
                {
                    LyricsFontSizeDisplay.Text = _lyricsProjectionFontSize.ToString("0");
                }
            }
        }

        private int GetActiveSplitRegionIndex()
        {
            if (_activeLyricsEditor == LyricsSplitTextBox2) return 1;
            if (_activeLyricsEditor == LyricsSplitTextBox3) return 2;
            if (_activeLyricsEditor == LyricsSplitTextBox4) return 3;
            return 0;
        }

        private bool TryGetFocusedSplitRegionIndex(out int index)
        {
            index = 0;
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                return false;
            }

            if (_lyricsPagingMode && LyricsTextBox.IsKeyboardFocusWithin)
            {
                index = ClampPagingRegionIndex(_lyricsCurrentPageIndex);
                return true;
            }

            if (LyricsSplitTextBox1.IsKeyboardFocusWithin)
            {
                index = 0;
                return true;
            }

            if (LyricsSplitTextBox2.IsKeyboardFocusWithin)
            {
                index = 1;
                return true;
            }

            if (LyricsSplitTextBox3.IsKeyboardFocusWithin)
            {
                index = 2;
                return true;
            }

            if (LyricsSplitTextBox4.IsKeyboardFocusWithin)
            {
                index = 3;
                return true;
            }

            return false;
        }

        private double GetSplitProjectionFontSizeForRegion(int index)
        {
            int i = Math.Clamp(index, 0, _lyricsSplitProjectionFontSizes.Length - 1);
            double value = _lyricsSplitProjectionFontSizes[i];
            if (value <= 0)
            {
                value = _lyricsProjectionFontSize;
            }
            return Math.Clamp(value, MinLyricsFontSize, MaxLyricsFontSize);
        }

        private void SetSplitProjectionFontSizeForRegion(int index, double value)
        {
            int i = Math.Clamp(index, 0, _lyricsSplitProjectionFontSizes.Length - 1);
            _lyricsSplitProjectionFontSizes[i] = Math.Clamp(value, MinLyricsFontSize, MaxLyricsFontSize);
        }

        private string GetCurrentLyricsSongWatermarkText()
        {
            string name = ResolveCurrentLyricsWatermarkDisplayName();
            return string.IsNullOrWhiteSpace(name) ? string.Empty : $"《{name}》";
        }

        private string ResolveCurrentLyricsWatermarkDisplayName()
        {
            string projectName = NormalizeLyricsProjectName(_currentLyricsProject?.Name);
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                return projectName;
            }

            return ResolveCurrentImageDisplayName();
        }

        private string ResolveCurrentImageDisplayName()
        {
            try
            {
                string imagePath = _imagePath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    imagePath = _imageProcessor?.CurrentImagePath?.Trim() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    return string.Empty;
                }

                return Path.GetFileNameWithoutExtension(imagePath)?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizeLyricsProjectName(string rawName)
        {
            string name = rawName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            string[] prefixes = { "歌词_", "新歌曲_" };
            foreach (string prefix in prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    name = name.Substring(prefix.Length).Trim();
                    break;
                }
            }

            // 去掉自动序号前缀（如: 1.歌名 / 12、歌名 / 3-歌名）
            int idx = 0;
            while (idx < name.Length && char.IsDigit(name[idx]))
            {
                idx++;
            }

            if (idx > 0 && idx < name.Length)
            {
                char marker = name[idx];
                if (marker == '.' || marker == '、' || marker == '-' || marker == '_' || marker == ' ')
                {
                    name = name[(idx + 1)..].Trim();
                }
            }

            return name;
        }

        private double ResolveProjectionFontSizeForProject(LyricsProject project)
        {
            double projectSize = project?.FontSize ?? 0;
            if (projectSize > 0)
            {
                return Math.Clamp(projectSize, MinLyricsFontSize, MaxLyricsFontSize);
            }

            return Math.Clamp(_configManager.LyricsProjectionFontSize, MinLyricsFontSize, MaxLyricsFontSize);
        }

        private string GetCurrentLyricsWatermarkImagePath()
        {
            string raw = _currentLyricsProject?.ProjectionWatermarkPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            try
            {
                string absolute = Path.IsPathRooted(raw)
                    ? raw
                    : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, raw));
                return File.Exists(absolute) ? absolute : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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

        private static bool IsSupportedSplitMode(ViewSplitMode mode)
        {
            return mode == ViewSplitMode.Horizontal
                || mode == ViewSplitMode.Vertical
                || mode == ViewSplitMode.TripleSplit
                || mode == ViewSplitMode.Quad;
        }

        private static IEnumerable<ViewSplitMode> EnumerateSupportedSplitModes()
        {
            yield return ViewSplitMode.Horizontal;
            yield return ViewSplitMode.Vertical;
            yield return ViewSplitMode.TripleSplit;
            yield return ViewSplitMode.Quad;
        }

        private void NormalizeSplitPages()
        {
            var normalized = new Dictionary<ViewSplitMode, LyricsSplitContentData>();
            foreach (var page in _lyricsSplitPages)
            {
                if (page == null)
                {
                    continue;
                }

                var mode = (ViewSplitMode)page.SplitMode;
                if (!IsSupportedSplitMode(mode))
                {
                    continue;
                }

                if (!normalized.ContainsKey(mode))
                {
                    normalized[mode] = page;
                }
            }

            foreach (var mode in EnumerateSupportedSplitModes())
            {
                if (!normalized.TryGetValue(mode, out var page))
                {
                    page = CreateDefaultSplitPage(mode);
                    normalized[mode] = page;
                }

                page.SplitMode = (int)mode;
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
            }

            _lyricsSplitPages.Clear();
            _lyricsSplitPages.AddRange(normalized.Values.OrderBy(p => p.SplitMode));
            _lyricsCurrentPageIndex = ClampPagingRegionIndex(_lyricsCurrentPageIndex);
        }

        private LyricsSplitContentData GetCurrentSplitPage()
        {
            NormalizeSplitPages();
            var mode = (ViewSplitMode)_lyricsSplitMode;
            if (!IsSupportedSplitMode(mode))
            {
                mode = ViewSplitMode.Horizontal;
            }

            var page = _lyricsSplitPages.FirstOrDefault(p => p != null && p.SplitMode == (int)mode);
            if (page != null)
            {
                return page;
            }

            page = CreateDefaultSplitPage(mode);
            _lyricsSplitPages.Add(page);
            NormalizeSplitPages();
            return _lyricsSplitPages.First(p => p.SplitMode == (int)mode);
        }

        private void EnsureSplitPagesInitialized(ViewSplitMode mode, string seedText = "")
        {
            NormalizeSplitPages();
            var page = GetCurrentSplitPage();
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
            page.RegionStyles = splitEditors.Select((tb, i) => new LyricsSplitRegionStyle
            {
                FontSize = GetSplitProjectionFontSizeForRegion(i),
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
                splitEditors[i].FontSize = _lyricsMainScreenFontSize;
                SetSplitProjectionFontSizeForRegion(i, style.FontSize > 0 ? style.FontSize : _lyricsProjectionFontSize);
                splitEditors[i].TextAlignment = ParseTextAlignmentOrDefault(style.TextAlign, TextAlignment.Center);
                splitEditors[i].Foreground = new SolidColorBrush(HexToColor(
                    string.IsNullOrWhiteSpace(style.ColorHex) ? _configManager.DefaultLyricsColor : style.ColorHex));
            }

            _activeLyricsEditor = splitEditors[0];
            UpdateLyricsProjectionFontSizeDisplay();
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
                ShowStatus($"分页 {(_lyricsCurrentPageIndex + 1)}/{GetCurrentSplitRegionCount()}");
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

            _lyricsPagingMode = false;
            LyricsPageNavPanel.Visibility = Visibility.Collapsed;
        }

        private void SetLyricsPagingMode(bool enabled)
        {
            _ = enabled;
            _lyricsPagingMode = false;
            UpdatePagingNavVisibility();
            ShowStatus("分页功能已下线，请使用切片功能");
        }


        private void SetLyricsSplitMode(ViewSplitMode mode, bool keepTextBridge = false)
        {
            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                SaveCurrentSplitPageFromUi();
            }

            _lyricsSplitMode = (int)mode;
            _lyricsPagingMode = false;

            if (mode == ViewSplitMode.Single)
            {
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
                EnforceMainLyricsEditorFontSize();
                UpdateLyricsProjectionFontSizeDisplay();
                UpdateAlignmentButtonsState(LyricsTextBox.TextAlignment);
                LyricsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                _lyricsSliceModeEnabled = true;
                _lyricsSliceCutPoints.Clear();
                if (_lyricsSliceLinesPerPage <= 0)
                {
                    _lyricsSingleRawText = LyricsTextBox.Text ?? string.Empty;
                    _lyricsSingleRawTextInitialized = true;
                }
                GenerateLyricsSlicesFromSingleText(preserveIndex: true, applyMainScreenSpacing: false);
                UpdateLyricsSliceUiState();
                UpdatePagingNavVisibility();
                return;
            }

            if (_lyricsSliceModeEnabled)
            {
                SetLyricsSliceModeEnabled(false);
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
            _activeLyricsEditor?.Focus();
            UpdateSplitActiveRegionHighlight();
            ShowSplitPageStatus();
            UpdatePagingNavVisibility();
            EnforceMainLyricsEditorFontSize();
            UpdateLyricsSliceUiState();
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
            UpdateSplitActiveRegionHighlight();
        }

        private void UpdateSplitActiveRegionHighlight()
        {
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                return;
            }

            var inactiveBorder = new SolidColorBrush(WpfColor.FromRgb(255, 59, 48));
            var activeBorder = new SolidColorBrush(WpfColor.FromRgb(255, 196, 0));
            var activeBackground = new SolidColorBrush(WpfColor.FromArgb(45, 255, 196, 0));
            var regions = new[] { LyricsSplitRegion1, LyricsSplitRegion2, LyricsSplitRegion3, LyricsSplitRegion4 };
            int activeIndex = GetActiveSplitRegionIndex();
            int visibleCount = GetCurrentSplitRegionCount();
            var borderThickness = _lyricsSplitBorderVisible ? new Thickness(1) : new Thickness(0);

            for (int i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                if (region == null || region.Visibility != Visibility.Visible || i >= visibleCount)
                {
                    continue;
                }

                bool isActive = i == activeIndex;
                region.BorderThickness = borderThickness;
                region.BorderBrush = isActive ? activeBorder : inactiveBorder;
                region.Background = isActive ? activeBackground : WpfBrushes.Transparent;
            }
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
            _lyricsMainScreenFontSize = ResolveMainLyricsFontSize();
            _lyricsTextWatermarkFontSize = ResolveLyricsTextWatermarkFontSize();

            // 隐藏其他显示区域
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            VideoContainer.Visibility = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Collapsed;

            // 显示歌词编辑面板
            LyricsEditorPanel.Visibility = Visibility.Visible;

            // 加载或创建歌词项目
            LoadOrCreateLyricsProject();
            UpdateLyricsSliceUiState();

            // 聚焦到文本框
            Dispatcher.InvokeAsync(() =>
            {
                GetActiveLyricsEditor().Focus();
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            // 隐藏合成播放按钮面板（歌词模式不需要）
            CompositePlaybackPanel.Visibility = Visibility.Collapsed;

            // 启动自动保存计时器（每30秒保存一次）
            StartAutoSaveTimer();

            // 如果投影已开启，先清空图片投影状态，再投影歌词
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
            SaveLyricsProject("ExitLyricsMode");

            // 隐藏歌词编辑面板
            LyricsEditorPanel.Visibility = Visibility.Collapsed;

            // 显示图片浏览区域
            ImageScrollViewer.Visibility = Visibility.Visible;

            // 先设置标志为false，再恢复合成播放按钮的显示状态
            _isLyricsMode = false;

            // 恢复合成播放按钮的显示状态
            UpdateFloatingCompositePlayButton();

            // 如果投影已开启，恢复图片投影（刷新当前图片）
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
            AdjustLyricsFontSizeByWheel(e);
        }

        private void AdjustLyricsFontSizeByWheel(MouseWheelEventArgs e)
        {
            bool fastMode = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            double step = fastMode ? LyricsFontWheelFastStep : LyricsFontWheelStep;
            double delta = e.Delta > 0 ? step : -step;
            bool applyGlobal = false;
            int targetRegionIndex = GetActiveSplitRegionIndex();

            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                if (TryGetFocusedSplitRegionIndex(out int focusedIndex))
                {
                    targetRegionIndex = focusedIndex;
                }
                else
                {
                    applyGlobal = true;
                }
            }

            AdjustLyricsFontSize(delta, applyGlobal, targetRegionIndex);
            e.Handled = true;
        }

        private void AdjustLyricsFontSize(double delta, bool applyGlobal, int targetRegionIndex = -1)
        {
            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                if (applyGlobal)
                {
                    double newGlobal = Math.Clamp(_lyricsProjectionFontSize + delta, MinLyricsFontSize, MaxLyricsFontSize);
                    if (Math.Abs(newGlobal - _lyricsProjectionFontSize) < 0.001)
                    {
                        return;
                    }

                    _lyricsProjectionFontSize = newGlobal;
                    _configManager.LyricsProjectionFontSize = newGlobal;
                    for (int i = 0; i < _lyricsSplitProjectionFontSizes.Length; i++)
                    {
                        SetSplitProjectionFontSizeForRegion(i, newGlobal);
                    }
                    ShowStatus($"分割投影全局字号: {newGlobal:0}");
                }
                else
                {
                    int index = targetRegionIndex >= 0 ? targetRegionIndex : GetActiveSplitRegionIndex();
                    double current = GetSplitProjectionFontSizeForRegion(index);
                    double next = Math.Clamp(current + delta, MinLyricsFontSize, MaxLyricsFontSize);
                    if (Math.Abs(next - current) < 0.001)
                    {
                        return;
                    }

                    SetSplitProjectionFontSizeForRegion(index, next);
                    ShowStatus($"分区{index + 1}投影字号: {next:0}");
                }

                SaveCurrentSplitPageFromUi();
                UpdateLyricsProjectionFontSizeDisplay();
                if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
                {
                    RenderLyricsToProjection();
                }
                return;
            }

            double currentSize = _lyricsProjectionFontSize;
            double newSize = Math.Clamp(currentSize + delta, MinLyricsFontSize, MaxLyricsFontSize);
            if (Math.Abs(newSize - currentSize) < 0.001)
            {
                return;
            }

            _lyricsProjectionFontSize = newSize;
            _configManager.LyricsProjectionFontSize = newSize;
            UpdateLyricsProjectionFontSizeDisplay();

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
            OpenLyricsCustomColorPicker(forDefaultOnly: false);
        }

        private void BtnLyricsWatermark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement anchor)
            {
                ShowLyricsWatermarkMenu(anchor);
            }
        }

        private void ShowLyricsWatermarkMenu(FrameworkElement placementTarget)
        {
            var menu = new ContextMenu();
            menu.Style = (Style)FindResource("NoBorderContextMenuStyle");

            var importItem = new MenuItem { Header = "导入" };
            importItem.Click += (s, e) => ImportLyricsWatermarkForCurrentSong();
            menu.Items.Add(importItem);

            var openFolderItem = new MenuItem { Header = "打开位置" };
            openFolderItem.Click += (s, e) => OpenPathInExplorer(GetLyricsWatermarkDirectoryPath());
            menu.Items.Add(openFolderItem);

            menu.PlacementTarget = placementTarget;
            menu.IsOpen = true;
        }

        private void ImportLyricsWatermarkForCurrentSong()
        {
            if (_currentLyricsProject == null)
            {
                ShowStatus("请先打开一首歌词再设置水印");
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择水印图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                string sourcePath = dialog.FileName;
                string ext = Path.GetExtension(sourcePath);
                if (string.IsNullOrWhiteSpace(ext))
                {
                    ext = ".png";
                }

                string targetDirectory = GetLyricsWatermarkDirectoryPath();
                string targetName = Path.GetFileName(sourcePath);
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    targetName = $"watermark{ext}";
                }
                string targetPath = Path.Combine(targetDirectory, targetName);
                string sourceFullPath = Path.GetFullPath(sourcePath);
                string targetFullPath = Path.GetFullPath(targetPath);
                if (!string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                }

                string relative = GetLyricsWatermarkRelativePath(targetPath);
                BindLyricsWatermarkToCurrentSong(relative);
                ShowStatus($"水印已导入并绑定: {targetName}");
            }
            catch (Exception ex)
            {
                ShowStatus($"水印导入失败: {ex.Message}");
            }
        }

        private void BindLyricsWatermarkToCurrentSong(string relativePath)
        {
            if (_currentLyricsProject == null)
            {
                ShowStatus("当前没有可绑定的歌词项目");
                return;
            }

            _currentLyricsProject.ProjectionWatermarkPath = relativePath ?? string.Empty;
            SaveLyricsProject("WatermarkChanged", suppressUserError: true);

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        private void ClearLyricsWatermarkForCurrentSong()
        {
            if (_currentLyricsProject == null)
            {
                return;
            }

            _currentLyricsProject.ProjectionWatermarkPath = string.Empty;
            SaveLyricsProject("WatermarkCleared", suppressUserError: true);

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }

            ShowStatus("已清除当前歌曲水印");
        }

        private IEnumerable<string> EnumerateLyricsWatermarkFiles()
        {
            string directory = GetLyricsWatermarkDirectoryPath();
            if (!Directory.Exists(directory))
            {
                yield break;
            }

            string[] allowed = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                string ext = Path.GetExtension(file) ?? string.Empty;
                if (allowed.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return file;
                }
            }
        }

        private string GetLyricsWatermarkDirectoryPath()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LyricsWatermarkRelativeDirectory);
            Directory.CreateDirectory(path);
            return path;
        }

        private string GetLyricsWatermarkRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            string fullPath = Path.GetFullPath(absolutePath);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(baseDir.Length).TrimStart('\\', '/');
            }

            return absolutePath;
        }

        private void AddWatermarkSelectionItemsToContextMenu(ContextMenu contextMenu)
        {
            string current = _currentLyricsProject?.ProjectionWatermarkPath ?? string.Empty;

            var selectWatermarkItem = new MenuItem
            {
                Header = "选择水印",
                Height = 36
            };

            var noneItem = new MenuItem
            {
                Header = "无水印",
                IsCheckable = true,
                IsChecked = string.IsNullOrWhiteSpace(current)
            };
            noneItem.Click += (s, e) => ClearLyricsWatermarkForCurrentSong();
            selectWatermarkItem.Items.Add(noneItem);

            var watermarks = EnumerateLyricsWatermarkFiles()
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var path in watermarks)
            {
                string relative = GetLyricsWatermarkRelativePath(path);
                string displayName = Path.GetFileNameWithoutExtension(path);
                var item = new MenuItem
                {
                    Header = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(path) : displayName,
                    IsCheckable = true,
                    IsChecked = string.Equals(current, relative, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += (s, e) => BindLyricsWatermarkToCurrentSong(relative);
                selectWatermarkItem.Items.Add(item);
            }

            contextMenu.Items.Add(selectWatermarkItem);
        }

        /// <summary>
        /// 打开自定义颜色选择器
        /// </summary>
        private void OpenLyricsCustomColorPicker(bool forDefaultOnly)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();

            // 菜单栏颜色：取当前编辑器颜色；右键默认色：取全局默认色
            var currentColor = forDefaultOnly
                ? HexToColor(_configManager.DefaultLyricsColor)
                : (GetActiveLyricsEditor().Foreground as System.Windows.Media.SolidColorBrush)?.Color
                    ?? HexToColor(_configManager.DefaultLyricsColor);
            colorDialog.Color = System.Drawing.Color.FromArgb(
                currentColor.A, currentColor.R, currentColor.G, currentColor.B);

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = colorDialog.Color;
                if (forDefaultOnly)
                {
                    SetDefaultLyricsColor(color.R, color.G, color.B);
                    ShowStatus("✨ 已更新新建歌词默认颜色");
                }
                else
                {
                    SetLyricsColor(color.R, color.G, color.B);
                    ShowStatus("✨ 已更新当前歌词渲染颜色");
                }

//#if DEBUG
//                Debug.WriteLine($"[歌词-全局] 自定义颜色: #{color.R:X2}{color.G:X2}{color.B:X2}");
//#endif
            }
        }

        /// <summary>
        /// 设置当前歌词颜色（用于菜单栏颜色功能）
        /// </summary>
        private void SetLyricsColor(byte r, byte g, byte b)
        {
            bool isSplitMode = _lyricsSplitMode != (int)ViewSplitMode.Single;

            // 更新当前UI显示
            var brush = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(r, g, b));
            if (isSplitMode)
            {
                foreach (var editor in new[] { LyricsSplitTextBox1, LyricsSplitTextBox2, LyricsSplitTextBox3, LyricsSplitTextBox4 })
                {
                    if (editor != null)
                    {
                        editor.Foreground = brush;
                    }
                }
                if (_lyricsPagingMode && LyricsTextBox != null)
                {
                    LyricsTextBox.Foreground = brush;
                    SyncSplitRegionFromPagingEditor();
                }
            }
            else
            {
                ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = brush);
            }

            // 颜色改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }

            if (_isLyricsMode && _currentLyricsProject != null && !_isLoadingLyricsProject)
            {
                SaveLyricsProject("LyricsColorChanged", suppressUserError: true);
            }
        }

        /// <summary>
        /// 设置“新建歌词默认颜色”（不影响当前歌词渲染）
        /// </summary>
        private void SetDefaultLyricsColor(byte r, byte g, byte b)
        {
            string hexColor = $"#{r:X2}{g:X2}{b:X2}";
            _configManager.DefaultLyricsColor = hexColor;
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
            // 重新设计的视觉反馈：使用深色背景+橙色高亮
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
            if (sender is WpfTextBox textBox && TryNormalizeWhitespaceOnlyLines(textBox))
            {
                // 文本被规范化后会再次触发 TextChanged，后续流程在下一次事件中执行
                return;
            }

            if (sender == LyricsTextBox
                && _lyricsSplitMode == (int)ViewSplitMode.Single
                && !_isApplyingSliceVisualSpacing
                && _lyricsSliceLinesPerPage <= 0)
            {
                _lyricsSingleRawText = LyricsTextBox?.Text ?? string.Empty;
                _lyricsSingleRawTextInitialized = true;
            }

            if (sender == LyricsTextBox && _lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                QueueLyricsSliceRegeneration();
            }

            if (_isLyricsMode && !_isLoadingLyricsProject)
            {
                // 文本输入走防抖保存，避免每个按键都同步写库造成卡顿。
                RestartLyricsTypingSaveTimer();
            }

            // 如果投影已开启，自动更新投影
//#if DEBUG
//            Debug.WriteLine($"[歌词] TextChanged - _isLyricsMode: {_isLyricsMode}, _projectionManager: {_projectionManager != null}, IsProjecting: {_projectionManager?.IsProjecting}");
//#endif
            
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                QueueLyricsProjectionRender();
            }
        }

        private void QueueLyricsProjectionRender()
        {
            if (_lyricsProjectionRenderQueued)
            {
                return;
            }

            _lyricsProjectionRenderQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _lyricsProjectionRenderQueued = false;
                if (!_isLyricsMode || _projectionManager == null || !_projectionManager.IsProjecting)
                {
                    return;
                }

                try
                {
                    RenderLyricsToProjection();
                }
                catch
                {
                    // 输入时的投影刷新失败不影响主编辑流程
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool TryNormalizeWhitespaceOnlyLines(WpfTextBox textBox)
        {
            if (textBox == null || _isNormalizingLyricsWhitespaceLine)
            {
                return false;
            }

            if (textBox == LyricsTextBox && _lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                // 切片模式下主屏允许视觉空行，不在这里自动清理
                return false;
            }

            string original = textBox.Text ?? string.Empty;
            if (original.Length == 0)
            {
                return false;
            }

            var lines = original.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool hasWhitespaceOnlyLine = lines.Any(line =>
                line.Length > 0 &&
                line.All(char.IsWhiteSpace));
            if (!hasWhitespaceOnlyLine)
            {
                return false;
            }

            // 仅清理“由空格/Tab构成”的行，保留用户按回车产生的真正空行，避免光标被拉回上一行。
            string normalized = string.Join(
                Environment.NewLine,
                lines.Where(line => !(line.Length > 0 && line.All(char.IsWhiteSpace))));
            if (normalized == original)
            {
                return false;
            }

            if (_lyricsWhitespaceNormalizeQueued)
            {
                return true;
            }

            int caret = textBox.CaretIndex;
            _lyricsWhitespaceNormalizeQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _lyricsWhitespaceNormalizeQueued = false;
                if (textBox == null)
                {
                    return;
                }

                try
                {
                    string current = textBox.Text ?? string.Empty;
                    if (!string.Equals(current, original, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _isNormalizingLyricsWhitespaceLine = true;
                    textBox.Text = normalized;
                    textBox.CaretIndex = Math.Clamp(caret, 0, normalized.Length);
                }
                catch
                {
                    // 文本容器处于不稳定期时跳过本次规范化，避免影响主流程
                }
                finally
                {
                    _isNormalizingLyricsWhitespaceLine = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);

            return true;
        }

        private void QueueLyricsSliceRegeneration()
        {
            if (_lyricsSliceRegenerateQueued)
            {
                return;
            }

            _lyricsSliceRegenerateQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _lyricsSliceRegenerateQueued = false;
                if (!_isLyricsMode || !_lyricsSliceModeEnabled || _lyricsSplitMode != (int)ViewSplitMode.Single || LyricsTextBox == null)
                {
                    return;
                }

                try
                {
                    GenerateLyricsSlicesFromSingleText(preserveIndex: true, applyMainScreenSpacing: false);
                }
                catch
                {
                    // 切片重算失败不阻断文本编辑与保存
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ClearCurrentLyricsRegion()
        {
            GetActiveLyricsEditor().Text = string.Empty;
            SaveLyricsProject("ClearCurrentRegion");

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

            SaveLyricsProject("ClearAllRegions");

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
                UpdateLyricsProjectionFontSizeDisplay();
                UpdateAlignmentButtonsState(textBox.TextAlignment);
                UpdateSplitActiveRegionHighlight();
            }
        }

        private void BtnLyricsPageUp_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            ShowStatus("分页功能已下线，请使用切片功能");
        }

        private void BtnLyricsPagingToggle_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            ShowStatus("分页功能已下线，请使用切片功能");
        }

        private void BtnLyricsPageDown_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            ShowStatus("分页功能已下线，请使用切片功能");
        }

        /// <summary>
        /// 键盘事件处理
        /// </summary>
        private void LyricsTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var key = ResolveEffectiveKey(e);

            if (Keyboard.Modifiers == ModifierKeys.Control && sender is WpfTextBox editor)
            {
                if (HandleLyricsEditorClipboardHotKeys(editor, key))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl+S 保存
            if (key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveLyricsProject("Ctrl+S");
                ShowToast("歌词已保存");
                e.Handled = true;
                return;
            }

            // Ctrl+C/X/V/A 已在当前 TextBox 入口统一处理。

            if (_lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (e.Key == Key.PageDown || e.Key == Key.Down)
                {
                    GoToLyricsSlice(NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex + 1));
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.PageUp || e.Key == Key.Up)
                {
                    GoToLyricsSlice(NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex - 1));
                    e.Handled = true;
                    return;
                }
            }

        }

        private bool HandleLyricsEditorClipboardHotKeys(WpfTextBox editor, Key key)
        {
            if (!_isLyricsMode || editor == null)
            {
                return false;
            }

            if (key != Key.C && key != Key.X && key != Key.V && key != Key.A)
            {
                return false;
            }

            try
            {
                switch (key)
                {
                    case Key.C:
                        if (editor.SelectionLength > 0 && !string.IsNullOrEmpty(editor.SelectedText))
                        {
                            int copyStart = editor.SelectionStart;
                            int copyLen = editor.SelectionLength;
                            _lyricsClipboardFallbackText = editor.SelectedText;
                            QueueLyricsClipboardUpdate(_lyricsClipboardFallbackText);
                            CollapseLyricsEditorSelection(editor, copyStart + copyLen);
                        }
                        return true;

                    case Key.X:
                        if (editor.SelectionLength <= 0 || string.IsNullOrEmpty(editor.SelectedText))
                        {
                            return true;
                        }

                        int cutStart = editor.SelectionStart;
                        string selected = editor.SelectedText;
                        _lyricsClipboardFallbackText = selected;
                        QueueLyricsClipboardUpdate(selected);
                        editor.SelectedText = string.Empty;
                        CollapseLyricsEditorSelection(editor, cutStart);
                        return true;

                    case Key.V:
                        string paste = null;
                        if (!TryGetLyricsClipboardText(out paste))
                        {
                            paste = _lyricsClipboardFallbackText;
                        }

                        if (string.IsNullOrEmpty(paste))
                        {
                            return true;
                        }

                        int pasteStart = editor.SelectionStart;
                        editor.SelectedText = paste;
                        CollapseLyricsEditorSelection(editor, pasteStart + paste.Length);
                        return true;

                    case Key.A:
                        editor.SelectAll();
                        return true;
                }
            }
            catch (Exception)
            {
                // 回退到原生命令路径
                return false;
            }

            return false;
        }

        private static bool TrySetLyricsClipboardText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void QueueLyricsClipboardUpdate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _lyricsPendingClipboardText = text;
            if (_lyricsClipboardUpdateQueued)
            {
                return;
            }

            _lyricsClipboardUpdateQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _lyricsClipboardUpdateQueued = false;
                string pending = _lyricsPendingClipboardText;
                _lyricsPendingClipboardText = string.Empty;
                if (string.IsNullOrEmpty(pending))
                {
                    return;
                }

                TrySetLyricsClipboardText(pending);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void CollapseLyricsEditorSelection(WpfTextBox editor, int desiredCaret)
        {
            if (editor == null)
            {
                return;
            }

            int caret = Math.Clamp(desiredCaret, 0, editor.Text?.Length ?? 0);
            editor.SelectionStart = caret;
            editor.SelectionLength = 0;
            editor.CaretIndex = caret;

            // 某些输入链路会在当前按键结束后重设选区，下一帧再收口一次。
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (editor == null)
                {
                    return;
                }

                int nextCaret = Math.Clamp(caret, 0, editor.Text?.Length ?? 0);
                editor.SelectionStart = nextCaret;
                editor.SelectionLength = 0;
                editor.CaretIndex = nextCaret;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private static bool TryGetLyricsClipboardText(out string text)
        {
            text = null;
            try
            {
                if (!System.Windows.Clipboard.ContainsText())
                {
                    return false;
                }

                text = System.Windows.Clipboard.GetText();
                return !string.IsNullOrEmpty(text);
            }
            catch
            {
                return false;
            }
        }

        private static Key ResolveEffectiveKey(System.Windows.Input.KeyEventArgs e)
        {
            if (e == null)
            {
                return Key.None;
            }

            if (e.Key == Key.System)
            {
                return e.SystemKey;
            }

            if (e.Key == Key.ImeProcessed)
            {
                return e.ImeProcessedKey;
            }

            return e.Key;
        }

        internal bool TryHandleLyricsNavigationHotKeys(System.Windows.Input.KeyEventArgs e)
        {
            if (e == null)
            {
                return false;
            }

            return TryHandleLyricsNavigationHotKeys(e.Key, Keyboard.Modifiers);
        }

        internal bool TryHandleLyricsNavigationHotKeys(Key key, ModifierKeys modifiers)
        {
            if (!_isLyricsMode)
            {
                return false;
            }

            bool noModifier = modifiers == ModifierKeys.None;
            if (_lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single && noModifier)
            {
                if (key == Key.PageDown || key == Key.Down)
                {
                    GoToLyricsSlice(NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex + 1));
                    return true;
                }

                if (key == Key.PageUp || key == Key.Up)
                {
                    GoToLyricsSlice(NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex - 1));
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 鼠标滚轮事件（用于滚动）
        /// </summary>
        private void LyricsTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                AdjustLyricsFontSizeByWheel(e);
                return;
            }

            if (TryHandleLyricsCtrlWheelScroll(sender, e))
            {
                return;
            }

            if (_lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single && Keyboard.Modifiers == ModifierKeys.None)
            {
                int target = e.Delta < 0
                    ? NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex + 1)
                    : NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex - 1);
                GoToLyricsSlice(target);
                e.Handled = true;
            }
        }

        private void LyricsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            TryHandleLyricsCtrlWheelScroll(sender, e);
        }

        private bool TryHandleLyricsCtrlWheelScroll(object sender, MouseWheelEventArgs e)
        {
            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                return false;
            }

            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                return false;
            }

            if (LyricsScrollViewer == null)
            {
                Debug.WriteLine("[LyricsCtrlWheel] ScrollViewer=null");
                return false;
            }

            double before = LyricsScrollViewer.VerticalOffset;
            double extent = LyricsScrollViewer.ExtentHeight;
            double viewport = LyricsScrollViewer.ViewportHeight;
            double max = Math.Max(0, extent - viewport);
            if (max > 0.1)
            {
                double delta = e.Delta > 0 ? -120 : 120;
                double target = Math.Clamp(before + delta, 0, max);
                LyricsScrollViewer.ScrollToVerticalOffset(target);
                double after = LyricsScrollViewer.VerticalOffset;
                Debug.WriteLine($"[LyricsCtrlWheel] mode=outer sender={sender?.GetType().Name}, delta={e.Delta}, modifiers={Keyboard.Modifiers}, before={before:F1}, target={target:F1}, after={after:F1}, max={max:F1}");
            }
            else
            {
                bool moved = ScrollLyricsTextBoxInternallyByWheel(e.Delta);
                Debug.WriteLine($"[LyricsCtrlWheel] mode=inner sender={sender?.GetType().Name}, delta={e.Delta}, modifiers={Keyboard.Modifiers}, outerMax={max:F1}, moved={moved}");
            }

            e.Handled = true;
            return true;
        }

        private bool ScrollLyricsTextBoxInternallyByWheel(int wheelDelta)
        {
            if (LyricsTextBox == null)
            {
                return false;
            }

            try
            {
                int before = LyricsTextBox.GetFirstVisibleLineIndex();
                int step = Math.Max(1, Math.Abs(wheelDelta) / 60); // 120 -> 2 lines
                bool down = wheelDelta < 0;
                for (int i = 0; i < step; i++)
                {
                    if (down)
                    {
                        LyricsTextBox.LineDown();
                    }
                    else
                    {
                        LyricsTextBox.LineUp();
                    }
                }

                int after = LyricsTextBox.GetFirstVisibleLineIndex();
                return after != before;
            }
            catch
            {
                return false;
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
                // 同步投影滚动位置（传入歌词ScrollViewer）
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

            var themeMenuItem = new MenuItem
            {
                Header = "主题",
                Height = 36
            };

            var themePresets = GetLyricsThemePresets();
            foreach (var preset in themePresets)
            {
                var item = new MenuItem
                {
                    Header = preset.Name,
                    Height = 36,
                    IsCheckable = true,
                    IsChecked = string.Equals((_lyricsThemeName ?? "").Trim(), (preset.Name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                };

                string presetName = preset.Name ?? "主题";
                string presetHex = string.IsNullOrWhiteSpace(preset.BackgroundHex) ? "#000000" : preset.BackgroundHex;
                item.Click += (s, args) => ApplyLyricsTheme(presetName, presetHex);
                themeMenuItem.Items.Add(item);
            }

            themeMenuItem.Items.Add(new Separator());
            var addThemeItem = new MenuItem
            {
                Header = "新增主题...",
                Height = 36
            };
            addThemeItem.Click += (s, args) => AddLyricsThemePreset();
            themeMenuItem.Items.Add(addThemeItem);

            contextMenu.Items.Add(themeMenuItem);
            
            // 颜色菜单（第一位）
            var colorMenuItem = new MenuItem 
            { 
                Header = "颜色",
                Height = 36
            };

            // 右键颜色仅维护“新建歌词默认色”
            var defaultColor = HexToColor(_configManager.DefaultLyricsColor);

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
                    IsChecked = defaultColor.R == preset.R &&
                               defaultColor.G == preset.G &&
                               defaultColor.B == preset.B,
                    Height = 36
                };

                var currentPreset = preset;
                colorItem.Click += (s, args) =>
                {
                    SetDefaultLyricsColor(currentPreset.R, currentPreset.G, currentPreset.B);
                    ShowStatus($"✨ 新建歌词默认颜色: {currentPreset.Name}");
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
            customColorItem.Click += (s, args) => OpenLyricsCustomColorPicker(forDefaultOnly: true);
            colorMenuItem.Items.Add(customColorItem);

            contextMenu.Items.Add(colorMenuItem);
            AddMainLyricsFontItemsToContextMenu(contextMenu);
            AddLyricsTextWatermarkFontSizeItemsToContextMenu(contextMenu);

            AddWatermarkSelectionItemsToContextMenu(contextMenu);
            
            // 显示菜单
            contextMenu.PlacementTarget = LyricsScrollViewer;
            contextMenu.IsOpen = true;
            
            e.Handled = true;
        }

        private void AddMainLyricsFontItemsToContextMenu(ContextMenu contextMenu)
        {
            if (contextMenu == null)
            {
                return;
            }

            var fontMenuItem = new MenuItem
            {
                Header = "主屏字号",
                Height = 36
            };

            int[] candidates = { 20, 30, 40, 50, 60 };
            foreach (int size in candidates)
            {
                var item = new MenuItem
                {
                    Header = size.ToString(),
                    Height = 36,
                    IsCheckable = true,
                    IsChecked = Math.Abs(_lyricsMainScreenFontSize - size) < 0.001
                };

                int selected = size;
                item.Click += (s, e) => SetMainLyricsFontSize(selected);
                fontMenuItem.Items.Add(item);
            }

            contextMenu.Items.Add(fontMenuItem);
        }

        private void AddLyricsTextWatermarkFontSizeItemsToContextMenu(ContextMenu contextMenu)
        {
            if (contextMenu == null)
            {
                return;
            }

            var watermarkFontMenuItem = new MenuItem
            {
                Header = "文字水印大小",
                Height = 36
            };

            int[] candidates = { 40, 50, 60, 72, 84, 96 };
            foreach (int size in candidates)
            {
                var item = new MenuItem
                {
                    Header = size.ToString(),
                    Height = 36,
                    IsCheckable = true,
                    IsChecked = Math.Abs(_lyricsTextWatermarkFontSize - size) < 0.001
                };

                int selected = size;
                item.Click += (s, e) => SetLyricsTextWatermarkFontSize(selected);
                watermarkFontMenuItem.Items.Add(item);
            }

            watermarkFontMenuItem.Items.Add(new Separator());
            var customItem = new MenuItem
            {
                Header = "自定义...",
                Height = 36
            };
            customItem.Click += (s, e) => ShowLyricsTextWatermarkFontSizeDialog();
            watermarkFontMenuItem.Items.Add(customItem);

            contextMenu.Items.Add(watermarkFontMenuItem);
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
                SaveLyricsProject("SplitModeChanged");
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
            _ = index;
            _lyricsPagingMode = false;
            UpdatePagingNavVisibility();
            ShowStatus("分页功能已下线，请使用切片功能");
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
                _isLoadingLyricsProject = true;
            try
            {
                _lyricsMainScreenFontSize = ResolveMainLyricsFontSize();
                EnsureLyricsProjectManager();
                LogLyricsSaveDebug($"[Load-Begin] targetProjectId={_currentLyricsProjectId}, db={GetLyricsDbPathSafe()}");
                string loadedSingleColorHex = _configManager.DefaultLyricsColor;

                if (_currentLyricsProjectId <= 0)
                {
                    // 未指定歌曲ID时，进入独立临时编辑态。
                    CreateTempLyricsProject();
                    return;
                }

                _lyricsProjectManager.ClearTracking();
                _currentLyricsProject = _lyricsProjectManager.FindById(_currentLyricsProjectId);
                    
//#if DEBUG
//                Debug.WriteLine($"[歌词-加载] 查询结果: {(_currentLyricsProject != null ? $"找到 - {_currentLyricsProject.Name}" : "未找到，将创建新项目")}");
//#endif

                if (_currentLyricsProject != null)
                {
                    string loadedThemeName = "黑色";
                    string loadedThemeHex = "#000000";
                    _currentLyricsProjectId = _currentLyricsProject.Id;
                    LogLyricsSaveDebug($"[Load-Hit] projectId={_currentLyricsProject.Id}, name={_currentLyricsProject.Name}, contentLen={(_currentLyricsProject.Content ?? "").Length}, db={GetLyricsDbPathSafe()}");
                    // 加载现有项目
//#if DEBUG
//                    Debug.WriteLine($"[歌词-加载] 项目ID: {_currentLyricsProject.Id}, 名称: {_currentLyricsProject.Name}");
//                    Debug.WriteLine($"[歌词-加载] 关联图片ID: {_currentLyricsProject.ImageId}");
//                    Debug.WriteLine($"[歌词-加载] 内容长度: {(_currentLyricsProject.Content ?? "").Length}");
//                    Debug.WriteLine($"[歌词-加载] 内容完整: {_currentLyricsProject.Content ?? "(空)"}");
//#endif

                    // 自动升级旧项目：对齐方式
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
                        loadedSingleColorHex = NormalizeLyricsColorHex(modeData.SingleColorHex, _configManager.DefaultLyricsColor);
                        if (modeData.SplitContents != null && modeData.SplitContents.Count > 0)
                        {
                            _lyricsSplitPages.AddRange(modeData.SplitContents.Where(p => p != null));
                        }
                        else
                        {
                            _lyricsSplitPages.Add(modeData.SplitContent ?? CreateDefaultSplitPage(ViewSplitMode.Horizontal));
                        }
                        NormalizeSplitPages();
                        _lyricsCurrentPageIndex = 0;
                        SetLyricsSplitMode((ViewSplitMode)modeData.ActiveMode, keepTextBridge: false);
                        _lyricsSliceLinesPerPage = Math.Clamp(modeData.SliceLinesPerPage, 0, 4);
                        _lyricsSliceRuleFromCustom = false;
                        _lyricsSliceCutPoints.Clear();
                        _lyricsCurrentSliceIndex = Math.Max(0, modeData.SliceCurrentIndex);
                        _lyricsSliceModeEnabled = _lyricsSplitMode == (int)ViewSplitMode.Single;
                        _lyricsSingleRawText = modeData.SingleContent ?? string.Empty;
                        _lyricsSingleRawTextInitialized = true;
                        _lyricsMainScreenFontSize = ResolveMainLyricsFontSizeForProject(modeData.MainScreenFontSize);
                        loadedThemeName = string.IsNullOrWhiteSpace(modeData.ThemeName) ? "黑色" : modeData.ThemeName;
                        loadedThemeHex = string.IsNullOrWhiteSpace(modeData.ThemeBackgroundHex)
                            ? (modeData.ThemeMode == 1 ? "#FFFFFF" : "#000000")
                            : modeData.ThemeBackgroundHex;
                    }
                    else if (TryParsePagesLyricsContent(content, out var pagesData))
                    {
                        if (pagesData.Pages != null && pagesData.Pages.Count > 0)
                        {
                            _lyricsSplitPages.AddRange(pagesData.Pages.Where(p => p != null));
                        }
                        NormalizeSplitPages();
                        _lyricsCurrentPageIndex = 0;
                        var loadedMode = (ViewSplitMode)_lyricsSplitPages.First().SplitMode;
                        SetLyricsSplitMode(loadedMode, keepTextBridge: false);
                        _lyricsSliceModeEnabled = false;
                        _lyricsSliceLinesPerPage = 0;
                        _lyricsSliceRuleFromCustom = false;
                        _lyricsSliceCutPoints.Clear();
                        _lyricsCurrentSliceIndex = 0;
                        _lyricsSingleRawText = LyricsTextBox.Text ?? string.Empty;
                        _lyricsSingleRawTextInitialized = true;
                        _lyricsMainScreenFontSize = ResolveMainLyricsFontSize();
                        loadedThemeName = "黑色";
                        loadedThemeHex = "#000000";
                        LogPagingDebug("[Load] legacy pages content detected, collapsed to single split data");
                    }
                    else if (TryParseSplitLyricsContent(content, out var splitData))
                    {
                        _lyricsSplitPages.Add(splitData);
                        NormalizeSplitPages();
                        _lyricsCurrentPageIndex = 0;
                        SetLyricsSplitMode((ViewSplitMode)splitData.SplitMode, keepTextBridge: false);
                        _lyricsSliceModeEnabled = false;
                        _lyricsSliceLinesPerPage = 0;
                        _lyricsSliceRuleFromCustom = false;
                        _lyricsSliceCutPoints.Clear();
                        _lyricsCurrentSliceIndex = 0;
                        _lyricsSingleRawText = LyricsTextBox.Text ?? string.Empty;
                        _lyricsSingleRawTextInitialized = true;
                        _lyricsMainScreenFontSize = ResolveMainLyricsFontSize();
                        loadedThemeName = "黑色";
                        loadedThemeHex = "#000000";
                    }
                    else
                    {
                        SetLyricsSplitMode(ViewSplitMode.Single, keepTextBridge: false);
                        LyricsTextBox.Text = content;
                        LyricsSplitTextBox1.Text = "";
                        LyricsSplitTextBox2.Text = "";
                        LyricsSplitTextBox3.Text = "";
                        LyricsSplitTextBox4.Text = "";
                        _lyricsSliceModeEnabled = false;
                        _lyricsSliceLinesPerPage = 0;
                        _lyricsSliceRuleFromCustom = false;
                        _lyricsSliceCutPoints.Clear();
                        _lyricsCurrentSliceIndex = 0;
                        _lyricsSingleRawText = LyricsTextBox.Text ?? string.Empty;
                        _lyricsSingleRawTextInitialized = true;
                        _lyricsMainScreenFontSize = ResolveMainLyricsFontSize();
                        loadedThemeName = "黑色";
                        loadedThemeHex = "#000000";
                    }

                    _lyricsProjectionFontSize = ResolveProjectionFontSizeForProject(_currentLyricsProject);
                    if (_lyricsSplitMode == (int)ViewSplitMode.Single)
                    {
                        ApplyLyricsEditorStyleToCurrentMode(tb => tb.FontSize = _lyricsMainScreenFontSize);
                        UpdateLyricsProjectionFontSizeDisplay();
                    }
                    else
                    {
                        UpdateLyricsProjectionFontSizeDisplay();
                    }

                    // 单画面：优先使用歌曲自身保存的颜色；未保存时回退到全局默认色。
                    if (_lyricsSplitMode == (int)ViewSplitMode.Single)
                    {
                        var textColor = new System.Windows.Media.SolidColorBrush(HexToColor(loadedSingleColorHex));
                        ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = textColor);
                    }

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

                    ApplyLyricsTheme(loadedThemeName, loadedThemeHex, showStatus: false);
                    if (_lyricsSplitMode == (int)ViewSplitMode.Single)
                    {
                        var textColor = new System.Windows.Media.SolidColorBrush(HexToColor(loadedSingleColorHex));
                        ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = textColor);
                    }

//#if DEBUG
//                    Debug.WriteLine($"[歌词] 加载项目完成: {_currentLyricsProject.Name}");
//                    Debug.WriteLine($"[歌词] TextBox当前文本长度: {LyricsTextBox.Text.Length}");
//#endif
                }
                else
                {
                    // 指定歌曲ID不存在时创建独立歌词项目（不关联图片）
                    string songName = $"新歌曲_{DateTime.Now:HHmmss}";
                    _currentLyricsProject = new LyricsProject
                    {
                        Name = $"歌词_{songName}",
                        ImageId = null,
                        SourceType = 1,
                        CreatedTime = DateTime.Now,
                        FontSize = DefaultLyricsFontSize,
                        TextAlign = "Center"
                    };

                    _lyricsProjectManager.Add(_currentLyricsProject);
                    _currentLyricsProjectId = _currentLyricsProject.Id;
                    LogLyricsSaveDebug($"[Load-Create] projectId={_currentLyricsProject.Id}, sourceType={_currentLyricsProject.SourceType}, groupId={_currentLyricsProject.GroupId}");
                    
                    // 清空歌词内容（新项目没有歌词）
                    _lyricsSplitPages.Clear();
                    _lyricsCurrentPageIndex = 0;
                    SetLyricsSplitMode(ViewSplitMode.Single, keepTextBridge: false);
                    LyricsTextBox.Text = "";
                    LyricsSplitTextBox1.Text = "";
                    LyricsSplitTextBox2.Text = "";
                    LyricsSplitTextBox3.Text = "";
                    LyricsSplitTextBox4.Text = "";
                    ApplyLyricsEditorStyleToCurrentMode(tb => tb.FontSize = _lyricsMainScreenFontSize);
                    _lyricsProjectionFontSize = Math.Clamp(_configManager.LyricsProjectionFontSize, MinLyricsFontSize, MaxLyricsFontSize);
                    UpdateLyricsProjectionFontSizeDisplay();
                    ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = new System.Windows.Media.SolidColorBrush(HexToColor(_configManager.DefaultLyricsColor)));
                    ApplyLyricsEditorStyleToCurrentMode(tb => tb.TextAlignment = TextAlignment.Center);
                    _lyricsSliceModeEnabled = false;
                    _lyricsSliceLinesPerPage = 0;
                    _lyricsSliceRuleFromCustom = false;
                    _lyricsSliceCutPoints.Clear();
                    _lyricsCurrentSliceIndex = 0;
                    _lyricsSingleRawText = string.Empty;
                    _lyricsSingleRawTextInitialized = true;
                    _lyricsThemeName = "黑色";
                    _lyricsThemeBackgroundHex = "#000000";
                    EnforceMainLyricsEditorFontSize();
                    ApplyLyricsTheme("黑色", "#000000", showStatus: false);
                    ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = new System.Windows.Media.SolidColorBrush(HexToColor(_configManager.DefaultLyricsColor)));

                    // 初始化对齐按钮状态
                    UpdateAlignmentButtonsState(TextAlignment.Center);

//#if DEBUG
//                    Debug.WriteLine($"[歌词] 创建新项目: {_currentLyricsProject.Name}, 关联图片ID: {currentImageId}");
//                    Debug.WriteLine($"[歌词] TextBox已清空");
//#endif
                }

                if (_lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single)
                {
                    try
                    {
                        GenerateLyricsSlicesFromSingleText(preserveIndex: true);
                    }
                    catch (Exception sliceEx)
                    {
                        _lyricsSliceModeEnabled = false;
                        _lyricsCurrentSliceIndex = 0;
                        LogLyricsSaveDebug($"[Load-SliceWarn] slice disabled due to: {sliceEx.Message}");
                    }
                }
                UpdateLyricsSliceUiState();
            }
            catch (Exception ex)
            {
                LogLyricsSaveDebug($"[Load-Error] {ex.Message}; stack={ex.StackTrace}");
//#if DEBUG
//                Debug.WriteLine($"[歌词] 加载项目出错: {ex.Message}");
//#endif
                CreateTempLyricsProject();
            }
            finally
            {
                _isLoadingLyricsProject = false;
                LogLyricsSaveDebug($"[Load-End] currentProjectId={_currentLyricsProject?.Id ?? 0}, splitMode={_lyricsSplitMode}, sliceMode={_lyricsSliceModeEnabled}");
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
            _lyricsSliceModeEnabled = false;
            _lyricsSliceLinesPerPage = 0;
            _lyricsSliceRuleFromCustom = false;
            _lyricsSliceCutPoints.Clear();
            _lyricsCurrentSliceIndex = 0;
            _lyricsSingleRawText = string.Empty;
            _lyricsSingleRawTextInitialized = true;
            _lyricsThemeName = "黑色";
            _lyricsThemeBackgroundHex = "#000000";
            ApplyLyricsEditorStyleToCurrentMode(tb => tb.FontSize = _lyricsMainScreenFontSize);
            ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = new SolidColorBrush(HexToColor(_configManager.DefaultLyricsColor)));
            ApplyLyricsEditorStyleToCurrentMode(tb => tb.TextAlignment = TextAlignment.Center);
            _lyricsProjectionFontSize = Math.Clamp(_configManager.LyricsProjectionFontSize, MinLyricsFontSize, MaxLyricsFontSize);
            UpdateLyricsProjectionFontSizeDisplay();
            EnforceMainLyricsEditorFontSize();
            ApplyLyricsTheme("黑色", "#000000", showStatus: false);
            ApplyLyricsEditorStyleToCurrentMode(tb => tb.Foreground = new SolidColorBrush(HexToColor(_configManager.DefaultLyricsColor)));
            
            // 初始化对齐按钮状态
            UpdateAlignmentButtonsState(TextAlignment.Center);
        }

        /// <summary>
        /// 保存歌词项目
        /// </summary>
        internal void SaveLyricsProject(string reason = "Unknown", bool suppressUserError = false)
        {
            if (_currentLyricsProject == null)
            {
                LogLyricsSaveDebug($"[Skip] reason={reason}, project=null");
                return;
            }

            if (_isLoadingLyricsProject)
            {
                LogLyricsSaveDebug($"[Skip] reason={reason}, loading=true, projectId={_currentLyricsProject.Id}");
                return;
            }

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
                var splitSnapshot = _lyricsSplitMode == (int)ViewSplitMode.Single
                    ? CreateDefaultSplitPage(ViewSplitMode.Horizontal)
                    : GetCurrentSplitPage();

                var modeData = new LyricsModeContentData
                {
                    SingleContent = GetSingleLyricsContentForPersistence(),
                    SingleColorHex = ColorToHex((LyricsTextBox.Foreground as SolidColorBrush)?.Color ?? HexToColor(_configManager.DefaultLyricsColor)),
                    SplitContent = splitSnapshot,
                    SplitContents = _lyricsSplitPages
                        .Where(p => p != null)
                        .Select(p => new LyricsSplitContentData
                        {
                            SplitMode = p.SplitMode,
                            Regions = (p.Regions ?? Array.Empty<string>()).Take(4).Concat(Enumerable.Repeat(string.Empty, 4)).Take(4).ToArray(),
                            RegionStyles = (p.RegionStyles ?? Array.Empty<LyricsSplitRegionStyle>())
                                .Take(4)
                                .Concat(Enumerable.Range(0, 4).Select(_ => new LyricsSplitRegionStyle()))
                                .Take(4)
                                .Select(s => s ?? new LyricsSplitRegionStyle())
                                .ToArray()
                        })
                        .ToList(),
                    ActiveMode = _lyricsSplitMode,
                    SliceModeEnabled = _lyricsSplitMode == (int)ViewSplitMode.Single,
                    SliceLinesPerPage = Math.Clamp(_lyricsSliceLinesPerPage, 0, 4),
                    SliceRuleFromCustom = false,
                    SliceUseFreeCutPoints = false,
                    SliceCutPoints = new List<int>(),
                    SliceCurrentIndex = Math.Max(0, _lyricsCurrentSliceIndex),
                    MainScreenFontSize = Math.Clamp(_lyricsMainScreenFontSize, MinMainLyricsFontSize, MaxMainLyricsFontSize),
                    ThemeMode = string.Equals(_lyricsThemeName, "白色", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                    ThemeName = string.IsNullOrWhiteSpace(_lyricsThemeName) ? "黑色" : _lyricsThemeName,
                    ThemeBackgroundHex = string.IsNullOrWhiteSpace(_lyricsThemeBackgroundHex) ? "#000000" : _lyricsThemeBackgroundHex
                };
                string serializedModeData = JsonSerializer.Serialize(modeData);
                string expectedContent = LyricsModeContentPrefix + serializedModeData;
                _currentLyricsProject.Content = expectedContent;

                _currentLyricsProject.FontSize = _lyricsProjectionFontSize;
                _currentLyricsProject.TextAlign = activeEditor.TextAlignment.ToString();
                _currentLyricsProject.ModifiedTime = DateTime.Now;

                // 保存到数据库
                _lyricsProjectManager.Save(_currentLyricsProject);
                var savedSnapshot = _dbContext.LyricsProjects
                    .AsNoTracking()
                    .FirstOrDefault(p => p.Id == _currentLyricsProject.Id);
                if (savedSnapshot == null || !string.Equals(savedSnapshot.Content, expectedContent, StringComparison.Ordinal))
                {
                    var persistent = _dbContext.LyricsProjects.FirstOrDefault(p => p.Id == _currentLyricsProject.Id);
                    if (persistent != null)
                    {
                        persistent.Content = expectedContent;
                        persistent.FontSize = _lyricsProjectionFontSize;
                        persistent.TextAlign = activeEditor.TextAlignment.ToString();
                        persistent.ModifiedTime = DateTime.Now;
                        _dbContext.SaveChanges();
                        savedSnapshot = _dbContext.LyricsProjects
                            .AsNoTracking()
                            .FirstOrDefault(p => p.Id == _currentLyricsProject.Id);
                        LogLyricsSaveDebug($"[Repair] reason={reason}, projectId={_currentLyricsProject.Id}, mismatchDetected=true");
                    }
                }
                int savedContentLen = (savedSnapshot?.Content ?? "").Length;
                bool savedAsModePayload = (savedSnapshot?.Content ?? "").StartsWith(LyricsModeContentPrefix, StringComparison.Ordinal);
                LogLyricsSaveDebug(
                    $"[OK] reason={reason}, projectId={_currentLyricsProject.Id}, splitMode={_lyricsSplitMode}, sliceMode={_lyricsSliceModeEnabled}, textLen={(LyricsTextBox.Text ?? "").Length}, modeJsonLen={serializedModeData.Length}, savedContentLen={savedContentLen}, savedModePayload={savedAsModePayload}, db={GetLyricsDbPathSafe()}");

//#if DEBUG
//                Debug.WriteLine($"[歌词] 保存成功: {_currentLyricsProject.Name}");
//#endif
            }
            catch (Exception ex)
            {
                LogLyricsSaveDebug($"[Fail] reason={reason}, projectId={_currentLyricsProject?.Id ?? 0}, error={ex.Message}");
//#if DEBUG
//                Debug.WriteLine($"[歌词] 保存出错: {ex.Message}");
//#endif

                if (!suppressUserError)
                {
                    WpfMessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LogLyricsSaveDebug(string message)
        {
            _ = message;
            // Debug.WriteLine($"[歌词保存] {message}");
        }

        private string GetSingleLyricsContentForPersistence()
        {
            string text = LyricsTextBox?.Text ?? string.Empty;
            if (!(_lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single))
            {
                return text;
            }

            // 固定 1/2/3/4 显示时，主编辑区是可视化文本，持久化应使用原文快照。
            if (_lyricsSliceLinesPerPage > 0 && _lyricsSingleRawTextInitialized)
            {
                return _lyricsSingleRawText ?? string.Empty;
            }

            // 默认模式（空白行切片）直接保存编辑区原文，保留空白行。
            if (_lyricsSliceLinesPerPage <= 0)
            {
                return text;
            }

            string plain = BuildPlainLyricsTextFromSliceItems();
            if (!string.IsNullOrWhiteSpace(plain))
            {
                return plain;
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(LyricsSlicePlanner.StripDisplayPrefix)
                .Where(line => !string.IsNullOrWhiteSpace(line));
            return string.Join(Environment.NewLine, lines);
        }

        private string GetLyricsDbPathSafe()
        {
            try
            {
                return _dbContext?.Database?.GetDbConnection()?.DataSource ?? "(unknown)";
            }
            catch (Exception ex)
            {
                return $"(db-path-error: {ex.Message})";
            }
        }

        /// <summary>
        /// 仅在跨过版本阈值（6.0.1.5）时执行一次清理：删除旧版图片关联歌词。
        /// </summary>
        private void PurgeLegacyImageLinkedLyricsProjects()
        {
            const string thresholdVersion = "6.0.1.5";
            try
            {
                if (_dbContext == null || _configManager == null)
                {
                    return;
                }

                string currentVersion = Services.UpdateService.GetCurrentVersion() ?? string.Empty;
                string lastRunVersion = _configManager.LastRunAppVersion ?? string.Empty;

                // 仅当“上次版本 < 阈值 && 当前版本 >= 阈值”时触发清理。
                bool crossedThreshold =
                    CompareSimpleVersion(lastRunVersion, thresholdVersion) < 0 &&
                    CompareSimpleVersion(currentVersion, thresholdVersion) >= 0;
                if (!crossedThreshold)
                {
                    if (!string.IsNullOrWhiteSpace(currentVersion))
                    {
                        _configManager.LastRunAppVersion = currentVersion;
                    }
                    return;
                }

                var legacyItems = _dbContext.LyricsProjects
                    .Where(p => p.ImageId != null || p.SourceType == 0)
                    .ToList();
                int removed = legacyItems.Count;
                if (removed > 0)
                {
                    var removedIds = legacyItems.Select(x => x.Id).ToHashSet();
                    _dbContext.LyricsProjects.RemoveRange(legacyItems);
                    _dbContext.SaveChanges();

                    if (_currentLyricsProject != null && removedIds.Contains(_currentLyricsProject.Id))
                    {
                        _currentLyricsProject = null;
                        _currentLyricsProjectId = 0;
                    }
                }

                _configManager.LastRunAppVersion = string.IsNullOrWhiteSpace(currentVersion) ? thresholdVersion : currentVersion;
                LogLyricsSaveDebug($"[Legacy-Purge] threshold={thresholdVersion}, removed={removed}, lastRun={_configManager.LastRunAppVersion}, db={GetLyricsDbPathSafe()}");
            }
            catch (Exception ex)
            {
                LogLyricsSaveDebug($"[Legacy-Purge-Error] {ex.Message}");
            }
        }

        private static int CompareSimpleVersion(string left, string right)
        {
            if (Version.TryParse(string.IsNullOrWhiteSpace(left) ? "0.0.0.0" : left, out var lv) &&
                Version.TryParse(string.IsNullOrWhiteSpace(right) ? "0.0.0.0" : right, out var rv))
            {
                return lv.CompareTo(rv);
            }

            return string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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

        private string NormalizeLyricsColorHex(string candidate, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
            try
            {
                var color = HexToColor(value);
                return ColorToHex(color);
            }
            catch
            {
                var color = HexToColor(fallback);
                return ColorToHex(color);
            }
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
                _lyricsAutoSaveTimer.Interval = TimeSpan.FromSeconds(15); // 每15秒保存一次
                _lyricsAutoSaveTimer.Tick += (s, e) =>
                {
                    SaveLyricsProject("Timer15s", suppressUserError: true);
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

            if (_lyricsTypingSaveTimer != null && _lyricsTypingSaveTimer.IsEnabled)
            {
                _lyricsTypingSaveTimer.Stop();
            }
        }

        private void RestartLyricsTypingSaveTimer()
        {
            if (!_isLyricsMode)
            {
                return;
            }

            if (_lyricsTypingSaveTimer == null)
            {
                _lyricsTypingSaveTimer = new System.Windows.Threading.DispatcherTimer();
                _lyricsTypingSaveTimer.Interval = TimeSpan.FromMilliseconds(LyricsTypingSaveDelayMs);
                _lyricsTypingSaveTimer.Tick += (s, e) =>
                {
                    _lyricsTypingSaveTimer.Stop();
                    SaveLyricsProject("TypingDebounce", suppressUserError: true);
                };
            }

            _lyricsTypingSaveTimer.Stop();
            _lyricsTypingSaveTimer.Start();
        }

        // ============================================
        // 公共方法（供主窗口调用）
        // ============================================

        // 浮动歌词按钮已删除
    }
}






