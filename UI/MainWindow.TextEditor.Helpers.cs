using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using ImageColorChanger.Services.Projection.Output;
using ImageColorChanger.Services.TextEditor.Components.Notice;
using ImageColorChanger.Services.TextEditor.Application.Models;
using ImageColorChanger.Services.TextEditor.Models;
using ImageColorChanger.UI.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfColor = System.Windows.Media.Color;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using SkiaSharp;
using System.Text.RegularExpressions;
using System.Globalization;

namespace ImageColorChanger.UI
{
    internal sealed class NoticeVisualBoundsCache
    {
        public string ContentHash { get; set; }
        public double FirstX { get; set; }
        public double LastX { get; set; }
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// MainWindow TextEditor Helper Methods
    /// </summary>
    public partial class MainWindow
    {
        #region 辅助方法


        /// <summary>
        /// 将文本框添加到画布
        /// </summary>
        private void AddTextBoxToCanvas(DraggableTextBox textBox)
        {
            _textBoxes.Add(textBox);
            EditorCanvas.Children.Add(textBox);

            // 监听选中事件
            textBox.SelectionChanged += (s, isSelected) =>
            {
                _textBoxEditSessionService?.SetSelected(textBox.Data.Id, isSelected);

                if (isSelected)
                {
                    // 取消其他文本框的选中状态
                    foreach (var tb in _textBoxes)
                    {
                        if (tb != textBox && tb.IsSelected)
                        {
                            tb.SetSelected(false);
                        }
                    }
                    _selectedTextBox = textBox;

                    // 更新工具栏状态
                    UpdateToolbarFromSelection();
                    
                    // 显示浮动工具栏
                    ShowTextBoxFloatingToolbar(textBox);
                }
                else
                {
                    // 取消选中时隐藏浮动工具栏
                    HideBibleFloatingToolbar();
                }
            };

            textBox.EditModeChanged += (s, isEditing) =>
            {
                _textBoxEditSessionService?.SetEditing(textBox.Data.Id, isEditing);
            };

            // 监听内容变化，保存按钮变绿色
            textBox.ContentChanged += (s, content) =>
            {
                MarkContentAsModified();
                //System.Diagnostics.Debug.WriteLine($"文本内容改变: {content}");
            };
            
            // 监听位置变化，显示辅助线并保存
            textBox.PositionChanged += (s, pos) =>
            {
                UpdateAlignmentGuides(textBox);
                MarkContentAsModified();
            };
            
            // 监听拖动结束，隐藏辅助线
            textBox.DragEnded += (s, e) =>
            {
                HideAlignmentGuides();
            };
            
            // 监听尺寸变化，保存按钮变绿色
            textBox.SizeChanged += (s, size) =>
            {
                MarkContentAsModified();
            };
            
            // 监听删除请求（右键菜单或DEL键）
            textBox.RequestDelete += async (s, e) =>
            {
                await DeleteTextBoxAsync(textBox);
            };

            // 监听复制请求（右键菜单 - 复制到缓冲区）
            textBox.RequestCopy += async (s, e) =>
            {
                await CopyTextBoxToClipboardAsync(textBox);
            };

            // 监听粘贴请求（右键菜单 - 从缓冲区粘贴）
            textBox.RequestPaste += async (s, e) =>
            {
                await PasteTextBoxFromClipboardAsync(textBox);
            };

            //  监听文本选择改变事件（更新工具栏按钮状态）
            textBox.TextSelectionChanged += (s, e) =>
            {
                if (_selectedTextBox == textBox)
                {
                    UpdateToolbarButtonStatesFromSelection();
                }
            };

            EnsureNoticeAnimationLoopState();
        }

        private List<TextBoxSnapshot> CaptureTextBoxSnapshotsForSave(IEnumerable<DraggableTextBox> sourceTextBoxes = null)
        {
            var textBoxes = (sourceTextBoxes ?? _textBoxes).Where(tb => tb != null).ToList();
            var snapshots = new List<TextBoxSnapshot>(textBoxes.Count);
            foreach (var textBox in textBoxes)
            {
                snapshots.Add(textBox.CaptureSnapshotForSave());
            }

            return snapshots;
        }

        private async Task PersistTextElementsAsync(IEnumerable<DraggableTextBox> sourceTextBoxes = null)
        {
            if (_textElementPersistenceService == null)
            {
                throw new InvalidOperationException("文本持久化服务未初始化");
            }

            var snapshots = CaptureTextBoxSnapshotsForSave(sourceTextBoxes);
            await _textElementPersistenceService.SaveAsync(snapshots);
        }

        private async Task<TextEditorSaveResult> SaveTextEditorStateAsync(
            SaveTrigger trigger,
            IEnumerable<DraggableTextBox> sourceTextBoxes = null,
            bool persistAdditionalState = true,
            bool saveThumbnail = false,
            CancellationToken cancellationToken = default)
        {
            var snapshots = CaptureTextBoxSnapshotsForSave(sourceTextBoxes);

            if (_textEditorSaveOrchestrator == null)
            {
                throw new InvalidOperationException("文本保存编排服务未初始化。");
            }

            return await _textEditorSaveOrchestrator.SaveAsync(
                new TextEditorSaveRequest
                {
                    Trigger = trigger,
                    Snapshots = snapshots,
                    PersistAdditionalStateAsync = persistAdditionalState
                        ? _ => SaveSplitConfigAsync()
                        : null,
                    SaveThumbnailAsync = saveThumbnail && _currentSlide != null
                        ? _ =>
                        {
                            var path = SaveSlideThumbnail(_currentSlide.Id);
                            if (!string.IsNullOrEmpty(path))
                            {
                                _currentSlide.ThumbnailPath = path;
                            }

                            return Task.FromResult(path);
                        }
                        : null
                },
                cancellationToken);
        }

        /// <summary>
        /// 标记内容已修改（保存按钮变绿）
        /// </summary>
        private void MarkContentAsModified()
        {
            if (BtnSaveTextProject.Background is SolidColorBrush brush && brush.Color == Colors.LightGreen)
                return; // 已经是绿色，不重复设置

            BtnSaveTextProject.Background = new SolidColorBrush(Colors.LightGreen);
            //System.Diagnostics.Debug.WriteLine("内容已修改，保存按钮变绿");
        }

        /// <summary>
        ///  根据选中文字的实际样式更新工具栏按钮状态
        /// </summary>
        private void UpdateToolbarButtonStatesFromSelection()
        {
            if (_selectedTextBox == null) return;

            // 更新加粗按钮状态（使用选中文字的实际样式）
            UpdateBoldButtonState(_selectedTextBox.IsSelectionBold());

            // 更新斜体按钮状态（使用选中文字的实际样式）
            UpdateItalicButtonState(_selectedTextBox.IsSelectionItalic());

            // 更新下划线按钮状态（使用选中文字的实际样式）
            UpdateUnderlineButtonState(_selectedTextBox.IsSelectionUnderline());

            // 颜色图标色条跟随当前选中文字颜色
            UpdateSecondLayerTextColorIndicator(GetSelectedTextColorHex() ?? _selectedTextBox.Data?.FontColor);
            UpdateSecondLayerTextHighlightIndicator(GetSelectedTextHighlightColorHex());
        }

        /// <summary>
        /// 根据选中的文本框更新工具栏状态
        /// </summary>
        private void UpdateToolbarFromSelection()
        {
            if (_selectedTextBox == null) return;

            // 更新字体选择器
            var fontFamily = _selectedTextBox.Data.FontFamily;
            // //System.Diagnostics.Debug.WriteLine($"同步字体选择器: {fontFamily}");
            
            for (int i = 0; i < FontFamilySelector.Items.Count; i++)
            {
                var item = FontFamilySelector.Items[i] as ComboBoxItem;
                if (item?.Tag is FontItemData fontData)
                {
                    // 匹配字体：可能是完整URI，也可能是简单的字体族名称
                    var fontSource = fontData.FontFamily.Source;
                    
                    // 情况1：完全匹配（新格式：完整URI）
                    if (fontSource == fontFamily)
                    {
                        FontFamilySelector.SelectedIndex = i;
                        //System.Diagnostics.Debug.WriteLine($" 找到匹配字体（完整URI）: {fontData.Config.Name}");
                        break;
                    }
                    
                    // 情况2：旧数据格式匹配（只有字体族名称）
                    if (fontData.Config.Family == fontFamily)
                    {
                        FontFamilySelector.SelectedIndex = i;
                        //System.Diagnostics.Debug.WriteLine($" 找到匹配字体（族名称）: {fontData.Config.Name}");
                        
                        // 自动修复：更新文本框的字体为完整URI
                        _selectedTextBox.Data.FontFamily = fontSource;
                        //System.Diagnostics.Debug.WriteLine($"自动修复字体URI: {fontSource}");
                        break;
                    }
                }
            }

            // 更新字号选择框
            FontSizeSelector.Text = ((int)Math.Round(_selectedTextBox.Data.FontSize)).ToString();

            // 保持用户最后一次设置的颜色
            if (string.IsNullOrEmpty(_currentTextColor))
            {
                _currentTextColor = _selectedTextBox.Data.FontColor;
            }

            // 同步第二层颜色图标色条
            UpdateSecondLayerTextColorIndicator(_selectedTextBox.Data.FontColor);
            UpdateSecondLayerTextHighlightIndicator(GetSelectedTextHighlightColorHex());

            // 更新加粗按钮状态
            UpdateBoldButtonState(_selectedTextBox.Data.IsBoldBool);

            // 更新下划线按钮状态
            UpdateUnderlineButtonState(_selectedTextBox.Data.IsUnderlineBool);

            // 更新斜体按钮状态
            UpdateItalicButtonState(_selectedTextBox.Data.IsItalicBool);
        }
        
        /// <summary>
        /// 更新加粗按钮状态
        /// </summary>
        private void UpdateBoldButtonState(bool isBold)
        {
            var themeBrush = System.Windows.Application.Current?.Resources["BrushGlobalIcon"] as SolidColorBrush;
            var activeBrush = themeBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));

            if (BtnBold != null)
            {
                if (isBold)
                {
                    // 顶部按钮：高亮底色
                    BtnBold.Background = activeBrush;
                    BtnBold.Foreground = new SolidColorBrush(Colors.White);
                }
                else
                {
                    BtnBold.Background = new SolidColorBrush(Colors.White);
                    BtnBold.Foreground = activeBrush;
                }
            }

            if (BtnSecondLayerBold != null)
            {
                if (isBold)
                {
                    // 第二层按钮：与需求一致，B 高亮色块
                    BtnSecondLayerBold.Background = activeBrush;
                    BtnSecondLayerBold.Foreground = new SolidColorBrush(Colors.White);
                }
                else
                {
                    BtnSecondLayerBold.Background = new SolidColorBrush(Colors.Transparent);
                    BtnSecondLayerBold.Foreground = activeBrush;
                }
            }
        }

        /// <summary>
        /// 更新下划线按钮状态
        /// </summary>
        private void UpdateUnderlineButtonState(bool isUnderline)
        {
            if (BtnFloatingUnderline == null) return;
            var themeBrush = System.Windows.Application.Current?.Resources["BrushGlobalIcon"] as SolidColorBrush;
            var activeBrush = themeBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));

            if (isUnderline)
            {
                // 下划线状态：按钮背景使用当前主题色
                BtnFloatingUnderline.Background = activeBrush;
                BtnFloatingUnderline.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // 非下划线状态：恢复默认样式（透明背景）
                BtnFloatingUnderline.Background = new SolidColorBrush(Colors.Transparent);
                BtnFloatingUnderline.Foreground = activeBrush;
            }
        }

        /// <summary>
        /// 更新斜体按钮状态
        /// </summary>
        private void UpdateItalicButtonState(bool isItalic)
        {
            if (BtnFloatingItalic == null) return;
            var themeBrush = System.Windows.Application.Current?.Resources["BrushGlobalIcon"] as SolidColorBrush;
            var activeBrush = themeBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));

            if (isItalic)
            {
                // 斜体状态：按钮背景使用当前主题色
                BtnFloatingItalic.Background = activeBrush;
                BtnFloatingItalic.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // 非斜体状态：恢复默认样式（透明背景）
                BtnFloatingItalic.Background = new SolidColorBrush(Colors.Transparent);
                BtnFloatingItalic.Foreground = activeBrush;
            }
        }

        private void UpdateSecondLayerTextColorIndicator(string colorHex)
        {
            if (SecondLayerTextColorBar == null)
            {
                return;
            }

            var fallback = System.Windows.Application.Current?.Resources["BrushIconDefault"] as SolidColorBrush
                ?? System.Windows.Application.Current?.Resources["BrushGlobalIcon"] as SolidColorBrush
                ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 64, 67));

            if (string.IsNullOrWhiteSpace(colorHex) || string.Equals(colorHex, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                SecondLayerTextColorBar.Fill = fallback;
                return;
            }

            try
            {
                var converted = WpfColorConverter.ConvertFromString(colorHex);
                if (converted is WpfColor color)
                {
                    SecondLayerTextColorBar.Fill = new SolidColorBrush(color);
                    _currentTextColor = colorHex;
                    return;
                }
            }
            catch
            {
                // ignore and use fallback
            }

            SecondLayerTextColorBar.Fill = fallback;
        }

        private void UpdateSecondLayerTextHighlightIndicator(string colorHex)
        {
            if (SecondLayerTextHighlightBar == null)
            {
                return;
            }

            var fallback = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FF9800"));

            if (string.IsNullOrWhiteSpace(colorHex) || string.Equals(colorHex, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                SecondLayerTextHighlightBar.Fill = fallback;
                return;
            }

            try
            {
                var converted = WpfColorConverter.ConvertFromString(colorHex);
                if (converted is WpfColor color)
                {
                    SecondLayerTextHighlightBar.Fill = new SolidColorBrush(color);
                    return;
                }
            }
            catch
            {
                // ignore and use fallback
            }

            SecondLayerTextHighlightBar.Fill = fallback;
        }

        private string GetSelectedTextColorHex()
        {
            if (_selectedTextBox?.RichTextBox?.Selection == null)
            {
                return null;
            }

            var value = _selectedTextBox.RichTextBox.Selection
                .GetPropertyValue(System.Windows.Documents.TextElement.ForegroundProperty);

            if (value == DependencyProperty.UnsetValue || value == null)
            {
                return null;
            }

            if (value is SolidColorBrush brush)
            {
                var c = brush.Color;
                return c.A < 255
                    ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
                    : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }

            return null;
        }

        private string GetSelectedTextHighlightColorHex()
        {
            if (_selectedTextBox?.RichTextBox?.Selection == null)
            {
                return null;
            }

            var value = _selectedTextBox.RichTextBox.Selection
                .GetPropertyValue(System.Windows.Documents.TextElement.BackgroundProperty);

            if (value == DependencyProperty.UnsetValue || value == null)
            {
                return null;
            }

            if (value is SolidColorBrush brush)
            {
                var c = brush.Color;
                if (c.A == 0)
                {
                    return null;
                }

                return c.A < 255
                    ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
                    : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }

            return null;
        }
        
        /// <summary>
        /// 查找可视化树中的子元素
        /// </summary>
        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild && (string.IsNullOrEmpty(name) || typedChild.Name == name))
                {
                    return typedChild;
                }
                
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            
            return null;
        }

        private Services.TextEditor.Rendering.TextEditorProjectionCacheContext BuildProjectionCacheContext()
        {
            var textStates = _textBoxes
                .Where(tb => tb != null)
                .Select(tb => new Services.TextEditor.Rendering.TextEditorProjectionTextState
                {
                    Content = tb.Data.Content,
                    X = tb.Data.X,
                    Y = tb.Data.Y,
                    Width = tb.Data.Width,
                    Height = tb.Data.Height,
                    FontSize = tb.Data.FontSize,
                    FontFamily = tb.Data.FontFamily,
                    FontColor = tb.Data.FontColor,
                    IsBold = tb.Data.IsBoldBool,
                    IsItalic = tb.Data.IsItalicBool,
                    IsUnderline = tb.Data.IsUnderlineBool,
                    TextAlign = tb.Data.TextAlign,
                    TextVerticalAlign = tb.Data.TextVerticalAlign,
                    ZIndex = tb.Data.ZIndex,
                    BorderColor = tb.Data.BorderColor,
                    BorderWidth = tb.Data.BorderWidth,
                    BorderRadius = tb.Data.BorderRadius,
                    BorderOpacity = tb.Data.BorderOpacity,
                    BackgroundColor = tb.Data.BackgroundColor,
                    BackgroundRadius = tb.Data.BackgroundRadius,
                    BackgroundOpacity = tb.Data.BackgroundOpacity,
                    LineSpacing = tb.Data.LineSpacing,
                    LetterSpacing = tb.Data.LetterSpacing,
                    ComponentType = tb.Data.ComponentType,
                    ComponentConfigJson = tb.Data.ComponentConfigJson
                })
                .ToArray();

            return new Services.TextEditor.Rendering.TextEditorProjectionCacheContext
            {
                RegionImagePaths = _regionImagePaths,
                TextStates = textStates,
                SplitMode = _currentSlide?.SplitMode.ToString(),
                SplitDisplayMode = _splitImageDisplayMode.ToString(),
                BackgroundColor = _currentSlide?.BackgroundColor,
                BackgroundImagePath = _currentSlide?.BackgroundImagePath,
                BackgroundGradientEnabled = _currentSlide?.BackgroundGradientEnabled ?? false,
                BackgroundGradientStartColor = _currentSlide?.BackgroundGradientStartColor,
                BackgroundGradientEndColor = _currentSlide?.BackgroundGradientEndColor,
                BackgroundGradientDirection = _currentSlide?.BackgroundGradientDirection ?? 1,
                BackgroundOpacity = _currentSlide?.BackgroundOpacity ?? 0,
                BiblePopupOverlayVisible = _isBiblePopupOverlayVisible,
                BiblePopupOverlayReference = _biblePopupOverlayReference,
                BiblePopupOverlayContent = _biblePopupOverlayContent,
                BiblePopupOverlayPosition = (_biblePopupOverlayConfig?.PopupPosition ?? BiblePopupPosition.Bottom).ToString(),
                BiblePopupOverlayBackgroundColor = _biblePopupOverlayConfig?.PopupBackgroundColorHex,
                BiblePopupOverlayBackgroundOpacity = _biblePopupOverlayConfig?.PopupBackgroundOpacity ?? 0,
                BiblePopupOverlayScrollOffset = _biblePopupOverlayVerseScrollOffset
            };
        }

        private bool HasActiveNoticeAnimation(long nowMs)
        {
            foreach (var tb in _textBoxes)
            {
                if (!IsNoticeComponent(tb?.Data))
                {
                    continue;
                }

                var cfg = NoticeComponentConfigCodec.Deserialize(tb.Data.ComponentConfigJson);
                var state = _noticeRuntimeService.GetStateSnapshot(tb.Data.Id, nowMs);

                if (state.IsManuallyClosed)
                {
                    continue;
                }
                if (state.IsAutoPausedByTimeout)
                {
                    continue;
                }

                if (_noticeRuntimeService.IsExpired(state.ElapsedMs, cfg.DurationMinutes, cfg.AutoClose))
                {
                    if (_noticeRuntimeService.TryAutoPauseIfExpired(tb.Data.Id, nowMs, cfg.DurationMinutes, cfg.AutoClose))
                    {
                        QueueNoticeProjectionRefresh();
                    }
                    continue;
                }

                if (cfg.ScrollingEnabled)
                {
                    return true;
                }
            }

            return false;
        }

        private void QueueNoticeProjectionRefresh()
        {
            _noticeProjectionRefreshPending = true;
            _textEditorProjectionRenderStateService?.ClearCache();
        }

        private static long GetNoticeNowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private bool TryGetNoticeRenderOffset(DraggableTextBox textBox, long nowMs, out double offsetX, bool isProjectionRender = false)
        {
            offsetX = 0;
            if (!IsNoticeComponent(textBox?.Data))
            {
                return true;
            }

            var cfg = NoticeComponentConfigCodec.Deserialize(textBox.Data.ComponentConfigJson);
            var state = _noticeRuntimeService.GetStateSnapshot(textBox.Data.Id, nowMs);
            if (state.IsManuallyClosed)
            {
                return false;
            }
            if (state.IsAutoPausedByTimeout)
            {
                // 超时后：主屏保持静止显示，投影隐藏。
                return !isProjectionRender;
            }

            if (!cfg.ScrollingEnabled)
            {
                offsetX = 0;
                return true;
            }

            if (_noticeRuntimeService.IsExpired(state.ElapsedMs, cfg.DurationMinutes, cfg.AutoClose))
            {
                if (_noticeRuntimeService.TryAutoPauseIfExpired(textBox.Data.Id, nowMs, cfg.DurationMinutes, cfg.AutoClose))
                {
                    QueueNoticeProjectionRefresh();
                }
                return !isProjectionRender;
            }

            double canvasWidth = EditorCanvas?.ActualWidth > 0 ? EditorCanvas.ActualWidth : (_currentTextProject?.CanvasWidth > 0 ? _currentTextProject.CanvasWidth : 1600);
            double viewportWidth = canvasWidth;
            double laneStartX = EstimateNoticeContentStartInset(textBox);
            double laneEndX = EstimateNoticeContentEndInset(textBox);
            double contentWidth = EstimateNoticeContentWidth(textBox);
            double laneLeft = Math.Max(0.0, laneStartX);
            double laneRight = Math.Max(laneLeft + 1.0, viewportWidth - Math.Max(0.0, laneEndX));
            double contentStartX;

            // 使用缓存避免每帧重新测量字符边界
            var visualBounds = TryGetCachedNoticeVisualBounds(textBox);
            if (visualBounds.IsValid)
            {
                double visualWidth = Math.Max(1.0, visualBounds.LastX - visualBounds.FirstX);
                contentWidth = Math.Max(contentWidth, visualWidth);
            }

	// 根据方向确定起点位置：
	// - L->R（左对齐）：起点 = 左边界
	// - R->L（右对齐）：起点 = 右边界 - 内容宽度
	contentStartX = cfg.Direction == NoticeDirection.RightToLeft
		? Math.Max(laneLeft, laneRight - contentWidth)
		: laneLeft;

	// 对单向循环限制内容宽度，避免测宽误差导致回卷阈值失真；
	// 往返模式需保留真实宽度，否则"内容宽于轨道"时会出现近似静止。
	double collisionWidth = Math.Max(1.0, contentWidth);
            offsetX = _noticeRuntimeService.GetLoopingOffset(
                state.ElapsedMs,
                cfg.Speed,
                cfg.Direction,
                viewportWidth,
                collisionWidth,
                laneStartX,
                laneEndX,
                contentStartX);
            return true;
        }

        private double EstimateNoticeContentWidth(DraggableTextBox textBox)
        {
            if (textBox == null)
            {
                return 1.0;
            }

            string content = string.Empty;
            if (textBox.RichTextBox?.Document != null)
            {
                var range = new System.Windows.Documents.TextRange(
                    textBox.RichTextBox.Document.ContentStart,
                    textBox.RichTextBox.Document.ContentEnd);
                content = range.Text ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                content = textBox.Data?.Content ?? string.Empty;
            }

            content = (content ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(content))
            {
                return Math.Max(1.0, (textBox.ActualWidth > 0 ? textBox.ActualWidth : textBox.Data?.Width ?? 1.0) * 0.2);
            }

            string fontFamily = CleanFontFamilyName(textBox.Data?.FontFamily);
            float fontSize = (float)Math.Max(8.0, textBox.Data?.FontSize ?? 24.0);
            using var typeface = SKTypeface.FromFamilyName(fontFamily) ?? SKTypeface.Default;
            using var font = new SKFont
            {
                Typeface = typeface,
                Size = fontSize,
                Subpixel = true,
                Edging = SKFontEdging.Antialias
            };
            using var paint = new SKPaint { IsAntialias = true };

            float maxLineWidth = 0f;
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                float width = font.MeasureText(line ?? string.Empty, paint);
                if (width > maxLineWidth)
                {
                    maxLineWidth = width;
                }
            }

            // 单一路径：Skia + FormattedText + 启发式兜底。
            // 不再参与 WPF 文档/Extent，避免不同测量链路互相“打架”。
            double formattedWidth = EstimateNoticeContentWidthFromFormattedText(textBox, content, fontFamily, fontSize);
            // 按字符类型估算宽度，避免测量异常时得到“接近0”的宽度，导致碰撞点过晚。
            double heuristicWidth = EstimateNoticeContentWidthFromHeuristic(content, fontSize);

            // 主测量值：Skia 与 FormattedText 取大值，保证对字体回退更稳健。
            double primaryWidth = Math.Max(maxLineWidth, formattedWidth);
            if (primaryWidth < 1.0)
            {
                primaryWidth = heuristicWidth;
            }

            // 略偏保守地靠近启发式，优先避免“宽度偏小”导致离场后等待。
            double measuredWidth = Math.Max(primaryWidth, heuristicWidth * 0.8);

            // 当测量值显著偏小（例如仅 1~2px）时，使用启发式兜底。
            if (measuredWidth < heuristicWidth * 0.35)
            {
                measuredWidth = heuristicWidth;
            }

            return Math.Max(1.0, measuredWidth);
        }

        private double EstimateNoticeContentWidthFromFormattedText(
            DraggableTextBox textBox,
            string content,
            string fontFamily,
            float fontSize)
        {
            try
            {
                var family = new System.Windows.Media.FontFamily(string.IsNullOrWhiteSpace(fontFamily) ? "Microsoft YaHei UI" : fontFamily);
                var style = textBox.Data?.IsItalicBool == true ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
                var weight = textBox.Data?.IsBoldBool == true ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
                var typeface = new Typeface(family, style, weight, FontStretches.Normal);
                double pixelsPerDip = 1.0;
                if (EditorCanvas != null)
                {
                    pixelsPerDip = VisualTreeHelper.GetDpi(EditorCanvas).PixelsPerDip;
                }

                double maxWidth = 0;
                foreach (var line in (content ?? string.Empty).Split('\n'))
                {
                    var formatted = new FormattedText(
                        line ?? string.Empty,
                        CultureInfo.CurrentUICulture,
                        System.Windows.FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        System.Windows.Media.Brushes.Black,
                        pixelsPerDip);

                    maxWidth = Math.Max(maxWidth, formatted.WidthIncludingTrailingWhitespace);
                }

                return double.IsNaN(maxWidth) || double.IsInfinity(maxWidth) ? 0 : maxWidth;
            }
            catch
            {
                return 0;
            }
        }

        private static double EstimateNoticeContentWidthFromHeuristic(string content, float fontSize)
        {
            if (string.IsNullOrEmpty(content))
            {
                return fontSize * 0.8;
            }

            var lines = content.Split('\n');
            double maxUnits = 0;
            foreach (var line in lines)
            {
                double units = 0;
                foreach (char c in line ?? string.Empty)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        units += 0.35;
                    }
                    else if (c <= 0x7F)
                    {
                        units += 0.55;
                    }
                    else
                    {
                        units += 1.0;
                    }
                }
                maxUnits = Math.Max(maxUnits, units);
            }

            if (maxUnits <= 0.1)
            {
                maxUnits = 1.0;
            }

            return maxUnits * fontSize;
        }

        private static double EstimateNoticeContentStartInset(DraggableTextBox textBox)
        {
            if (textBox?.RichTextBox == null)
            {
                return 0;
            }

            double inset = 0;
            inset += Math.Max(0, textBox.RichTextBox.Padding.Left);
            inset += Math.Max(0, textBox.RichTextBox.Document?.PagePadding.Left ?? 0);
            return inset;
        }

        private static double EstimateNoticeContentEndInset(DraggableTextBox textBox)
        {
            if (textBox?.RichTextBox == null)
            {
                return 0;
            }

            double inset = 0;
            inset += Math.Max(0, textBox.RichTextBox.Padding.Right);
            inset += Math.Max(0, textBox.RichTextBox.Document?.PagePadding.Right ?? 0);
            return inset;
        }

        private NoticeVisualBoundsCache TryGetCachedNoticeVisualBounds(DraggableTextBox textBox)
        {
            if (textBox?.Data == null)
            {
                return new NoticeVisualBoundsCache { IsValid = false };
            }

            int textBoxId = textBox.Data.Id;
            string contentHash = ComputeNoticeContentHash(textBox);

            if (_noticeVisualBoundsCache.TryGetValue(textBoxId, out var cached))
            {
                if (cached.ContentHash == contentHash && cached.IsValid)
                {
                    return cached;
                }
            }

            if (TryMeasureNoticeContentVisualBounds(textBox, out double firstX, out double lastX))
            {
                var newCache = new NoticeVisualBoundsCache
                {
                    ContentHash = contentHash,
                    FirstX = firstX,
                    LastX = lastX,
                    IsValid = true
                };
                _noticeVisualBoundsCache[textBoxId] = newCache;
                return newCache;
            }

            return new NoticeVisualBoundsCache { IsValid = false };
        }

        private static string ComputeNoticeContentHash(DraggableTextBox textBox)
        {
            if (textBox?.RichTextBox?.Document == null)
            {
                return textBox?.Data?.Content ?? string.Empty;
            }

            var range = new System.Windows.Documents.TextRange(
                textBox.RichTextBox.Document.ContentStart,
                textBox.RichTextBox.Document.ContentEnd);
            return $"{range.Text}|{textBox.Data?.FontSize}|{textBox.Data?.FontFamily}";
        }

        private static bool TryMeasureNoticeContentVisualBounds(DraggableTextBox textBox, out double firstX, out double lastX)
        {
            firstX = 0;
            lastX = 0;

            var richTextBox = textBox?.RichTextBox;
            var doc = richTextBox?.Document;
            if (doc == null)
            {
                return false;
            }

            try
            {
                var pointer = doc.ContentStart?.GetInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);
                var docEnd = doc.ContentEnd?.GetInsertionPosition(System.Windows.Documents.LogicalDirection.Backward);
                if (pointer == null || docEnd == null || pointer.CompareTo(docEnd) > 0)
                {
                    return false;
                }

                bool hasAnyRect = false;
                double minLeft = double.MaxValue;
                double maxRight = double.MinValue;
                int guard = 0;
                const int maxProbeCount = 4096;

                while (pointer != null)
                {
                    AccumulateCharacterRect(pointer, System.Windows.Documents.LogicalDirection.Forward, ref hasAnyRect, ref minLeft, ref maxRight);
                    AccumulateCharacterRect(pointer, System.Windows.Documents.LogicalDirection.Backward, ref hasAnyRect, ref minLeft, ref maxRight);

                    if (pointer.CompareTo(docEnd) >= 0)
                    {
                        break;
                    }

                    var next = pointer.GetNextInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);
                    if (next == null || next.CompareTo(pointer) <= 0)
                    {
                        break;
                    }

                    pointer = next;
                    guard++;
                    if (guard > maxProbeCount)
                    {
                        break;
                    }
                }

                if (!hasAnyRect || maxRight <= minLeft)
                {
                    return false;
                }

                firstX = Math.Max(0, minLeft);
                lastX = Math.Max(firstX, maxRight);
                return (lastX - firstX) > 0.5;
            }
            catch
            {
                return false;
            }
        }

        private static void AccumulateCharacterRect(
            System.Windows.Documents.TextPointer pointer,
            System.Windows.Documents.LogicalDirection direction,
            ref bool hasAnyRect,
            ref double minLeft,
            ref double maxRight)
        {
            if (pointer == null)
            {
                return;
            }

            var rect = pointer.GetCharacterRect(direction);
            if (rect.IsEmpty)
            {
                return;
            }

            hasAnyRect = true;
            minLeft = Math.Min(minLeft, rect.Left);
            maxRight = Math.Max(maxRight, rect.Right);
        }

        private void EnsureNoticeAnimationLoopState()
        {
            if (TextEditorPanel == null || TextEditorPanel.Visibility != Visibility.Visible)
            {
                StopNoticeAnimationLoop(resetPreviewOffsets: true);
                UpdateNoticeToggleButtonState();
                return;
            }

            long nowMs = GetNoticeNowMs();
            bool hasActive = HasActiveNoticeAnimation(nowMs);
            ApplyNoticePreviewOffsets(nowMs);

            if (hasActive)
            {
                StartNoticeAnimationLoop();
            }
            else
            {
                StopNoticeAnimationLoop(resetPreviewOffsets: false);
            }

            UpdateNoticeToggleButtonState();
        }

        private void StartNoticeAnimationLoop()
        {
            if (!_noticeRenderingSubscribed)
            {
                System.Windows.Media.CompositionTarget.Rendering += NoticeAnimationRendering;
                _noticeRenderingSubscribed = true;
            }
        }

        private void StopNoticeAnimationLoop(bool resetPreviewOffsets)
        {
            if (_noticeRenderingSubscribed)
            {
                System.Windows.Media.CompositionTarget.Rendering -= NoticeAnimationRendering;
                _noticeRenderingSubscribed = false;
            }

            if (resetPreviewOffsets)
            {
                ResetNoticePreviewOffsets();
            }
        }

        private void NoticeAnimationRendering(object sender, EventArgs e)
        {
            _ = sender;
            _ = e;

            if (TextEditorPanel == null || TextEditorPanel.Visibility != Visibility.Visible)
            {
                StopNoticeAnimationLoop(resetPreviewOffsets: true);
                UpdateNoticeToggleButtonState();
                return;
            }

            long nowMs = GetNoticeNowMs();
            bool hasActive = HasActiveNoticeAnimation(nowMs);

            ApplyNoticePreviewOffsets(nowMs);

            if (_projectionManager?.IsProjectionActive == true)
            {
                bool forceProjectionRefresh = _noticeProjectionRefreshPending;
                if (forceProjectionRefresh || nowMs - _lastNoticeProjectionUpdateMs >= NoticeProjectionFrameIntervalMs)
                {
                    _lastNoticeProjectionUpdateMs = nowMs;
                    UpdateProjectionNoticeOverlay(nowMs);
                }
            }

            if (!hasActive)
            {
                StopNoticeAnimationLoop(resetPreviewOffsets: false);
                UpdateSecondLayerSelectedActionsVisibility(_selectedTextBox != null);
            }

            UpdateNoticeToggleButtonState();
        }

        private void ApplyNoticePreviewOffsets(long nowMs)
        {
            foreach (var textBox in _textBoxes)
            {
                if (!IsNoticeComponent(textBox?.Data))
                {
                    continue;
                }

                if (!TryGetNoticeRenderOffset(textBox, nowMs, out double offsetX, isProjectionRender: false))
                {
                    textBox.SetNoticeTextOffset(0);
                    textBox.Visibility = Visibility.Visible;
                    continue;
                }

                Canvas.SetLeft(textBox, textBox.Data.X);
                Canvas.SetTop(textBox, textBox.Data.Y);
                textBox.Visibility = Visibility.Visible;
                textBox.SetNoticeTextOffset(offsetX);
            }
        }

        private void ResetNoticePreviewOffsets()
        {
            foreach (var textBox in _textBoxes)
            {
                if (!IsNoticeComponent(textBox?.Data))
                {
                    continue;
                }

                Canvas.SetLeft(textBox, textBox.Data.X);
                Canvas.SetTop(textBox, textBox.Data.Y);
                textBox.SetNoticeTextOffset(0);
                textBox.Visibility = Visibility.Visible;
            }
        }

        private static IEnumerable<NoticePosition> EnumerateNoticePositions(NoticePositionFlags flags)
        {
            var normalized = NoticeComponentConfig.NormalizePositionFlags(flags);
            if ((normalized & NoticePositionFlags.Top) == NoticePositionFlags.Top)
            {
                yield return NoticePosition.Top;
            }

            if ((normalized & NoticePositionFlags.Center) == NoticePositionFlags.Center)
            {
                yield return NoticePosition.Center;
            }

            if ((normalized & NoticePositionFlags.Bottom) == NoticePositionFlags.Bottom)
            {
                yield return NoticePosition.Bottom;
            }
        }

        private double CalculateNoticePositionY(DraggableTextBox textBox, NoticePosition position)
        {
            if (textBox?.Data == null)
            {
                return 0;
            }

            double canvasHeight = EditorCanvas?.ActualHeight > 1
                ? EditorCanvas.ActualHeight
                : (_currentTextProject?.CanvasHeight > 0 ? _currentTextProject.CanvasHeight : 900);
            double boxHeight = textBox.ActualHeight > 1 ? textBox.ActualHeight : Math.Max(1, textBox.Data.Height);

            return position switch
            {
                NoticePosition.Center => Math.Max(0, (canvasHeight - boxHeight) / 2.0),
                NoticePosition.Bottom => Math.Max(0, canvasHeight - boxHeight),
                _ => 0
            };
        }

        private void UpdateProjectionNoticeOverlay(long nowMs = -1)
        {
            if (_projectionManager?.IsProjectionActive != true)
            {
                return;
            }

            if (_hideNoticeOnProjection)
            {
                _projectionManager.HideProjectionNoticeOverlay();
                _noticeProjectionRefreshPending = false;
                return;
            }

            long renderNowMs = nowMs >= 0 ? nowMs : GetNoticeNowMs();
            bool hasVisibleNotice = false;
            foreach (var textBox in _textBoxes)
            {
                if (!IsNoticeComponent(textBox?.Data))
                {
                    continue;
                }

                if (TryGetNoticeRenderOffset(textBox, renderNowMs, out _, isProjectionRender: true))
                {
                    hasVisibleNotice = true;
                    break;
                }
            }

            if (!hasVisibleNotice)
            {
                _projectionManager.HideProjectionNoticeOverlay();
                _noticeProjectionRefreshPending = false;
                return;
            }

            var (projWidth, projHeight) = _projectionManager.GetCurrentProjectionPhysicalSize();
            if (projWidth <= 0 || projHeight <= 0)
            {
                projWidth = 1920;
                projHeight = 1080;
            }

            var noticeLayer = _textEditorNoticeOverlayRenderService != null
                ? _textEditorNoticeOverlayRenderService.ExecuteSafely(
                    _textBoxes,
                    () => ComposeCanvasWithSkia(
                        projWidth,
                        projHeight,
                        transparentBackground: true,
                        textOnlyOverlay: true,
                        noticeOnlyOverlay: true,
                        hideNoticeComponents: false))
                : ComposeCanvasWithSkia(
                    projWidth,
                    projHeight,
                    transparentBackground: true,
                    textOnlyOverlay: true,
                    noticeOnlyOverlay: true,
                    hideNoticeComponents: false);

            try
            {
                _projectionManager.UpdateProjectionNoticeOverlay(noticeLayer);
            }
            finally
            {
                noticeLayer?.Dispose();
            }

            _noticeProjectionRefreshPending = false;
        }
        
        /// <summary>
        /// 从Canvas更新投影（核心投影功能）
        /// 优化：添加缓存机制和节流控制
        ///  新增：支持视频背景的 VisualBrush 镜像投影
        /// 新增：添加淡入淡出过渡动画
        /// </summary>
        private void UpdateProjectionFromCanvas()
        {
            bool suppressAnimationThisCall = ConsumeSuppressNextProjectionAnimation();
            long nowMs = GetNoticeNowMs();
            bool noticeAnimationActive = HasActiveNoticeAnimation(nowMs);
            bool noticeSelected = IsSelectedTextBoxNoticeComponent();

            if (noticeAnimationActive || noticeSelected)
            {
                // 通知组件只参与内容刷新，不参与幻灯片淡入淡出动画刷新。
                suppressAnimationThisCall = true;
            }

            if (_isBiblePopupOverlayVisible && _biblePopupOverlayEnterProgress < 0.999)
            {
                // 弹窗进入动画期间，强制关闭投影链路的二次淡入，避免主屏/投影透明度不一致。
                suppressAnimationThisCall = true;
            }
            _textEditorProjectionComposer?.Compose(new Services.TextEditor.Rendering.TextEditorProjectionComposeRequest
            {
                IsProjectionActive = _projectionManager.IsProjectionActive,
                AnimationEnabled = _projectionAnimationEnabled && !suppressAnimationThisCall,
                AnimationOpacity = _projectionAnimationOpacity,
                AnimationDurationMs = _projectionAnimationDuration,
                GetProjectionContainer = () => _projectionManager.GetProjectionContainer(),
                UpdateProjectionContent = UpdateProjectionContent,
                ShowProjectionNotActiveHint = () =>
                    WpfMessageBox.Show("请先开启投影！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning)
            });
        }

        /// <summary>
        /// 更新投影内容（实际更新逻辑）
        /// </summary>
        private void UpdateProjectionContent()
        {
            //  检查是否有视频背景
            bool hasVideoBackground = _currentSlide?.VideoBackgroundEnabled == true 
                                     && !string.IsNullOrEmpty(_currentSlide?.BackgroundImagePath);
            
            if (_isProjectionLocked)
            {
                //  锁定模式：使用独立的 MediaElement
                if (hasVideoBackground)
                {
                    // 主屏幕有视频：更新投影的独立 MediaElement
                    var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionPhysicalSize() ?? (1920, 1080);
                    var textLayer = ComposeCanvasWithSkia(
                        projWidth,
                        projHeight,
                        transparentBackground: true,
                        hideNoticeComponents: true);
                    _projectionManager.UpdateProjectionWithLockedVideo(
                        _currentSlide.BackgroundImagePath,
                        _currentSlide.VideoLoopEnabled,
                        textLayer);

                    // NDI 全投影：锁定视频路径下尽力输出整帧（视频帧采集未接入前，使用当前画布合成结果兜底）
                    var ndiFrame = ComposeCanvasWithSkia(
                        projWidth,
                        projHeight,
                        transparentBackground: false,
                        hideNoticeComponents: _hideNoticeOnProjection);
                    PublishSlideFrameToNdi(ndiFrame, projWidth, projHeight);
                    ndiFrame?.Dispose();
                }
                else
                {
                    // 主屏幕没有视频：清理投影的独立 MediaElement，使用静态背景模式
                    _projectionManager.ClearLockedVideo();
                    UpdateProjectionWithStaticBackground();
                }
            }
            else
            {
                //  未锁定模式：使用 VisualBrush（镜像主屏幕）
                if (hasVideoBackground)
                {
                    //  视频背景模式：使用 VisualBrush 镜像投影
                    UpdateProjectionWithVideoBackground();
                }
                else
                {
                    // 普通模式：使用 SkiaSharp 渲染投影
                    UpdateProjectionWithStaticBackground();
                }
            }

            UpdateProjectionNoticeOverlay();
        }

        /// <summary>
        ///  使用 VisualBrush 更新视频背景投影
        /// </summary>
        private void UpdateProjectionWithVideoBackground()
        {
#if DEBUG
            var startTime = System.Diagnostics.Stopwatch.StartNew();
            //System.Diagnostics.Debug.WriteLine($" [视频投影] ===== 开始更新视频投影 =====");
            //System.Diagnostics.Debug.WriteLine($" [视频投影] 使用 VisualBrush 镜像模式");
#endif

            // 查找编辑器 Canvas 中的 MediaElement
            var editorMedia = EditorCanvas.Children.OfType<MediaElement>().FirstOrDefault();
            if (editorMedia == null)
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [视频投影] 未找到编辑器中的 MediaElement，回退到静态模式");
#endif
                UpdateProjectionWithStaticBackground();
                return;
            }

#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[视频投影] 找到 MediaElement: {editorMedia.Source}");
            //System.Diagnostics.Debug.WriteLine($"[视频投影] MediaElement 状态: Position={editorMedia.Position}, NaturalDuration={editorMedia.NaturalDuration}");
#endif

            // 创建 VisualBrush 镜像
            var visualBrush = new VisualBrush(editorMedia)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            
            // 设置 VisualBrush 的缓存模式为 BitmapCache 以提高性能
            // 注意：BitmapCache 可能导致视频不更新，所以只在需要时启用
            // visualBrush.Visual.CacheMode = new BitmapCache();

#if DEBUG
            var brushTime = startTime.ElapsedMilliseconds;
            //System.Diagnostics.Debug.WriteLine($" [视频投影] 已创建 VisualBrush (耗时: {brushTime} ms)");
            //System.Diagnostics.Debug.WriteLine($"[视频投影] VisualBrush.Visual: {visualBrush.Visual?.GetType().Name}");
            //System.Diagnostics.Debug.WriteLine($"[视频投影] MediaElement.IsLoaded: {editorMedia.IsLoaded}, IsVisible: {editorMedia.IsVisible}");
#endif

            //  获取投影窗口尺寸
            var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionPhysicalSize() ?? (1920, 1080);

#if DEBUG
            //System.Diagnostics.Debug.WriteLine($" [视频投影] 投影窗口尺寸: {projWidth}x{projHeight}");
#endif

            // 渲染文本层（透明背景）
#if DEBUG
            var textLayerStartTime = System.Diagnostics.Stopwatch.StartNew();
#endif
            var textLayer = ComposeCanvasWithSkia(
                projWidth,
                projHeight,
                transparentBackground: true,
                hideNoticeComponents: true);
#if DEBUG
            textLayerStartTime.Stop();
            //System.Diagnostics.Debug.WriteLine($"[视频投影] 文本层渲染完成 (耗时: {textLayerStartTime.ElapsedMilliseconds} ms)");
#endif

            // 更新投影（传递 VisualBrush 和文本层）
#if DEBUG
            var updateStartTime = System.Diagnostics.Stopwatch.StartNew();
#endif
            _projectionManager.UpdateProjectionWithVideo(visualBrush, textLayer);

            // NDI 全投影：视频背景路径下尽力输出整帧（视频帧采集未接入前，使用当前画布合成结果兜底）
            var ndiFrame = ComposeCanvasWithSkia(
                projWidth,
                projHeight,
                transparentBackground: false,
                hideNoticeComponents: _hideNoticeOnProjection);
            PublishSlideFrameToNdi(ndiFrame, projWidth, projHeight);
            ndiFrame?.Dispose();
#if DEBUG
            updateStartTime.Stop();
            //System.Diagnostics.Debug.WriteLine($"[视频投影] ProjectionManager.UpdateProjectionWithVideo 完成 (耗时: {updateStartTime.ElapsedMilliseconds} ms)");
#endif

#if DEBUG
            startTime.Stop();
            //System.Diagnostics.Debug.WriteLine($" [视频投影] 投影已更新");
            //System.Diagnostics.Debug.WriteLine($"⏱ [视频投影] 总耗时: {startTime.ElapsedMilliseconds} ms");
            //System.Diagnostics.Debug.WriteLine($" [视频投影] ===== 更新完成 =====\n");
#endif
        }

        /// <summary>
        /// 使用 SkiaSharp 更新静态背景投影（原有逻辑）
        /// </summary>
        private void UpdateProjectionWithStaticBackground()
        {
#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[静态投影] 使用 SkiaSharp 渲染模式");
#endif

            // 渲染节流 - 避免过于频繁的更新
            var now = DateTime.UtcNow;
            if (!_isBiblePopupOverlayVisible &&
                _textEditorProjectionRenderStateService?.ShouldThrottle(now, CanvasUpdateThrottleMs) == true)
            {
                return;
            }

            // 缓存检查 - 如果Canvas内容没变，直接复用上次的渲染结果
            string cacheKey = _textEditorProjectionRenderStateService?.BuildCanvasCacheKey(BuildProjectionCacheContext()) ?? string.Empty;
            bool bypassCacheForPopupAnimation = _isBiblePopupOverlayVisible && _biblePopupOverlayEnterProgress < 0.999;
#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[静态投影-缓存] 检查缓存键: {cacheKey}");
#endif
            if (!bypassCacheForPopupAnimation &&
                _textEditorProjectionRenderStateService?.TryGetCached(cacheKey, out var cachedBitmap) == true &&
                cachedBitmap != null)
            {
                _projectionManager.UpdateProjectionTextFullFrame(cachedBitmap);
                PublishSlideFrameToNdi(cachedBitmap);
                return;
            }

            // 保存辅助线的可见性状态
            var guidesVisibility = AlignmentGuidesCanvas.Visibility;

            try
            {
                _textEditorRenderSafetyService.Execute(
                    _textBoxes,
                    renderAction: () =>
                    {
                        if (EditorCanvasContainer == null)
                        {
                            return;
                        }

                        // 使用物理像素分辨率（而非WPF单位），获得最高质量
                        var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionPhysicalSize() ?? (1920, 1080);
                        var finalImage = ComposeCanvasWithSkia(
                            projWidth,
                            projHeight,
                            hideNoticeComponents: true);

                        _projectionManager.UpdateProjectionTextFullFrame(finalImage);
                        PublishSlideFrameToNdi(finalImage, projWidth, projHeight);
                        _textEditorProjectionRenderStateService?.UpdateCache(cacheKey, finalImage);
                    },
                    beforeRenderAction: () =>
                    {
                        AlignmentGuidesCanvas.Visibility = Visibility.Collapsed;
                        HideSplitLinesForProjection();
                    },
                    afterRenderAction: () =>
                    {
                        AlignmentGuidesCanvas.Visibility = guidesVisibility;
                        RestoreSplitLinesAfterProjection();
                    });

#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [静态投影] 渲染完成");
#endif
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [更新投影] 更新投影失败: {ex.Message}");
                //#endif
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [更新投影] 堆栈: {ex.StackTrace}");
                //#endif
                WpfMessageBox.Show($"更新投影失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PublishSlideFrameToNdi(SKBitmap frame, int projectionWidth = 0, int projectionHeight = 0)
        {
            if (_projectionNdiOutputManager == null || frame == null)
            {
                return;
            }

            if (_currentSlide?.OutputMode == SlideOutputMode.Transparent)
            {
                int targetWidth = projectionWidth > 0 ? projectionWidth : frame.Width;
                int targetHeight = projectionHeight > 0 ? projectionHeight : frame.Height;
                using var transparentFrame = ComposeCanvasWithSkia(
                    targetWidth,
                    targetHeight,
                    transparentBackground: true,
                    textOnlyOverlay: true,
                    hideNoticeComponents: _hideNoticeOnProjection);
                _projectionNdiOutputManager.PublishFrame(transparentFrame, ProjectionNdiContentType.SlideTransparent);
                return;
            }

            _projectionNdiOutputManager.PublishFrame(frame, ProjectionNdiContentType.Slide);
        }

        /// <summary>
        /// 使用SkiaSharp直接合成Canvas内容（跳过WPF的RenderTargetBitmap）
        /// 核心优化：直接访问Image控件的Source，避免WPF渲染管道
        ///  新增：支持透明背景（用于视频背景的文本层）
        /// </summary>
        /// <param name="targetWidth">目标宽度（0表示使用Canvas实际宽度）</param>
        /// <param name="targetHeight">目标高度（0表示使用Canvas实际高度）</param>
        /// <param name="transparentBackground">是否使用透明背景（true=透明，false=使用幻灯片背景色）</param>
        /// <param name="textOnlyOverlay">是否只渲染文本层（忽略背景图、分割图与分割线）</param>
        /// <param name="noticeOnlyOverlay">是否只渲染通知组件（用于投影侧通知覆盖层）</param>
        /// <param name="hideNoticeComponents">是否跳过通知组件绘制（用于仅投影隐藏通知）</param>
        private SKBitmap ComposeCanvasWithSkia(
            int targetWidth = 0,
            int targetHeight = 0,
            bool transparentBackground = false,
            bool textOnlyOverlay = false,
            bool noticeOnlyOverlay = false,
            bool hideNoticeComponents = false)
        {
            // 编辑器画布的实际尺寸（用于计算缩放比例）
            double canvasWidth = EditorCanvas.ActualWidth;
            double canvasHeight = EditorCanvas.ActualHeight;
            
            // 如果没有指定目标尺寸，使用Canvas实际尺寸
            if (targetWidth <= 0) targetWidth = (int)canvasWidth;
            if (targetHeight <= 0) targetHeight = (int)canvasHeight;
            
            // 计算缩放比例
            double scaleX = targetWidth / canvasWidth;
            double scaleY = targetHeight / canvasHeight;

            // 保存缩放比例到字段
            _currentCanvasScaleX = scaleX;
            _currentCanvasScaleY = scaleY;

//#if DEBUG
//            //System.Diagnostics.Debug.WriteLine($" [画布缩放] 原始={canvasWidth}×{canvasHeight}, 目标={targetWidth}×{targetHeight}, 缩放={scaleX:F2}×{scaleY:F2}");
//#endif

            // 优先使用GPU表面，如果GPU不可用则降级到CPU
            SKBitmap bitmap = null;
            SKCanvas canvas = null;
            SKSurface surface = null;
            
            try
            {
                // 尝试创建GPU表面
                var gpuContext = _gpuContext;
                if (gpuContext.IsGpuAvailable && gpuContext.GetContext() != null)
                {
                    var grContext = gpuContext.GetContext();
                    var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                    surface = SKSurface.Create(grContext, false, info);
                    
                    if (surface != null)
                    {
                        canvas = surface.Canvas;
                        // GPU表面创建成功，使用GPU渲染
                    }
                }
            }
            catch
            {
                // GPU表面创建失败，降级到CPU
            }
            
            // 如果GPU表面创建失败，使用CPU表面
            if (canvas == null)
            {
                bitmap = new SKBitmap(targetWidth, targetHeight);
                canvas = new SKCanvas(bitmap);
            }
            
            try
            {
                bool useSlideGradientBackground =
                    !transparentBackground &&
                    _currentSlide != null &&
                    HasSlideGradientBackground(_currentSlide);

                if (transparentBackground)
                {
                    canvas.Clear(SKColors.Transparent);
                }
                else if (useSlideGradientBackground)
                {
                    canvas.Clear(SKColors.Transparent);
                    using var gradientShader = BuildSlideBackgroundGradientShader(_currentSlide, targetWidth, targetHeight);
                    if (gradientShader != null)
                    {
                        using var gradientPaint = new SKPaint
                        {
                            IsAntialias = true,
                            Shader = gradientShader
                        };
                        canvas.DrawRect(new SKRect(0, 0, targetWidth, targetHeight), gradientPaint);
                    }
                    else
                    {
                        canvas.Clear(GetSlideSolidBackgroundColor(_currentSlide));
                    }
                }
                else
                {
                    canvas.Clear(GetSlideSolidBackgroundColor(_currentSlide));
                }

                // 应用缩放变换（X 和 Y 方向可以不同，铺满整个投影屏幕）
                // 注意：文本框使用 WPF 原生渲染，已经是最终视觉效果，缩放不会影响行间距
                canvas.Scale((float)scaleX, (float)scaleY);
                
                //#if DEBUG
                //createSw.Stop();
                //System.Diagnostics.Debug.WriteLine($"  [Compose] 创建画布: {createSw.ElapsedMilliseconds}ms");
                //#endif
                
                // 绘制背景图（如果有）
                if (!textOnlyOverlay &&
                    _currentSlide != null && !string.IsNullOrEmpty(_currentSlide.BackgroundImagePath) &&
                    System.IO.File.Exists(_currentSlide.BackgroundImagePath))
                {
                    try
                    {
                        //#if DEBUG
                        //var bgSw = System.Diagnostics.Stopwatch.StartNew();
                        //#endif
                        
                        // 加载背景图
                        var bgBitmap = SKBitmap.Decode(_currentSlide.BackgroundImagePath);
                        if (bgBitmap != null)
                        {
                            // 绘制背景图，铺满整个画布（使用原始尺寸，canvas.Scale 会自动缩放）
                            var destRect = new SKRect(0, 0, (float)canvasWidth, (float)canvasHeight);
                            // 使用 DrawImage 替代 DrawBitmap，支持 SKSamplingOptions
                            using var image = SKImage.FromBitmap(bgBitmap);
                            var paint = new SKPaint
                            {
                                IsAntialias = true
                            };
                            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                            // 使用 DrawImage(SKImage, SKRect, SKSamplingOptions, SKPaint) 重载
                            canvas.DrawImage(image, destRect, sampling, paint);
                            paint.Dispose();
                            bgBitmap.Dispose();
                            
                            //#if DEBUG
                            //bgSw.Stop();
                            //System.Diagnostics.Debug.WriteLine($"  [Compose] 背景图绘制: {_currentSlide.BackgroundImagePath}, 耗时: {bgSw.ElapsedMilliseconds}ms");
                            //#endif
                        }
                    }
                    catch
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 背景图加载失败: {ex.Message}");
                        //#endif
                    }
                }
                
                // 绘制所有区域图片
                if (!textOnlyOverlay)
                {
                    foreach (var kvp in _regionImages)
                    {
                        var imageControl = kvp.Value;
                        int regionIndex = kvp.Key;
                    
                    //#if DEBUG
                    //var imgSw = System.Diagnostics.Stopwatch.StartNew();
                    //#endif
                    
                    // 统一使用分割区域边框的位置和尺寸，避免 Image 控件为置顶模式偏移后影响投影计算。
                    double left = Canvas.GetLeft(_splitRegionBorders[regionIndex]);
                    double top = Canvas.GetTop(_splitRegionBorders[regionIndex]);
                    double width = _splitRegionBorders[regionIndex].Width;
                    double height = _splitRegionBorders[regionIndex].Height;
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - Image控件位置: ({left}, {top}), 尺寸: {width}×{height}, Stretch: {imageControl.Stretch}");
                    //System.Diagnostics.Debug.WriteLine($"    边框信息: 位置=({Canvas.GetLeft(_splitRegionBorders[regionIndex])}, {Canvas.GetTop(_splitRegionBorders[regionIndex])}), 尺寸={_splitRegionBorders[regionIndex].Width}×{_splitRegionBorders[regionIndex].Height}");
                    //#endif
                    
                    SKBitmap skBitmap = null;
                    
                    // 优先从原始文件加载高质量图片
                    if (_regionImagePaths.ContainsKey(regionIndex) && 
                        System.IO.File.Exists(_regionImagePaths[regionIndex]))
                    {
                        try
                        {
                            string imagePath = _regionImagePaths[regionIndex];
                            skBitmap = SKBitmap.Decode(imagePath);
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - 原始图片尺寸: {skBitmap.Width}×{skBitmap.Height}");
                            //#endif
                            
                            // 检查是否需要应用变色效果
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - 检查变色状态");
                            //System.Diagnostics.Debug.WriteLine($"   _regionImageColorEffects.ContainsKey: {_regionImageColorEffects.ContainsKey(regionIndex)}");
                            //if (_regionImageColorEffects.ContainsKey(regionIndex))
                            //{
                            //    //System.Diagnostics.Debug.WriteLine($"   需要变色: {_regionImageColorEffects[regionIndex]}");
                            //}
                            //#endif
                            
                            if (_regionImageColorEffects.ContainsKey(regionIndex) && 
                                _regionImageColorEffects[regionIndex])
                            {
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - 开始应用变色效果到投影");
                                //#endif
                                
                                // 应用变色效果
                                _imageProcessor.ApplyYellowTextEffect(skBitmap);
                                
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($" [Compose] 区域 {regionIndex} - 变色效果已应用");
                                //#endif
                            }
                            else
                            {
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"⚪ [Compose] 区域 {regionIndex} - 无需变色效果");
                                //#endif
                            }
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [Compose] 处理图片 {regionIndex}: 从原始文件加载 {skBitmap.Width}×{skBitmap.Height}, 位置: ({left}, {top}), 显示: {width}×{height}");
                            //#endif
                        }
                        catch
                        {
                            // 加载失败，回退到BitmapSource
                            skBitmap = null;
                        }
                    }
                    
                    // 回退方案：从WPF控件的BitmapSource转换
                    if (skBitmap == null && imageControl?.Source is BitmapSource bitmapSource)
                    {
                        skBitmap = ConvertBitmapSourceToSKBitmap(bitmapSource);
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 处理图片 {regionIndex}: 从BitmapSource转换 {bitmapSource.PixelWidth}×{bitmapSource.PixelHeight}, 位置: ({left}, {top}), 显示: {width}×{height}");
                        //#endif
                    }
                    
                        if (skBitmap != null)
                        {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 加载耗时: {imgSw.ElapsedMilliseconds}ms");
                        //imgSw.Restart();
                        //#endif
                        
                        // 根据分割图片显示模式计算实际绘制区域
                        SKRect destRect;
                        if (_splitImageDisplayMode == SplitImageDisplayMode.Fill)
                        {
                            // Fill模式：拉伸填满整个控件区域
                            destRect = new SKRect((float)left, (float)top,
                                                   (float)(left + width), (float)(top + height));
                        }
                        else
                        {
                            // FitCenter / FitTop：保持比例
                            double imageAspect = (double)skBitmap.Width / skBitmap.Height;
                            double controlAspect = width / height;
                            
                            double drawWidth, drawHeight;
                            double drawLeft, drawTop;
                            
                            double aspectDiff = Math.Abs(imageAspect - controlAspect);
                            if (aspectDiff < 0.001)
                            {
                                // 宽高比几乎相等：填满整个控件区域
                                drawWidth = width;
                                drawHeight = height;
                                drawLeft = left;
                                drawTop = top;
                            }
                            else if (imageAspect > controlAspect)
                            {
                                // 图片更宽（更扁），以宽度为准
                                drawWidth = width;
                                drawHeight = width / imageAspect;
                                drawLeft = left;
                                drawTop = _splitImageDisplayMode == SplitImageDisplayMode.FitTop
                                    ? top
                                    : top + (height - drawHeight) / 2; // FitCenter 垂直居中
                            }
                            else
                            {
                                // 图片更高（更瘦），以高度为准
                                drawHeight = height;
                                drawWidth = height * imageAspect;
                                drawLeft = left + (width - drawWidth) / 2; // 水平居中
                                drawTop = top; // 高图天然撑满高度，顶部即为区域顶部
                            }
                            
                            destRect = new SKRect((float)drawLeft, (float)drawTop,
                                                   (float)(drawLeft + drawWidth), (float)(drawTop + drawHeight));
                        }
                        
                        // 使用高质量过滤模式，确保投影质量
                        // 使用 DrawImage 替代 DrawBitmap，支持 SKSamplingOptions
                        using var image = SKImage.FromBitmap(skBitmap);
                        var paint = new SKPaint
                        {
                            IsAntialias = true
                        };
                        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        // 使用 DrawImage(SKImage, SKRect, SKSamplingOptions, SKPaint) 重载
                        canvas.DrawImage(image, destRect, sampling, paint);
                        paint.Dispose();
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 绘制耗时: {imgSw.ElapsedMilliseconds}ms");
                        //#endif
                        
                            skBitmap.Dispose();
                        }
                    }
                }
                
                // 绘制所有文本框
                long nowMsForNotice = GetNoticeNowMs();
                foreach (var textBox in _textBoxes)
                {
                    //#if DEBUG
                    //var textSw = System.Diagnostics.Stopwatch.StartNew();
                    //System.Diagnostics.Debug.WriteLine($"  [Compose] 文本框位置: ({textBox.Data.X}, {textBox.Data.Y}), 尺寸: {textBox.Data.Width}×{textBox.Data.Height}");
                    //#endif

                    bool isNotice = IsNoticeComponent(textBox?.Data);
                    if (noticeOnlyOverlay && !isNotice)
                    {
                        continue;
                    }

                    if (hideNoticeComponents && isNotice)
                    {
                        continue;
                    }

                    if (!TryGetNoticeRenderOffset(textBox, nowMsForNotice, out double noticeOffsetX, isProjectionRender: true))
                    {
                        continue;
                    }

                    if (isNotice)
                    {
                        var cfg = NoticeComponentConfigCodec.Deserialize(textBox.Data.ComponentConfigJson);
                        var flags = NoticeComponentConfig.NormalizePositionFlags(cfg.PositionFlags);
                        var currentPosition = DetectNoticePositionFromCurrentY(textBox);

                        foreach (var position in EnumerateNoticePositions(flags))
                        {
                            double? topOverride = position == currentPosition
                                ? null
                                : CalculateNoticePositionY(textBox, position);
                            DrawTextBoxToCanvas(canvas, textBox, noticeOffsetX, topOverride);
                        }
                    }
                    else
                    {
                        DrawTextBoxToCanvas(canvas, textBox, noticeOffsetX);
                    }
                    
                    //#if DEBUG
                    //textSw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"  [Compose] 绘制文本框: {textSw.ElapsedMilliseconds}ms");
                    //#endif
                }
                
                // 绘制分割线（如果有分割模式）
                if (!textOnlyOverlay &&
                    !noticeOnlyOverlay &&
                    _currentSlide != null && _currentSlide.SplitMode >= 0)
                {
                    DrawSplitLinesToCanvas(canvas, (Database.Models.Enums.ViewSplitMode)_currentSlide.SplitMode, canvasWidth, canvasHeight);
                }

                // 圣经弹窗作为“幻灯片画布衍生层”参与同一合成链路，
                // 保证投影与主屏遵循同一坐标体系与缩放关系。
                if (!noticeOnlyOverlay)
                {
                    DrawBiblePopupOverlayToCanvas(canvas, canvasWidth, canvasHeight);
                }
                
                // 如果是GPU表面，需要刷新并获取快照
                if (surface != null)
                {
                    canvas.Flush();
                    var snapshot = surface.Snapshot();
                    bitmap = SKBitmap.FromImage(snapshot);
                }
            }
            finally
            {
                // 清理资源
                if (surface != null)
                {
                    surface.Dispose();
                }
                if (canvas != null && bitmap != null && surface == null)
                {
                    // 只有CPU表面才需要释放canvas（GPU表面的canvas会随surface一起释放）
                    canvas.Dispose();
                }
            }
            
            return bitmap;
        }

        private void DrawBiblePopupOverlayToCanvas(SKCanvas canvas, double canvasWidth, double canvasHeight)
        {
            if (!_isBiblePopupOverlayVisible || canvas == null || canvasWidth <= 0 || canvasHeight <= 0)
            {
                return;
            }

            var cfg = _biblePopupOverlayConfig ?? new BibleTextInsertConfig();
            float maxPopupWidth = (float)Math.Min(canvasWidth - 60.0, 1500.0);
            float popupWidth = Math.Max(480f, maxPopupWidth);
            float popupX = (float)((canvasWidth - popupWidth) / 2.0);

            using var titleFont = new SKFont
            {
                Typeface = SKTypeface.FromFamilyName(cfg.PopupFontFamily ?? cfg.FontFamily ?? "Microsoft YaHei UI"),
                Size = Math.Max(16f, cfg.PopupTitleStyle.FontSize),
                Subpixel = true,
                Edging = SKFontEdging.Antialias
            };
            using var verseFont = new SKFont
            {
                Typeface = SKTypeface.FromFamilyName(cfg.PopupFontFamily ?? cfg.FontFamily ?? "Microsoft YaHei UI"),
                Size = Math.Max(16f, cfg.PopupVerseStyle.FontSize),
                Subpixel = true,
                Edging = SKFontEdging.Antialias
            };
            using var verseNumberFont = new SKFont
            {
                Typeface = SKTypeface.FromFamilyName(cfg.PopupFontFamily ?? cfg.FontFamily ?? "Microsoft YaHei UI"),
                Size = Math.Max(16f, cfg.PopupVerseNumberStyle.FontSize),
                Subpixel = true,
                Edging = SKFontEdging.Antialias
            };
            using var titlePaint = new SKPaint
            {
                IsAntialias = true,
                Color = ToSkColor(cfg.PopupTitleStyle.ColorHex, 0)
            };
            using var versePaint = new SKPaint
            {
                IsAntialias = true,
                Color = ToSkColor(cfg.PopupVerseStyle.ColorHex, 0)
            };
            using var verseNumberPaint = new SKPaint
            {
                IsAntialias = true,
                Color = ToSkColor(cfg.PopupVerseNumberStyle.ColorHex, 0)
            };
            using var borderPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                Color = new SKColor(255, 255, 255, 60)
            };
            using var bgPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = ToSkColor(cfg.PopupBackgroundColorHex, Math.Clamp(cfg.PopupBackgroundOpacity, 0, 100))
            };
            bool hideBorder = BiblePopupOpacity.ShouldHideBorder(cfg.PopupBackgroundOpacity);

            float lineHeight = Math.Max(verseFont.Size * (float)Math.Max(1.0, cfg.PopupVerseStyle.VerseSpacing), verseFont.Size * 1.2f);
            float titleHeight = titleFont.Size * 1.25f;
            float titleBottomGap = 10f;
            float leftPad = 36f;
            float rightPad = 36f;
            float topPad = 24f;
            float bottomPad = 24f;
            float textMaxWidth = popupWidth - leftPad - rightPad;

            var titleLines = WrapTextByWidth(_biblePopupOverlayReference ?? string.Empty, titleFont, titlePaint, textMaxWidth);
            var verseLayout = BuildPopupVerseLayout(_biblePopupOverlayContent ?? string.Empty, verseFont, versePaint, textMaxWidth, lineHeight);
            var verseLines = verseLayout.WrappedLines;
            int titleCount = Math.Max(1, titleLines.Count);
            int verseCount = Math.Max(1, verseLines.Count);
            float verseViewportHeight = lineHeight * Math.Clamp(cfg.PopupVerseCount, 1, 10);
            float verseContentHeight = verseCount * lineHeight;
            _biblePopupOverlayVerseMaxScroll = Math.Max(0, verseContentHeight - verseViewportHeight);
            _biblePopupOverlayVerseScrollOffset = Math.Clamp(_biblePopupOverlayVerseScrollOffset, 0, _biblePopupOverlayVerseMaxScroll);
            _biblePopupOverlayVerseAnchors = verseLayout.VerseStartOffsets
                .Select(offset => (double)Math.Clamp(offset, 0, _biblePopupOverlayVerseMaxScroll))
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            float contentHeight = titleCount * titleHeight + titleBottomGap + verseViewportHeight;
            float popupHeight = topPad + contentHeight + bottomPad;

            float popupY = cfg.PopupPosition switch
            {
                BiblePopupPosition.Top => 0f,
                BiblePopupPosition.Center => (float)((canvasHeight - popupHeight) / 2.0),
                _ => (float)(canvasHeight - popupHeight - 40.0)
            };
            if (popupY < 0f) popupY = 0f;
            _biblePopupOverlayLastRect = new Rect(popupX, popupY, popupWidth, popupHeight);

            double animProgress = _biblePopupAnimationEnabled
                ? Math.Clamp(_biblePopupOverlayEnterProgress, 0.0, 1.0)
                : 1.0;
            double animEase = EaseOutCubic(animProgress);
            double startAlpha = Math.Clamp(_biblePopupAnimationOpacity, 0.0, 1.0);
            float animAlphaMul = (float)Math.Clamp(startAlpha + ((1.0 - startAlpha) * animEase), 0.0, 1.0);
            string animationType = ResolveBiblePopupAnimationType(cfg);
            var clipRect = BuildPopupEnterClipRect(animationType, popupX, popupY, popupWidth, popupHeight, (float)animEase);

            titlePaint.Color = MultiplyAlpha(titlePaint.Color, animAlphaMul);
            versePaint.Color = MultiplyAlpha(versePaint.Color, animAlphaMul);
            verseNumberPaint.Color = MultiplyAlpha(verseNumberPaint.Color, animAlphaMul);
            borderPaint.Color = MultiplyAlpha(borderPaint.Color, animAlphaMul);
            bgPaint.Color = MultiplyAlpha(bgPaint.Color, animAlphaMul);

            bool isZoomIn = string.Equals(animationType, "ZoomIn", StringComparison.Ordinal);
            bool isPushTop = string.Equals(animationType, "TopPush", StringComparison.Ordinal);
            bool isPushBottom = string.Equals(animationType, "BottomPush", StringComparison.Ordinal);
            bool needsClip = string.Equals(animationType, "TopReveal", StringComparison.Ordinal) ||
                             string.Equals(animationType, "BottomReveal", StringComparison.Ordinal) ||
                             isPushTop ||
                             isPushBottom;

            canvas.Save();
            if (isZoomIn)
            {
                float cx = popupX + (popupWidth / 2f);
                float cy = popupY + (popupHeight / 2f);
                float scale = (float)(0.88 + (0.12 * animEase));
                canvas.Translate(cx, cy);
                canvas.Scale(scale, scale);
                canvas.Translate(-cx, -cy);
            }

            if (isPushTop)
            {
                float dy = (float)(-Math.Min(80.0, popupHeight * 0.35) * (1.0 - animEase));
                canvas.Translate(0f, dy);
            }
            else if (isPushBottom)
            {
                float dy = (float)(Math.Min(80.0, popupHeight * 0.35) * (1.0 - animEase));
                canvas.Translate(0f, dy);
            }

            if (needsClip)
            {
                canvas.ClipRect(clipRect);
            }

            var rect = new SKRoundRect(new SKRect(popupX, popupY, popupX + popupWidth, popupY + popupHeight), 16f, 16f);
            canvas.DrawRoundRect(rect, bgPaint);
            if (!hideBorder)
            {
                canvas.DrawRoundRect(rect, borderPaint);
            }

            float y = popupY + topPad + titleFont.Size;
            foreach (var line in titleLines)
            {
                canvas.DrawText(line, popupX + leftPad, y, SKTextAlign.Left, titleFont, titlePaint);
                y += titleHeight;
            }

            y += titleBottomGap;
            var verseViewportRect = new SKRect(
                popupX + leftPad,
                y - verseFont.Size,
                popupX + popupWidth - rightPad,
                y - verseFont.Size + verseViewportHeight);
            _biblePopupOverlayLastVerseViewportRect = new Rect(
                verseViewportRect.Left,
                verseViewportRect.Top,
                verseViewportRect.Width,
                verseViewportRect.Height);

            canvas.Save();
            canvas.ClipRect(verseViewportRect);
            float verseY = y - (float)_biblePopupOverlayVerseScrollOffset;
            foreach (var line in verseLines)
            {
                DrawPopupVerseLineWithNumberStyle(canvas, line, popupX + leftPad, verseY, verseFont, versePaint, verseNumberFont, verseNumberPaint);
                verseY += lineHeight;
            }
            canvas.Restore();
            canvas.Restore();
        }

        private string ResolveBiblePopupAnimationType(BibleTextInsertConfig cfg)
        {
            string type = _biblePopupAnimationType;
            return type switch
            {
                "FadeOnly" => "FadeOnly",
                "TopPush" => "TopPush",
                "BottomPush" => "BottomPush",
                "TopReveal" => "TopReveal",
                "BottomReveal" => "BottomReveal",
                "ZoomIn" => "ZoomIn",
                _ => cfg.PopupPosition switch
                {
                    BiblePopupPosition.Top => "TopReveal",
                    BiblePopupPosition.Center => "ZoomIn",
                    _ => "BottomReveal"
                }
            };
        }

        private sealed class BiblePopupVerseLayout
        {
            public List<string> WrappedLines { get; } = new();
            public List<float> VerseStartOffsets { get; } = new();
        }

        private static BiblePopupVerseLayout BuildPopupVerseLayout(string content, SKFont font, SKPaint paint, float maxWidth, float lineHeight)
        {
            var result = new BiblePopupVerseLayout();
            var paragraphs = (content ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            float currentOffset = 0f;

            foreach (var paragraph in paragraphs)
            {
                result.VerseStartOffsets.Add(currentOffset);
                var wrapped = WrapTextByWidth(paragraph, font, paint, maxWidth);
                foreach (var line in wrapped)
                {
                    result.WrappedLines.Add(line);
                    currentOffset += lineHeight;
                }
            }

            if (result.WrappedLines.Count == 0)
            {
                result.WrappedLines.Add(string.Empty);
                result.VerseStartOffsets.Add(0f);
            }

            return result;
        }

        private static void DrawPopupVerseLineWithNumberStyle(
            SKCanvas canvas,
            string line,
            float x,
            float y,
            SKFont verseFont,
            SKPaint versePaint,
            SKFont verseNumberFont,
            SKPaint verseNumberPaint)
        {
            if (canvas == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(line))
            {
                canvas.DrawText(string.Empty, x, y, SKTextAlign.Left, verseFont, versePaint);
                return;
            }

            var match = Regex.Match(line, @"^\s*(?<num>\d+(?:[、,，\-]\d+)*)\s+(?<rest>.+)$");
            if (!match.Success)
            {
                canvas.DrawText(line, x, y, SKTextAlign.Left, verseFont, versePaint);
                return;
            }

            string numberText = match.Groups["num"].Value;
            string numberLeadSpaces = line.Substring(0, match.Groups["num"].Index);
            string restText = match.Groups["rest"].Value;
            string separator = " ";
            string numberPart = numberLeadSpaces + numberText + separator;

            canvas.DrawText(numberPart, x, y, SKTextAlign.Left, verseNumberFont, verseNumberPaint);
            float numberWidth = verseNumberFont.MeasureText(numberPart, verseNumberPaint);
            canvas.DrawText(restText, x + numberWidth, y, SKTextAlign.Left, verseFont, versePaint);
        }

        private static SKRect BuildPopupEnterClipRect(string animationType, float x, float y, float width, float height, float progress)
        {
            float revealHeight = Math.Max(1f, height * Math.Clamp(progress, 0f, 1f));
            if (string.Equals(animationType, "BottomReveal", StringComparison.Ordinal) ||
                string.Equals(animationType, "BottomPush", StringComparison.Ordinal))
            {
                return new SKRect(x, y + height - revealHeight, x + width, y + height);
            }

            if (string.Equals(animationType, "TopReveal", StringComparison.Ordinal) ||
                string.Equals(animationType, "TopPush", StringComparison.Ordinal))
            {
                return new SKRect(x, y, x + width, y + revealHeight);
            }

            // FadeOnly / ZoomIn: 不依赖裁剪，返回全量区域。
            return new SKRect(x, y, x + width, y + revealHeight);
        }

        private static double EaseOutCubic(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            double inv = 1.0 - t;
            return 1.0 - (inv * inv * inv);
        }

        private static byte ToSlideBackgroundAlpha(int opacity)
        {
            return (byte)Math.Clamp((int)Math.Round(255 * (100 - Math.Clamp(opacity, 0, 100)) / 100.0), 0, 255);
        }

        private static SKColor GetSlideSolidBackgroundColor(Slide slide)
        {
            byte alpha = ToSlideBackgroundAlpha(slide?.BackgroundOpacity ?? 0);

            if (slide != null && !string.IsNullOrWhiteSpace(slide.BackgroundColor) && SKColor.TryParse(slide.BackgroundColor, out var parsed))
            {
                return new SKColor(parsed.Red, parsed.Green, parsed.Blue, alpha);
            }

            return new SKColor(0, 0, 0, alpha);
        }

        private static SKShader BuildSlideBackgroundGradientShader(Slide slide, int width, int height)
        {
            if (slide == null ||
                string.IsNullOrWhiteSpace(slide.BackgroundGradientStartColor) ||
                string.IsNullOrWhiteSpace(slide.BackgroundGradientEndColor))
            {
                return null;
            }

            if (!SKColor.TryParse(slide.BackgroundGradientStartColor, out var startBase) ||
                !SKColor.TryParse(slide.BackgroundGradientEndColor, out var endBase))
            {
                return null;
            }

            byte alpha = ToSlideBackgroundAlpha(slide.BackgroundOpacity);
            var start = new SKColor(startBase.Red, startBase.Green, startBase.Blue, alpha);
            var end = new SKColor(endBase.Red, endBase.Green, endBase.Blue, alpha);

            var direction = Enum.IsDefined(typeof(DraggableTextBox.BackgroundGradientDirection), slide.BackgroundGradientDirection)
                ? (DraggableTextBox.BackgroundGradientDirection)slide.BackgroundGradientDirection
                : DraggableTextBox.BackgroundGradientDirection.LeftToRight;

            return direction switch
            {
                DraggableTextBox.BackgroundGradientDirection.TopToBottom => SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(0, height),
                    new[] { start, end },
                    new[] { 0f, 1f },
                    SKShaderTileMode.Clamp),
                DraggableTextBox.BackgroundGradientDirection.BottomToTop => SKShader.CreateLinearGradient(
                    new SKPoint(0, height),
                    new SKPoint(0, 0),
                    new[] { start, end },
                    new[] { 0f, 1f },
                    SKShaderTileMode.Clamp),
                DraggableTextBox.BackgroundGradientDirection.RightToLeft => SKShader.CreateLinearGradient(
                    new SKPoint(width, 0),
                    new SKPoint(0, 0),
                    new[] { start, end },
                    new[] { 0f, 1f },
                    SKShaderTileMode.Clamp),
                DraggableTextBox.BackgroundGradientDirection.RadialCenter => SKShader.CreateRadialGradient(
                    new SKPoint(width / 2f, height / 2f),
                    MathF.Min(width, height) * 0.68f,
                    new[] { start, end },
                    new[] { 0f, 1f },
                    SKShaderTileMode.Clamp),
                _ => SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(width, 0),
                    new[] { start, end },
                    new[] { 0f, 1f },
                    SKShaderTileMode.Clamp)
            };
        }

        private static SKColor MultiplyAlpha(SKColor color, float multiplier)
        {
            byte alpha = (byte)Math.Clamp((int)Math.Round(color.Alpha * Math.Clamp(multiplier, 0f, 1f)), 0, 255);
            return new SKColor(color.Red, color.Green, color.Blue, alpha);
        }

        private static SKColor ToSkColor(string hex, int transparencyPercent)
        {
            try
            {
                string value = string.IsNullOrWhiteSpace(hex) ? "#000000" : hex;
                if (SKColor.TryParse(value, out var color))
                {
                    byte alpha = BiblePopupOpacity.ToAlphaFromTransparencyPercent(transparencyPercent);
                    return new SKColor(color.Red, color.Green, color.Blue, alpha);
                }
            }
            catch
            {
            }

            byte fallbackA = BiblePopupOpacity.ToAlphaFromTransparencyPercent(transparencyPercent);
            return new SKColor(0, 0, 0, fallbackA);
        }

        private static List<string> WrapTextByWidth(string text, SKFont font, SKPaint paint, float maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(string.Empty);
                return lines;
            }

            var paragraphs = text.Replace("\r\n", "\n").Split('\n');
            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrEmpty(paragraph))
                {
                    lines.Add(string.Empty);
                    continue;
                }

                var current = string.Empty;
                foreach (char ch in paragraph)
                {
                    string next = current + ch;
                    if (!string.IsNullOrEmpty(current) && font.MeasureText(next, paint) > maxWidth)
                    {
                        lines.Add(current);
                        current = ch.ToString();
                    }
                    else
                    {
                        current = next;
                    }
                }

                if (!string.IsNullOrEmpty(current))
                {
                    lines.Add(current);
                }
            }

            return lines.Count == 0 ? new List<string> { string.Empty } : lines;
        }
        
        /// <summary>
        /// 在SkiaSharp画布上绘制分割线和角标（匹配投影样式：细实线）
        /// </summary>
        private void DrawSplitLinesToCanvas(SKCanvas canvas, Database.Models.Enums.ViewSplitMode mode, double canvasWidth, double canvasHeight)
        {
            // 分割线画笔（使用统一常量，投影屏幕使用细实线）
            var linePaint = new SKPaint
            {
                Color = new SKColor(SPLIT_LINE_COLOR_R, SPLIT_LINE_COLOR_G, SPLIT_LINE_COLOR_B), // 使用统一常量（红色）
                StrokeWidth = (float)SPLIT_LINE_THICKNESS_PROJECTION, // 投影屏幕使用细线
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
                // 投影屏幕使用实线（不使用虚线）
            };
            
            // 角标背景画笔（半透明橙色）
            var labelBgPaint = new SKPaint
            {
                Color = new SKColor(255, 102, 0, 200), // ARGB(200, 255, 102, 0)
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            
            // 角标字体（白色粗体）
            // 使用统一常量，与主屏幕保持一致
            using var labelFont = new SKFont
            {
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                Size = (float)REGION_LABEL_FONT_SIZE,
                Subpixel = true
            };
            
            // 角标文字画笔（白色）
            var labelTextPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };
            
            switch (mode)
            {
                case Database.Models.Enums.ViewSplitMode.Single:
                    // 单画面：不绘制分割线和角标
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Horizontal:
                    // 左右分割：绘制竖线
                    canvas.DrawLine((float)(canvasWidth / 2), 0, (float)(canvasWidth / 2), (float)canvasHeight, linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", (float)(canvasWidth / 2), 0, labelBgPaint, labelFont, labelTextPaint);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Vertical:
                    // 上下分割：绘制横线
                    canvas.DrawLine(0, (float)(canvasHeight / 2), (float)canvasWidth, (float)(canvasHeight / 2), linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", 0, (float)(canvasHeight / 2), labelBgPaint, labelFont, labelTextPaint);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Quad:
                    // 四宫格：绘制十字线
                    canvas.DrawLine((float)(canvasWidth / 2), 0, (float)(canvasWidth / 2), (float)canvasHeight, linePaint);
                    canvas.DrawLine(0, (float)(canvasHeight / 2), (float)canvasWidth, (float)(canvasHeight / 2), linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", (float)(canvasWidth / 2), 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(2)) DrawLabel(canvas, "3", 0, (float)(canvasHeight / 2), labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(3)) DrawLabel(canvas, "4", (float)(canvasWidth / 2), (float)(canvasHeight / 2), labelBgPaint, labelFont, labelTextPaint);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.TripleSplit:
                    // 三分割：左边上下分割，右边整个
                    canvas.DrawLine((float)(canvasWidth / 2), 0, (float)(canvasWidth / 2), (float)canvasHeight, linePaint);
                    canvas.DrawLine(0, (float)(canvasHeight / 2), (float)(canvasWidth / 2), (float)(canvasHeight / 2), linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", 0, (float)(canvasHeight / 2), labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(2)) DrawLabel(canvas, "3", (float)(canvasWidth / 2), 0, labelBgPaint, labelFont, labelTextPaint);
                    break;
            }
            
            linePaint.Dispose();
            labelBgPaint.Dispose();
            labelTextPaint.Dispose();
        }
        
        /// <summary>
        /// 绘制角标（带圆角背景的数字标签）
        /// </summary>
        private void DrawLabel(SKCanvas canvas, string text, float x, float y, SKPaint bgPaint, SKFont font, SKPaint textPaint)
        {
            // 测量文本尺寸
            float textWidth = font.MeasureText(text);
            var fontMetrics = font.Metrics;
            float textHeight = fontMetrics.Descent - fontMetrics.Ascent;
            
            // 使用统一常量，与主屏幕保持一致
            float paddingX = (float)REGION_LABEL_PADDING_X;  // 左右padding
            float paddingY = (float)REGION_LABEL_PADDING_Y;  // 上下padding
            float labelWidth = textWidth + paddingX * 2;
            float labelHeight = textHeight + paddingY * 2;
            
            // 绘制圆角矩形背景（右下圆角）
            var path = new SKPath();
            var rect = new SKRect(x, y, x + labelWidth, y + labelHeight);
            float cornerRadius = (float)REGION_LABEL_CORNER_RADIUS;
            
            // 创建右下圆角的路径
            path.MoveTo(rect.Left, rect.Top);
            path.LineTo(rect.Right, rect.Top);
            path.LineTo(rect.Right, rect.Bottom - cornerRadius);
            path.ArcTo(new SKRect(rect.Right - cornerRadius, rect.Bottom - cornerRadius, rect.Right, rect.Bottom), 0, 90, false);
            path.LineTo(rect.Left, rect.Bottom);
            path.Close();
            
            canvas.DrawPath(path, bgPaint);
            path.Dispose();
            
            // 绘制文本（居中）
            float textX = x + paddingX;
            float textY = y + labelHeight - paddingY - fontMetrics.Descent; // 垂直居中
            canvas.DrawText(text, textX, textY, SKTextAlign.Left, font, textPaint);
        }
        
        /// <summary>
        /// 将文本框绘制到SkiaSharp画布上
        ///  使用 WPF 原生 RenderTargetBitmap 渲染 RichTextBox，确保行间距与主屏幕完全一致
        /// </summary>
        private void DrawTextBoxToCanvas(SKCanvas canvas, DraggableTextBox textBox, double textOffsetX = 0, double? topOverride = null)
        {
            var data = textBox.Data;

            // 获取文本框在Canvas上的实际位置（而不是Data中的值）
            double actualLeft = Canvas.GetLeft(textBox);
            double actualTop = topOverride ?? Canvas.GetTop(textBox);
            double actualWidth = textBox.ActualWidth;
            double actualHeight = textBox.ActualHeight;

            // 处理NaN的情况
            if (double.IsNaN(actualLeft)) actualLeft = data.X;
            if (double.IsNaN(actualTop)) actualTop = data.Y;
            if (actualWidth <= 0) actualWidth = data.Width;
            if (actualHeight <= 0) actualHeight = data.Height;

            //  使用 WPF 原生渲染 RichTextBox（保证行间距与主屏幕完全一致）
            try
            {
                int width = (int)Math.Ceiling(actualWidth);
                int height = (int)Math.Ceiling(actualHeight);

                if (width > 0 && height > 0)
                {
//#if DEBUG
//                    //System.Diagnostics.Debug.WriteLine($"[投影渲染参数-WPF] 文本框ID={data.Id}");
//                    //System.Diagnostics.Debug.WriteLine($"  字体大小: {data.FontSize}");
//                    //System.Diagnostics.Debug.WriteLine($"  行间距: {data.LineSpacing}");
//                    //System.Diagnostics.Debug.WriteLine($"  文本框尺寸: {width}×{height}");
//                    //System.Diagnostics.Debug.WriteLine($"  实际位置: ({actualLeft}, {actualTop})");
//                    //System.Diagnostics.Debug.WriteLine($"  缩放比例: {_currentCanvasScaleX:F2}×{_currentCanvasScaleY:F2}");
//#endif

                    //  使用 WPF 原生方法渲染 RichTextBox，传入缩放比例以获得高清效果
                    var wpfBitmap = textBox.GetRenderedBitmap(_currentCanvasScaleX, _currentCanvasScaleY, textOffsetX);

                    if (wpfBitmap != null)
                    {
                        // 转换 WPF BitmapSource 到 SkiaSharp SKBitmap
                        var skBitmap = ConvertBitmapSourceToSKBitmap(wpfBitmap);

                        if (skBitmap != null)
                        {
                            // 绘制到Canvas（使用原始位置和尺寸，canvas.Scale 会自动处理缩放）
                            var destRect = new SKRect(
                                (float)actualLeft,
                                (float)actualTop,
                                (float)(actualLeft + actualWidth),
                                (float)(actualTop + actualHeight));

                            // 使用高质量过滤模式，确保投影质量
                            // 使用 DrawImage 替代 DrawBitmap，支持 SKSamplingOptions
                            using var image = SKImage.FromBitmap(skBitmap);
                            var paint = new SKPaint
                            {
                                IsAntialias = true
                            };
                            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                            // 使用 DrawImage(SKImage, SKRect, SKSamplingOptions, SKPaint) 重载
                            canvas.DrawImage(image, destRect, sampling, paint);
                            paint.Dispose();
                            skBitmap.Dispose();

//#if DEBUG
//                            //System.Diagnostics.Debug.WriteLine($" [文本绘制-WPF] 位置: ({actualLeft}, {actualTop}), 尺寸: {width}×{height}");
//#endif
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [文本绘制-WPF] 失败: {ex.Message}");
                //#else
                _ = ex;
                //#endif
            }
        }
        
        /// <summary>
        /// 直接用SkiaSharp绘制文字（支持加粗、对齐等样式）
        /// </summary>
        private void DrawTextDirectly(SKCanvas canvas, TextElement data, float x, float y, float width, float height)
        {
            // 解析颜色
            SKColor textColor = SKColors.White;
            try
            {
                string hexColor = data.FontColor.TrimStart('#');
                if (hexColor.Length == 6)
                {
                    byte r = Convert.ToByte(hexColor.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hexColor.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hexColor.Substring(4, 2), 16);
                    textColor = new SKColor(r, g, b);
                }
            }
            catch { }
            
            // 创建字体（支持PAK资源、文件路径和系统字体）
            SKTypeface typeface = null;
            try
            {
                // 字体路径格式：./CCanvas_Fonts/江西拙楷.ttf#江西拙楷
                string fontPath = data.FontFamily;
                
                // 如果是文件路径格式（包含#号分隔符），提取文件路径部分
                if (fontPath.Contains("#"))
                {
                    fontPath = fontPath.Split('#')[0];
                }
                
                // 检查是否是相对路径（从PAK加载）
                if (fontPath.StartsWith("./") || fontPath.StartsWith(".\\"))
                {
                    // 从PAK资源包加载字体
                    // 提取文件名（例如从 ./CCanvas_Fonts/江西拙楷.ttf 提取 江西拙楷.ttf）
                    string fileName = System.IO.Path.GetFileName(fontPath);
                    
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"  [字体] 尝试从PAK加载: 原始路径='{fontPath}', 文件名='{fileName}'");
                    #endif
                    
                    // 在PAK中搜索匹配的字体文件
                    string actualPakPath = null;
                    var allResources = _pakManager.GetAllResourcePaths();
                    foreach (var resourcePath in allResources)
                    {
                        if (System.IO.Path.GetFileName(resourcePath) == fileName && 
                            resourcePath.StartsWith("Fonts/"))
                        {
                            actualPakPath = resourcePath;
                            break;
                        }
                    }
                    
                    if (actualPakPath != null)
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [字体] 在PAK中找到: {actualPakPath}");
                        #endif
                        
                        var fontData = _pakManager.GetResource(actualPakPath);
                        
                        if (fontData != null)
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [字体] PAK数据获取成功: {fontData.Length} bytes");
                            #endif
                            
                            typeface = SKTypeface.FromData(SKData.CreateCopy(fontData));
                            
                            if (typeface != null)
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"  [字体] 从PAK加载成功: {actualPakPath}");
                                #endif
                            }
                            else
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"  [字体] SKTypeface创建失败，数据可能不是有效字体");
                                #endif
                            }
                        }
                    }
                    else
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [字体] PAK中未找到字体，尝试文件系统");
                        #endif
                        
                        // PAK中没有，尝试从文件系统加载
                        if (System.IO.File.Exists(fontPath))
                        {
                            typeface = SKTypeface.FromFile(fontPath);
                            
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [字体] 从文件加载: {fontPath}");
                            #endif
                        }
                        else
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [字体] 文件也不存在: {fontPath}");
                            #endif
                        }
                    }
                }
                else if (System.IO.Path.IsPathRooted(fontPath))
                {
                    // 绝对路径，从文件加载
                    if (System.IO.File.Exists(fontPath))
                    {
                        typeface = SKTypeface.FromFile(fontPath);
                        
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [字体] 从文件加载: {fontPath}");
                        #endif
                    }
                }
                
                // 如果字体加载失败，使用系统字体
                if (typeface == null)
                {
                    var fontStyle = data.IsBoldBool ? SKFontStyle.Bold : SKFontStyle.Normal;
                    typeface = SKTypeface.FromFamilyName(fontPath, fontStyle);
                    
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"  [字体] 使用系统字体: {fontPath}");
                    #endif
                }
            }
            catch (Exception)
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"  [字体] 加载失败: {ex.Message}，使用默认字体");
                #endif
                // 加载失败，使用默认字体
                var fontStyle = data.IsBoldBool ? SKFontStyle.Bold : SKFontStyle.Normal;
                typeface = SKTypeface.FromFamilyName("Arial", fontStyle);
            }
            
            // 创建字体
            using var font = new SKFont
            {
                Typeface = typeface,
                Size = (float)data.FontSize,
                Subpixel = true
            };
            
            // 创建画笔
            using var paint = new SKPaint
            {
                Color = textColor,
                IsAntialias = true
            };
            
            // 处理文本对齐
            SKTextAlign textAlign = data.TextAlign switch
            {
                "Center" => SKTextAlign.Center,
                "Right" => SKTextAlign.Right,
                _ => SKTextAlign.Left
            };
            
            // 计算文本位置
            float textX = x;
            if (data.TextAlign == "Center")
            {
                textX = x + width / 2;
            }
            else if (data.TextAlign == "Right")
            {
                textX = x + width;
            }
            
            // 绘制文本（支持多行）
            string[] lines = data.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            float lineSpacingFactor = data.LineSpacing > 0 ? (float)data.LineSpacing : 1.2f;
            float lineHeight = font.Size * lineSpacingFactor;
            float totalTextHeight = lines.Length * lineHeight;

            float textTopY = y;
            string verticalAlign = (data.TextVerticalAlign ?? "Top").Trim();
            if (string.Equals(verticalAlign, "Middle", StringComparison.OrdinalIgnoreCase))
            {
                textTopY = y + Math.Max(0, (height - totalTextHeight) / 2f);
            }
            else if (string.Equals(verticalAlign, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                textTopY = y + Math.Max(0, height - totalTextHeight);
            }
            
            // 正确计算第一行基线位置
            // 使用 FontMetrics 获取字体度量信息
            var fontMetrics = font.Metrics;
            float firstLineBaseline = textTopY - fontMetrics.Ascent; // Ascent是负值，表示基线到顶部的距离
            float currentY = firstLineBaseline;
            
            #if DEBUG
            //System.Diagnostics.Debug.WriteLine($"  [文本绘制] 位置: ({textX}, {currentY}), 字号: {font.Size}, 行高: {lineHeight}, 对齐: {textAlign}");
            //System.Diagnostics.Debug.WriteLine($"  [文本绘制] 区域: x={x}, y={y}, w={width}, h={height}");
            //System.Diagnostics.Debug.WriteLine($"  [字体度量] Ascent: {fontMetrics.Ascent}, Descent: {fontMetrics.Descent}, Leading: {fontMetrics.Leading}");
            #endif
            
            foreach (string line in lines)
            {
                canvas.DrawText(line, textX, currentY, textAlign, font, paint);
                currentY += lineHeight;
            }
            
            typeface.Dispose();
        }
        
        #endregion

    }
}



