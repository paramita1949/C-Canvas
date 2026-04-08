using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using ImageColorChanger.Managers;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursor = System.Windows.Input.Cursor;
using WpfCursors = System.Windows.Input.Cursors;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ImageColorChanger.UI
{
    internal enum LiveCaptionDockMode
    {
        TopBand = 0,
        BottomBand = 1,
        Floating = 2
    }

    internal enum LiveCaptionWorkAreaMode
    {
        WindowOnly = 0,
        ReserveWithAppBar = 1
    }

    internal sealed class LiveCaptionOverlayWindow : Window
    {
        private const uint ABM_NEW = 0x00000000;
        private const uint ABM_REMOVE = 0x00000001;
        private const uint ABM_QUERYPOS = 0x00000002;
        private const uint ABM_SETPOS = 0x00000003;
        private const uint ABE_TOP = 1;
        private const uint ABE_BOTTOM = 3;
        private const double FloatingResizeHitThickness = 10;
        private const int VisibleCaptionLines = 2;
        private const double DefaultActionButtonHeight = 28;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        private enum ResizeAnchor
        {
            None = 0,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        private readonly Border _captionSurface;
        private readonly TextBlock _captionText;
        private readonly TextBlock _projectionActionText;
        private readonly TextBlock _ndiActionText;
        private readonly TextBlock _ndiStyleActionText;
        private readonly TextBlock _localStyleActionText;
        private readonly Path _settingsIcon;
        private readonly Border _projectionAction;
        private readonly Border _ndiAction;
        private readonly Border _ndiStyleAction;
        private readonly Border _localStyleAction;
        private readonly Border _orientationAction;
        private readonly Border _positionAction;
        private readonly StackPanel _actionPanel;
        private readonly TextBlock _orientationActionText;
        private readonly TextBlock _positionActionText;
        private readonly Border _styleAction;
        private readonly Border _settingsAction;
        private readonly Border _closeAction;
        private readonly DispatcherTimer _typingTimer;
        private readonly DispatcherTimer _floatingBoundsChangedTimer;
        private string _typingTarget = string.Empty;
        private string _currentCaptionRawText = string.Empty;
        private int _typingIndex;
        private int _highlightStartIndex;
        private bool _typingAnimationEnabled;
        private bool _latestTextHighlightEnabled = true;
        private System.Windows.Media.Brush _baseTextBrush = WpfBrushes.White;
        private System.Windows.Media.Brush _latestTextHighlightBrush = new WpfSolidColorBrush(WpfColor.FromRgb(255, 255, 0));
        private double _captionLetterSpacing;
        private double _captionParagraphGap = 4;
        private ProjectionCaptionOrientation _captionOrientation = ProjectionCaptionOrientation.Horizontal;
        private ProjectionCaptionHorizontalAnchor _captionHorizontalAnchor = ProjectionCaptionHorizontalAnchor.Center;
        private ProjectionCaptionVerticalAnchor _captionVerticalAnchor = ProjectionCaptionVerticalAnchor.Top;

        private LiveCaptionDockMode _dockMode = LiveCaptionDockMode.TopBand;
        private double _bandHeight = 180;
        private double _floatingWidth = 1320;
        private double _floatingHeight = 180;
        private double _floatingLeft;
        private double _floatingTop;
        private bool _hasCustomFloatingBounds;
        private double _autoMinCaptionHeight = 96;

        private IntPtr _handle = IntPtr.Zero;
        private bool _appBarRegistered;
        private bool _layoutApplyScheduled;
        private LiveCaptionWorkAreaMode _workAreaMode = LiveCaptionWorkAreaMode.WindowOnly;
        private bool _isResizingFromEdge;
        private ResizeAnchor _activeResizeAnchor = ResizeAnchor.None;
        private System.Windows.Point _resizeStartScreenPoint;
        private Rect _resizeStartBounds;
        private bool _suppressFloatingBoundsTracking = true;
        private int _layoutApplySequence;

        public event Action SettingsRequested;
        public event Action ProjectionToggleRequested;
        public event Action NdiToggleRequested;
        public event Action NdiStyleRequested;
        public event Action LocalStyleRequested;
        public event Action<ProjectionCaptionOrientation> CaptionOrientationRequested;
        public event Action<ProjectionCaptionHorizontalAnchor, ProjectionCaptionVerticalAnchor> CaptionPositionRequested;
        public event Action CaptionStyleRequested;
        public event Action CloseRequested;
        public event Action<Rect> FloatingBoundsChanged;

        public LiveCaptionOverlayWindow()
        {
            Width = _floatingWidth;
            Height = _floatingHeight;
            MinWidth = 640;
            MinHeight = 96;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            AllowsTransparency = true;
            Background = WpfBrushes.Transparent;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            var root = new Grid();

            _captionSurface = new Border
            {
                Background = new WpfSolidColorBrush(WpfColor.FromArgb(206, 0, 0, 0)),
                BorderBrush = new WpfSolidColorBrush(WpfColor.FromArgb(180, 33, 150, 243)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(8),
                Padding = new Thickness(24, 14, 20, 14)
            };
            _captionSurface.SetResourceReference(Border.BorderBrushProperty, "BrushGlobalIcon");
            _captionSurface.MouseLeftButtonDown += CaptionSurface_MouseLeftButtonDown;
            _typingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _typingTimer.Tick += TypingTimer_Tick;
            _floatingBoundsChangedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(320)
            };
            _floatingBoundsChangedTimer.Tick += FloatingBoundsChangedTimer_Tick;

            var captionGrid = new Grid();
            captionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            captionGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _captionText = new TextBlock
            {
                Foreground = WpfBrushes.White,
                FontSize = 29,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.NoWrap,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = 46,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 2),
                Text = string.Empty
            };
            TextOptions.SetTextFormattingMode(_captionText, TextFormattingMode.Display);
            TextOptions.SetTextHintingMode(_captionText, TextHintingMode.Fixed);
            Grid.SetColumn(_captionText, 0);
            Grid.SetColumnSpan(_captionText, 2);
            Grid.SetRow(_captionText, 1);

            _actionPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 4, 0)
            };

            _projectionAction = CreateTextActionItem("投影", out _projectionActionText);
            _projectionAction.Margin = new Thickness(6, 0, 0, 0);
            _projectionAction.ToolTip = "显示/隐藏投影字幕";
            _projectionAction.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                ProjectionToggleRequested?.Invoke();
            };
            SetProjectionToggleState(hidden: false);

            _ndiAction = CreateTextActionItem("NDI", out _ndiActionText);
            _ndiAction.Margin = new Thickness(6, 0, 0, 0);
            _ndiAction.ToolTip = "NDI转发（点击开启）";
            _ndiAction.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                NdiToggleRequested?.Invoke();
            };
            SetNdiToggleState(enabled: false);

            _ndiStyleAction = CreateTextActionItem("NDI样式", out _ndiStyleActionText);
            _ndiStyleAction.Margin = new Thickness(6, 0, 0, 0);
            _ndiStyleAction.ToolTip = "NDI字幕样式设置";
            _ndiStyleAction.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                NdiStyleRequested?.Invoke();
            };

            _localStyleAction = CreateTextActionItem("本机样式", out _localStyleActionText);
            _localStyleAction.Margin = new Thickness(6, 0, 0, 0);
            _localStyleAction.ToolTip = "本机字幕样式设置";
            _localStyleAction.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                LocalStyleRequested?.Invoke();
            };

            _orientationAction = CreateTextActionItem("横着", out _orientationActionText);
            _orientationAction.Margin = new Thickness(6, 0, 0, 0);
            _orientationAction.ToolTip = "字幕方向";
            _orientationAction.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                ToggleCaptionOrientation();
            };
            SetCaptionOrientationState(_captionOrientation);

            _positionAction = CreateTextActionItem("位置:中", out _positionActionText);
            _positionAction.Margin = new Thickness(6, 0, 0, 0);
            _positionAction.ToolTip = "字幕位置";
            _positionAction.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                OpenCaptionPositionMenu();
            };
            SetCaptionPositionState(_captionHorizontalAnchor, _captionVerticalAnchor);

            _styleAction = CreateTextActionItem("投影样式", out _);
            _styleAction.Margin = new Thickness(6, 0, 0, 0);
            _styleAction.ToolTip = "投影字幕样式";
            _styleAction.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                CaptionStyleRequested?.Invoke();
            };

            _settingsAction = CreateActionItem("IconLucideSettings", out _settingsIcon);
            _settingsAction.Margin = new Thickness(6, 0, 0, 0);
            _settingsAction.ToolTip = "字幕设置";
            _settingsAction.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                SettingsRequested?.Invoke();
            };

            _closeAction = CreateActionItem("IconLucideX", out _, dangerAccent: true);
            _closeAction.Margin = new Thickness(6, 0, 0, 0);
            _closeAction.ToolTip = "关闭字幕";
            _closeAction.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                CloseRequested?.Invoke();
            };

            _actionPanel.Children.Add(_projectionAction);
            _actionPanel.Children.Add(_ndiAction);
            _actionPanel.Children.Add(_ndiStyleAction);
            _actionPanel.Children.Add(_localStyleAction);
            _actionPanel.Children.Add(_styleAction);
            _actionPanel.Children.Add(_orientationAction);
            _actionPanel.Children.Add(_positionAction);
            _actionPanel.Children.Add(_settingsAction);
            _actionPanel.Children.Add(_closeAction);
            Grid.SetColumn(_actionPanel, 1);
            Grid.SetRow(_actionPanel, 0);

            var shellGrid = new Grid();
            shellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            shellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var accentBar = new Border
            {
                Opacity = 0.95,
                CornerRadius = new CornerRadius(6, 6, 0, 0)
            };
            accentBar.SetResourceReference(Border.BackgroundProperty, "BrushGlobalIcon");
            Grid.SetRow(accentBar, 0);

            captionGrid.Children.Add(_captionText);
            captionGrid.Children.Add(_actionPanel);
            Grid.SetRow(captionGrid, 1);

            shellGrid.Children.Add(accentBar);
            shellGrid.Children.Add(captionGrid);
            _captionSurface.Child = shellGrid;

            root.Children.Add(_captionSurface);
            Content = root;
            PreviewMouseLeftButtonDown += Overlay_PreviewMouseLeftButtonDown;
            PreviewMouseMove += Overlay_PreviewMouseMove;
            PreviewMouseLeftButtonUp += Overlay_PreviewMouseLeftButtonUp;
            LostMouseCapture += Overlay_LostMouseCapture;

            Loaded += (_, _) =>
            {
                if (!_hasCustomFloatingBounds)
                {
                    var wa = SystemParameters.WorkArea;
                    _floatingLeft = wa.Left + (wa.Width - _floatingWidth) / 2;
                    _floatingTop = wa.Top + 24;
                }

                ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                    $"OverlayLoaded: dock={_dockMode}, hasCustom={_hasCustomFloatingBounds}, cachedFloating=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##}), actual=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##})");
                StartSettingsIconAnimation();
                ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                    $"OverlayLoaded: projectionAction visible={_projectionAction.IsVisible}, size={_projectionAction.ActualWidth}x{_projectionAction.ActualHeight}; settingsAction visible={_settingsAction.IsVisible}, size={_settingsAction.ActualWidth}x{_settingsAction.ActualHeight}; closeAction visible={_closeAction.IsVisible}, size={_closeAction.ActualWidth}x{_closeAction.ActualHeight}");
            };

            SourceInitialized += (_, _) =>
            {
                _handle = new WindowInteropHelper(this).Handle;
                ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                    $"OverlaySourceInitialized: handle=0x{_handle.ToInt64():X}, visible={IsVisible}, dock={_dockMode}, cachedFloating=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##}), suppress={_suppressFloatingBoundsTracking}");
                if (IsVisible)
                {
                    ScheduleApplyLayout();
                }
            };

            IsVisibleChanged += (_, _) =>
            {
                ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                    $"OverlayVisibleChanged: visible={IsVisible}, dock={_dockMode}, hasCustom={_hasCustomFloatingBounds}, cachedFloating=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##}), actual=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##}), suppress={_suppressFloatingBoundsTracking}");
                if (IsVisible)
                {
                    if (_dockMode == LiveCaptionDockMode.Floating)
                    {
                        // 打开窗口阶段先屏蔽位置追踪，避免 Show 的初始系统定位覆盖持久化坐标。
                        _suppressFloatingBoundsTracking = true;
                    }

                    ScheduleApplyLayout();
                    if (_workAreaMode == LiveCaptionWorkAreaMode.ReserveWithAppBar)
                    {
                        ScheduleSettleLayoutRefresh();
                    }
                }
                else
                {
                    RemoveAppBar(force: true);
                }
            };

            Closed += (_, _) => RemoveAppBar(force: true);

            LocationChanged += (_, _) =>
            {
                if (_dockMode == LiveCaptionDockMode.Floating)
                {
                    if (_suppressFloatingBoundsTracking)
                    {
                        ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                            $"FloatingBounds: location ignored during programmatic-layout actual=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##}), cached=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##})");
                        return;
                    }

                    _hasCustomFloatingBounds = true;
                    _floatingLeft = Left;
                    _floatingTop = Top;
                    ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                        $"FloatingBounds: location tracked actual=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##}), cached=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##})");
                    ScheduleFloatingBoundsChanged();
                }
            };

            SizeChanged += (_, _) =>
            {
                if (_dockMode == LiveCaptionDockMode.Floating)
                {
                    if (_suppressFloatingBoundsTracking)
                    {
                        ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                            $"FloatingBounds: size ignored during programmatic-layout actual=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##}), cached=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##})");
                        return;
                    }

                    _hasCustomFloatingBounds = true;
                    _floatingWidth = Math.Max(MinWidth, Width);
                    _floatingHeight = Math.Max(MinHeight, Height);
                    ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                        $"FloatingBounds: size tracked actual=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##}), cached=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##})");
                    ScheduleFloatingBoundsChanged();
                }
                else
                {
                    _bandHeight = Math.Max(MinHeight, Height);
                }
            };

            EnsureAutoCaptionHeight();
        }

        public UIElement GetSettingsAnchorElement() => _settingsAction;
        public UIElement GetStyleAnchorElement() => _styleAction;
        public UIElement GetLocalStyleAnchorElement() => _localStyleAction;
        public UIElement GetNdiStyleAnchorElement() => _ndiStyleAction;

        public bool IsTypingAnimationEnabled => _typingAnimationEnabled;

        public void SetCaptionOrientationState(ProjectionCaptionOrientation orientation)
        {
            _captionOrientation = orientation == ProjectionCaptionOrientation.Vertical
                ? ProjectionCaptionOrientation.Vertical
                : ProjectionCaptionOrientation.Horizontal;
            UpdateCaptionOrientationActionState();
        }

        public void SetCaptionPositionState(ProjectionCaptionHorizontalAnchor horizontalAnchor, ProjectionCaptionVerticalAnchor verticalAnchor)
        {
            _captionHorizontalAnchor = NormalizeCaptionHorizontalAnchor(horizontalAnchor);
            _captionVerticalAnchor = NormalizeCaptionVerticalAnchor(verticalAnchor);
            UpdateCaptionPositionActionState();
        }

        public void SetTypingAnimationEnabled(bool enabled)
        {
            _typingAnimationEnabled = enabled;
            if (!enabled)
            {
                _typingTimer.Stop();
                if (!string.IsNullOrEmpty(_typingTarget))
                {
                    RenderCaption(_typingTarget, _highlightStartIndex);
                }
            }
        }

        public bool IsLatestTextHighlightEnabled => _latestTextHighlightEnabled;

        public void SetLatestTextHighlightEnabled(bool enabled)
        {
            _latestTextHighlightEnabled = enabled;
            RenderCaption(_currentCaptionRawText, _highlightStartIndex);
        }

        public void SetCaptionTypography(string fontFamily, double fontSize, double margin, double lineHeight)
        {
            if (_captionSurface == null || _captionText == null)
            {
                return;
            }

            string nextFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Microsoft YaHei UI" : fontFamily.Trim();
            double nextSize = Math.Clamp(fontSize, 16, 88);
            double nextMargin = Math.Clamp(margin, 6, 120);
            double nextLineHeight = Math.Max(nextSize, lineHeight);

            _captionText.FontFamily = new System.Windows.Media.FontFamily(nextFamily);
            _captionText.FontSize = nextSize;
            _captionParagraphGap = Math.Clamp(nextLineHeight - nextSize, 0, 60);
            _captionText.LineHeight = nextSize + _captionParagraphGap;
            _captionSurface.Padding = new Thickness(nextMargin);
            EnsureAutoCaptionHeight();
        }

        public void SetCaptionLetterSpacing(double spacing)
        {
            _captionLetterSpacing = Math.Clamp(spacing, 0, 10);
            RenderCaption(_currentCaptionRawText, _highlightStartIndex);
        }

        private void EnsureAutoCaptionHeight()
        {
            _autoMinCaptionHeight = ComputeCaptionAutoMinHeight();
            MinHeight = Math.Max(96, _autoMinCaptionHeight);

            if (_dockMode == LiveCaptionDockMode.Floating)
            {
                if (_floatingHeight < _autoMinCaptionHeight || Height < _autoMinCaptionHeight)
                {
                    _suppressFloatingBoundsTracking = true;
                    try
                    {
                        _floatingHeight = Math.Max(_autoMinCaptionHeight, _floatingHeight);
                        if (Height < _autoMinCaptionHeight)
                        {
                            Height = _autoMinCaptionHeight;
                        }
                    }
                    finally
                    {
                        _suppressFloatingBoundsTracking = false;
                    }

                    if (IsVisible)
                    {
                        ScheduleApplyLayout();
                    }
                }
            }
            else
            {
                if (_bandHeight < _autoMinCaptionHeight || Height < _autoMinCaptionHeight)
                {
                    _bandHeight = Math.Max(_autoMinCaptionHeight, _bandHeight);
                    if (Height < _autoMinCaptionHeight)
                    {
                        Height = _autoMinCaptionHeight;
                    }

                    if (IsVisible)
                    {
                        ScheduleApplyLayout();
                    }
                }
            }

            ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                $"CaptionAutoHeight: min={_autoMinCaptionHeight:0.##}, lineHeight={_captionText.LineHeight:0.##}, fontSize={_captionText.FontSize:0.##}, padding={_captionSurface.Padding.Top:0.##}/{_captionSurface.Padding.Bottom:0.##}, mode={_dockMode}, floating={_floatingHeight:0.##}, band={_bandHeight:0.##}");
        }

        private double ComputeCaptionAutoMinHeight()
        {
            double lineHeight = _captionText.LineHeight > 0
                ? _captionText.LineHeight
                : _captionText.FontSize * 1.5;

            double textAreaNeed = (lineHeight * VisibleCaptionLines)
                + _captionText.Margin.Top
                + _captionText.Margin.Bottom;

            double actionNeed = Math.Max(
                Math.Max(GetActionHeight(_projectionAction), GetActionHeight(_orientationAction)),
                Math.Max(GetActionHeight(_positionAction), Math.Max(GetActionHeight(_styleAction), Math.Max(GetActionHeight(_settingsAction), GetActionHeight(_closeAction)))));

            double bodyNeed = Math.Max(textAreaNeed, actionNeed);

            double shellNeed = 2 + bodyNeed;
            double surfaceNeed =
                _captionSurface.Margin.Top + _captionSurface.Margin.Bottom +
                _captionSurface.Padding.Top + _captionSurface.Padding.Bottom +
                _captionSurface.BorderThickness.Top + _captionSurface.BorderThickness.Bottom;

            double target = Math.Ceiling(shellNeed + surfaceNeed + 8);
            return Math.Clamp(target, 96, SystemParameters.WorkArea.Height * 0.85);
        }

        private static double GetActionHeight(FrameworkElement element)
        {
            if (element == null)
            {
                return DefaultActionButtonHeight;
            }

            if (element.ActualHeight > 0)
            {
                return element.ActualHeight;
            }

            if (!double.IsNaN(element.Height) && element.Height > 0)
            {
                return element.Height;
            }

            return DefaultActionButtonHeight;
        }

        public void SetLatestTextHighlightColor(string hexColor)
        {
            if (TryParseColor(hexColor, out WpfColor color))
            {
                _latestTextHighlightBrush = new WpfSolidColorBrush(color);
            }
            else
            {
                _latestTextHighlightBrush = new WpfSolidColorBrush(WpfColor.FromRgb(255, 255, 0));
            }

            RenderCaption(_currentCaptionRawText, _highlightStartIndex);
        }

        public void SetCaptionTextColor(string hexColor)
        {
            if (TryParseColor(hexColor, out WpfColor color))
            {
                _baseTextBrush = new WpfSolidColorBrush(color);
            }
            else
            {
                _baseTextBrush = WpfBrushes.White;
            }

            RenderCaption(_currentCaptionRawText, _highlightStartIndex);
        }

        private void CaptionSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_dockMode != LiveCaptionDockMode.Floating || e.ClickCount > 1)
            {
                return;
            }

            if (IsPointerOnActionButtons(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (_isResizingFromEdge)
            {
                return;
            }

            if (HitTestResizeAnchor(e.GetPosition(this)) != ResizeAnchor.None)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
                // ignored
            }
        }

        private bool IsPointerOnActionButtons(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, _projectionAction) || ReferenceEquals(current, _orientationAction) || ReferenceEquals(current, _positionAction) || ReferenceEquals(current, _styleAction) || ReferenceEquals(current, _settingsAction) || ReferenceEquals(current, _closeAction))
                {
                    return true;
                }

                current = GetHitTestParent(current);
            }

            return false;
        }

        private static DependencyObject GetHitTestParent(DependencyObject current)
        {
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(current);
            }

            if (current is FrameworkContentElement fce)
            {
                return fce.Parent ?? ContentOperations.GetParent(fce);
            }

            if (current is ContentElement ce)
            {
                return ContentOperations.GetParent(ce);
            }

            return null;
        }

        private void Overlay_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_dockMode != LiveCaptionDockMode.Floating || e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            ResizeAnchor anchor = HitTestResizeAnchor(e.GetPosition(this));
            if (anchor == ResizeAnchor.None)
            {
                return;
            }

            _isResizingFromEdge = true;
            _activeResizeAnchor = anchor;
            _resizeStartBounds = new Rect(Left, Top, Width, Height);
            _resizeStartScreenPoint = PointToScreen(e.GetPosition(this));
            CaptureMouse();
            Cursor = GetCursorForAnchor(anchor);
            e.Handled = true;
        }

        private void Overlay_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dockMode != LiveCaptionDockMode.Floating)
            {
                if (!_isResizingFromEdge)
                {
                    Cursor = null;
                }

                return;
            }

            if (_isResizingFromEdge)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    EndResizeFromEdge();
                    return;
                }

                System.Windows.Point current = PointToScreen(e.GetPosition(this));
                double scale = GetWindowScale();
                if (scale <= 0)
                {
                    scale = 1;
                }

                double dx = (current.X - _resizeStartScreenPoint.X) / scale;
                double dy = (current.Y - _resizeStartScreenPoint.Y) / scale;
                ApplyResizeFromAnchor(_activeResizeAnchor, dx, dy, _resizeStartBounds);
                e.Handled = true;
                return;
            }

            ResizeAnchor hoverAnchor = HitTestResizeAnchor(e.GetPosition(this));
            Cursor = GetCursorForAnchor(hoverAnchor);
        }

        private void Overlay_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                EndResizeFromEdge();
            }
        }

        private void Overlay_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            EndResizeFromEdge();
        }

        private void EndResizeFromEdge()
        {
            if (!_isResizingFromEdge)
            {
                return;
            }

            _isResizingFromEdge = false;
            _activeResizeAnchor = ResizeAnchor.None;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            Cursor = null;
        }

        private ResizeAnchor HitTestResizeAnchor(System.Windows.Point point)
        {
            if (_dockMode != LiveCaptionDockMode.Floating)
            {
                return ResizeAnchor.None;
            }

            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;
            if (width <= 0 || height <= 0)
            {
                return ResizeAnchor.None;
            }

            bool left = point.X <= FloatingResizeHitThickness;
            bool right = point.X >= width - FloatingResizeHitThickness;
            bool top = point.Y <= FloatingResizeHitThickness;
            bool bottom = point.Y >= height - FloatingResizeHitThickness;

            if (left && top) return ResizeAnchor.TopLeft;
            if (right && top) return ResizeAnchor.TopRight;
            if (left && bottom) return ResizeAnchor.BottomLeft;
            if (right && bottom) return ResizeAnchor.BottomRight;
            if (left) return ResizeAnchor.Left;
            if (right) return ResizeAnchor.Right;
            if (top) return ResizeAnchor.Top;
            if (bottom) return ResizeAnchor.Bottom;

            return ResizeAnchor.None;
        }

        private static WpfCursor GetCursorForAnchor(ResizeAnchor anchor)
        {
            return anchor switch
            {
                ResizeAnchor.Left => WpfCursors.SizeWE,
                ResizeAnchor.Right => WpfCursors.SizeWE,
                ResizeAnchor.Top => WpfCursors.SizeNS,
                ResizeAnchor.Bottom => WpfCursors.SizeNS,
                ResizeAnchor.TopLeft => WpfCursors.SizeNWSE,
                ResizeAnchor.BottomRight => WpfCursors.SizeNWSE,
                ResizeAnchor.TopRight => WpfCursors.SizeNESW,
                ResizeAnchor.BottomLeft => WpfCursors.SizeNESW,
                _ => null
            };
        }

        private void ApplyResizeFromAnchor(ResizeAnchor anchor, double dx, double dy, Rect startBounds)
        {
            if (_dockMode != LiveCaptionDockMode.Floating)
            {
                return;
            }

            double newLeft = startBounds.Left;
            double newTop = startBounds.Top;
            double newWidth = startBounds.Width;
            double newHeight = startBounds.Height;

            if (anchor == ResizeAnchor.Left || anchor == ResizeAnchor.TopLeft || anchor == ResizeAnchor.BottomLeft)
            {
                newWidth = Math.Max(MinWidth, startBounds.Width - dx);
                newLeft = startBounds.Left + (startBounds.Width - newWidth);
            }

            if (anchor == ResizeAnchor.Right || anchor == ResizeAnchor.TopRight || anchor == ResizeAnchor.BottomRight)
            {
                newWidth = Math.Max(MinWidth, startBounds.Width + dx);
            }

            if (anchor == ResizeAnchor.Top || anchor == ResizeAnchor.TopLeft || anchor == ResizeAnchor.TopRight)
            {
                newHeight = Math.Max(MinHeight, startBounds.Height - dy);
                newTop = startBounds.Top + (startBounds.Height - newHeight);
            }

            if (anchor == ResizeAnchor.Bottom || anchor == ResizeAnchor.BottomLeft || anchor == ResizeAnchor.BottomRight)
            {
                newHeight = Math.Max(MinHeight, startBounds.Height + dy);
            }

            Width = newWidth;
            Height = newHeight;
            Left = newLeft;
            Top = newTop;

            _floatingWidth = Width;
            _floatingHeight = Height;
            _floatingLeft = Left;
            _floatingTop = Top;
        }

        private Border CreateActionItem(string iconKey, out Path iconPath, bool dangerAccent = false)
        {
            var host = new Border
            {
                Width = 28,
                Height = 28,
                Background = new WpfSolidColorBrush(WpfColor.FromRgb(245, 243, 238)),
                BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(33, 150, 243)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Cursor = WpfCursors.Hand
            };
            host.SetResourceReference(Border.BackgroundProperty, "BrushMenuSubSurface");
            host.SetResourceReference(Border.BorderBrushProperty, dangerAccent ? "BrushBrandDanger" : "BrushMenuBorder");

            iconPath = CreateIcon(iconKey);
            iconPath.SetResourceReference(Path.StrokeProperty, dangerAccent ? "BrushBrandDanger" : "BrushMenuText");
            host.Child = iconPath;
            return host;
        }

        private Border CreateTextActionItem(string text, out TextBlock textBlock)
        {
            var host = new Border
            {
                MinWidth = 52,
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Background = new WpfSolidColorBrush(WpfColor.FromRgb(245, 243, 238)),
                BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(33, 150, 243)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Cursor = WpfCursors.Hand
            };
            host.SetResourceReference(Border.BackgroundProperty, "BrushMenuSubSurface");
            host.SetResourceReference(Border.BorderBrushProperty, "BrushMenuBorder");

            textBlock = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "BrushMenuText");
            host.Child = textBlock;
            return host;
        }

        public void SetProjectionToggleState(bool hidden)
        {
            if (_projectionActionText == null || _projectionAction == null)
            {
                return;
            }

            if (hidden)
            {
                _projectionActionText.Text = "显示投影";
                _projectionAction.Background = new WpfSolidColorBrush(WpfColor.FromRgb(220, 38, 38));
                _projectionAction.BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(185, 28, 28));
                _projectionActionText.Foreground = WpfBrushes.White;
                _projectionAction.ToolTip = "投影字幕已隐藏（点击显示）";
                return;
            }

            _projectionActionText.Text = "隐藏投影";
            _projectionAction.SetResourceReference(Border.BackgroundProperty, "BrushMenuSubSurface");
            _projectionAction.SetResourceReference(Border.BorderBrushProperty, "BrushMenuBorder");
            _projectionActionText.SetResourceReference(TextBlock.ForegroundProperty, "BrushMenuText");
            _projectionAction.ToolTip = "投影字幕已显示（点击隐藏）";
        }

        public void SetNdiToggleState(bool enabled)
        {
            if (_ndiActionText == null || _ndiAction == null)
            {
                return;
            }

            _ndiActionText.Text = "NDI";
            if (enabled)
            {
                _ndiAction.Background = new WpfSolidColorBrush(WpfColor.FromRgb(22, 163, 74));
                _ndiAction.BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(21, 128, 61));
                _ndiActionText.Foreground = WpfBrushes.White;
                _ndiAction.ToolTip = "NDI已开启（点击关闭）";
                return;
            }

            _ndiAction.SetResourceReference(Border.BackgroundProperty, "BrushMenuSubSurface");
            _ndiAction.SetResourceReference(Border.BorderBrushProperty, "BrushMenuBorder");
            _ndiActionText.SetResourceReference(TextBlock.ForegroundProperty, "BrushMenuText");
            _ndiAction.ToolTip = "NDI已关闭（点击开启）";
        }

        private void ToggleCaptionOrientation()
        {
            var nextOrientation = _captionOrientation == ProjectionCaptionOrientation.Horizontal
                ? ProjectionCaptionOrientation.Vertical
                : ProjectionCaptionOrientation.Horizontal;

            SetCaptionOrientationState(nextOrientation);
            CaptionOrientationRequested?.Invoke(nextOrientation);
        }

        private void OpenCaptionPositionMenu()
        {
            var menu = new ContextMenu();

            if (_captionOrientation == ProjectionCaptionOrientation.Vertical)
            {
                AddCaptionPositionItem(menu, "左", _captionHorizontalAnchor == ProjectionCaptionHorizontalAnchor.Left, () => ApplyCaptionPositionSelection(ProjectionCaptionHorizontalAnchor.Left, _captionVerticalAnchor));
                AddCaptionPositionItem(menu, "中", _captionHorizontalAnchor == ProjectionCaptionHorizontalAnchor.Center, () => ApplyCaptionPositionSelection(ProjectionCaptionHorizontalAnchor.Center, _captionVerticalAnchor));
                AddCaptionPositionItem(menu, "右", _captionHorizontalAnchor == ProjectionCaptionHorizontalAnchor.Right, () => ApplyCaptionPositionSelection(ProjectionCaptionHorizontalAnchor.Right, _captionVerticalAnchor));
            }
            else
            {
                AddCaptionPositionItem(menu, "上", _captionVerticalAnchor == ProjectionCaptionVerticalAnchor.Top, () => ApplyCaptionPositionSelection(_captionHorizontalAnchor, ProjectionCaptionVerticalAnchor.Top));
                AddCaptionPositionItem(menu, "中", _captionVerticalAnchor == ProjectionCaptionVerticalAnchor.Center, () => ApplyCaptionPositionSelection(_captionHorizontalAnchor, ProjectionCaptionVerticalAnchor.Center));
                AddCaptionPositionItem(menu, "下", _captionVerticalAnchor == ProjectionCaptionVerticalAnchor.Bottom, () => ApplyCaptionPositionSelection(_captionHorizontalAnchor, ProjectionCaptionVerticalAnchor.Bottom));
            }

            menu.PlacementTarget = _positionAction;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 2;
            menu.IsOpen = true;
        }

        private static void AddCaptionPositionItem(MenuItem parent, string text, bool selected, Action apply)
        {
            var item = new MenuItem
            {
                Header = BuildSelectedMenuHeader(text, selected)
            };
            item.Click += (_, _) => apply?.Invoke();
            parent.Items.Add(item);
        }

        private static void AddCaptionPositionItem(ContextMenu parent, string text, bool selected, Action apply)
        {
            var item = new MenuItem
            {
                Header = BuildSelectedMenuHeader(text, selected)
            };
            item.Click += (_, _) => apply?.Invoke();
            parent.Items.Add(item);
        }

        private static string BuildSelectedMenuHeader(string text, bool selected)
        {
            return selected ? $"{text}  ✓" : text;
        }

        private void ApplyCaptionPositionSelection(ProjectionCaptionHorizontalAnchor horizontalAnchor, ProjectionCaptionVerticalAnchor verticalAnchor)
        {
            SetCaptionPositionState(horizontalAnchor, verticalAnchor);
            CaptionPositionRequested?.Invoke(_captionHorizontalAnchor, _captionVerticalAnchor);
        }

        private void UpdateCaptionOrientationActionState()
        {
            if (_orientationAction == null || _orientationActionText == null)
            {
                return;
            }

            bool isHorizontal = _captionOrientation == ProjectionCaptionOrientation.Horizontal;
            _orientationAction.SetResourceReference(Border.BackgroundProperty, "BrushMenuSubSurface");
            _orientationAction.SetResourceReference(Border.BorderBrushProperty, isHorizontal ? "BrushMenuBorder" : "BrushGlobalIcon");
            _orientationActionText.Text = isHorizontal ? "横着" : "竖着";
            _orientationActionText.SetResourceReference(TextBlock.ForegroundProperty, isHorizontal ? "BrushMenuText" : "BrushGlobalIcon");
            _orientationAction.ToolTip = isHorizontal
                ? "字幕方向：横着（点击切到竖着）"
                : "字幕方向：竖着（点击切到横着）";
        }

        private void UpdateCaptionPositionActionState()
        {
            if (_positionAction == null || _positionActionText == null)
            {
                return;
            }

            _positionAction.SetResourceReference(Border.BackgroundProperty, "BrushMenuSubSurface");
            _positionAction.SetResourceReference(Border.BorderBrushProperty, "BrushGlobalIcon");
            _positionActionText.SetResourceReference(TextBlock.ForegroundProperty, "BrushGlobalIcon");
            if (_captionOrientation == ProjectionCaptionOrientation.Vertical)
            {
                _positionActionText.Text = $"位置:{GetHorizontalAnchorShortName(_captionHorizontalAnchor)}";
                _positionAction.ToolTip = $"字幕位置：{GetHorizontalAnchorDisplayName(_captionHorizontalAnchor)}";
            }
            else
            {
                _positionActionText.Text = $"位置:{GetVerticalAnchorShortName(_captionVerticalAnchor)}";
                _positionAction.ToolTip = $"字幕位置：{GetVerticalAnchorDisplayName(_captionVerticalAnchor)}";
            }
        }

        private static ProjectionCaptionHorizontalAnchor NormalizeCaptionHorizontalAnchor(ProjectionCaptionHorizontalAnchor anchor)
        {
            return anchor switch
            {
                ProjectionCaptionHorizontalAnchor.Left => ProjectionCaptionHorizontalAnchor.Left,
                ProjectionCaptionHorizontalAnchor.Right => ProjectionCaptionHorizontalAnchor.Right,
                _ => ProjectionCaptionHorizontalAnchor.Center
            };
        }

        private static ProjectionCaptionVerticalAnchor NormalizeCaptionVerticalAnchor(ProjectionCaptionVerticalAnchor anchor)
        {
            return anchor switch
            {
                ProjectionCaptionVerticalAnchor.Top => ProjectionCaptionVerticalAnchor.Top,
                ProjectionCaptionVerticalAnchor.Bottom => ProjectionCaptionVerticalAnchor.Bottom,
                _ => ProjectionCaptionVerticalAnchor.Center
            };
        }

        private static string GetHorizontalAnchorDisplayName(ProjectionCaptionHorizontalAnchor anchor)
        {
            return anchor switch
            {
                ProjectionCaptionHorizontalAnchor.Left => "靠左",
                ProjectionCaptionHorizontalAnchor.Right => "靠右",
                _ => "中间"
            };
        }

        private static string GetHorizontalAnchorShortName(ProjectionCaptionHorizontalAnchor anchor)
        {
            return anchor switch
            {
                ProjectionCaptionHorizontalAnchor.Left => "左",
                ProjectionCaptionHorizontalAnchor.Right => "右",
                _ => "中"
            };
        }

        private static string GetVerticalAnchorDisplayName(ProjectionCaptionVerticalAnchor anchor)
        {
            return anchor switch
            {
                ProjectionCaptionVerticalAnchor.Top => "顶部",
                ProjectionCaptionVerticalAnchor.Bottom => "底部",
                _ => "居中"
            };
        }

        private static string GetVerticalAnchorShortName(ProjectionCaptionVerticalAnchor anchor)
        {
            return anchor switch
            {
                ProjectionCaptionVerticalAnchor.Top => "上",
                ProjectionCaptionVerticalAnchor.Bottom => "下",
                _ => "中"
            };
        }

        private void ScheduleFloatingBoundsChanged()
        {
            if (_dockMode != LiveCaptionDockMode.Floating)
            {
                return;
            }

            _floatingBoundsChangedTimer.Stop();
            _floatingBoundsChangedTimer.Start();
        }

        private void FloatingBoundsChangedTimer_Tick(object sender, EventArgs e)
        {
            _floatingBoundsChangedTimer.Stop();

            Rect bounds = GetFloatingBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            FloatingBoundsChanged?.Invoke(bounds);
        }

        private Path CreateIcon(string iconKey)
        {
            var path = new Path
            {
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                Stroke = WpfBrushes.White,
                StrokeThickness = 1.8,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };

            if (TryFindResource(iconKey) is Geometry geometry)
            {
                path.Data = geometry;
            }
            else
            {
                path.Data = iconKey switch
                {
                    "IconLucideX" => Geometry.Parse("M18 6L6 18 M6 6L18 18"),
                    "IconLucideType" => Geometry.Parse("M4 6H20 M12 6V20 M8 20H16"),
                    "IconLucideSettings" => Geometry.Parse("M12 2V4 M12 20V22 M4.93 4.93L6.34 6.34 M17.66 17.66L19.07 19.07 M2 12H4 M20 12H22 M4.93 19.07L6.34 17.66 M17.66 6.34L19.07 4.93 M12 8A4 4 0 1 0 12 16A4 4 0 1 0 12 8"),
                    "IconLucideLayout" => Geometry.Parse("M2 4H12 M2 7H12 M2 10H12"),
                    "IconLucideCrosshair" => Geometry.Parse("M7 2V5 M7 9V12 M2 7H5 M9 7H12 M3.5 3.5L2 2 M10.5 3.5L12 2 M3.5 10.5L2 12 M10.5 10.5L12 12"),
                    _ => Geometry.Parse("M21 12A9 9 0 1 1 18.4 5.6 M21 3V9H15")
                };
            }

            return path;
        }

        private void StartSettingsIconAnimation()
        {
            var rotate = new RotateTransform(0, 7, 7);
            _settingsIcon.RenderTransform = rotate;
            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(3.2)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        public void UpdateCaption(string text, int? highlightStart = null)
        {
            string target = text ?? string.Empty;
            if (target.Length == 0)
            {
                _typingTimer.Stop();
                _typingTarget = string.Empty;
                _currentCaptionRawText = string.Empty;
                _typingIndex = 0;
                _highlightStartIndex = 0;
                RenderCaption(string.Empty, 0);
                return;
            }

            if (highlightStart.HasValue)
            {
                _typingTimer.Stop();
                _typingTarget = target;
                _currentCaptionRawText = target;
                _typingIndex = target.Length;
                _highlightStartIndex = Math.Clamp(highlightStart.Value, 0, target.Length);
                RenderCaption(target, _highlightStartIndex);
                return;
            }

            string baseline = string.IsNullOrEmpty(_typingTarget) ? _currentCaptionRawText : _typingTarget;
            if (string.Equals(baseline, target, StringComparison.Ordinal))
            {
                return;
            }

            int common = FindCommonPrefix(baseline, target);
            bool isPureAppend = baseline.Length > 0
                                && target.Length > baseline.Length
                                && common == baseline.Length
                                && target.StartsWith(baseline, StringComparison.Ordinal);
            bool canStream = isPureAppend && _typingAnimationEnabled;
            // Highlight only when content is true append; for rewrites/reflows keep full white to avoid "all yellow".
            _highlightStartIndex = isPureAppend
                ? Math.Clamp(common, 0, target.Length)
                : target.Length;
            if (!canStream)
            {
                _typingTimer.Stop();
                _typingTarget = target;
                _currentCaptionRawText = target;
                _typingIndex = target.Length;
                RenderCaption(target, _highlightStartIndex);
                return;
            }

            _typingTarget = target;
            _currentCaptionRawText = target;
            _typingIndex = common;
            RenderCaption(target.Substring(0, common), _highlightStartIndex);
            if (!_typingTimer.IsEnabled)
            {
                _typingTimer.Start();
            }
        }

        public void SetDockMode(LiveCaptionDockMode mode)
        {
            if (_dockMode == mode)
            {
                return;
            }

            _dockMode = mode;
            if (_dockMode == LiveCaptionDockMode.Floating)
            {
                _suppressFloatingBoundsTracking = true;
            }

            ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                $"DockMode: set to {_dockMode}, hasCustom={_hasCustomFloatingBounds}, cachedFloating=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##}), suppress={_suppressFloatingBoundsTracking}");
            ScheduleApplyLayout();
        }

        public LiveCaptionDockMode GetDockMode() => _dockMode;

        public bool IsWorkAreaReservationEnabled => _workAreaMode == LiveCaptionWorkAreaMode.ReserveWithAppBar;

        public void SetWorkAreaReservationEnabled(bool enabled)
        {
            var next = enabled ? LiveCaptionWorkAreaMode.ReserveWithAppBar : LiveCaptionWorkAreaMode.WindowOnly;
            if (_workAreaMode == next)
            {
                return;
            }

            _workAreaMode = next;
            if (_workAreaMode == LiveCaptionWorkAreaMode.WindowOnly)
            {
                RemoveAppBar(force: true);
            }

            ScheduleApplyLayout();
        }

        public void ResizeOverlay(double widthDelta, double heightDelta)
        {
            if (_dockMode == LiveCaptionDockMode.Floating)
            {
                _floatingWidth = Math.Max(MinWidth, _floatingWidth + widthDelta);
                _floatingHeight = Math.Max(MinHeight, _floatingHeight + heightDelta);
            }
            else
            {
                _bandHeight = Math.Max(MinHeight, _bandHeight + heightDelta);
            }

            ScheduleApplyLayout();
        }

        public double GetBandHeight() => _bandHeight;

        public void SetFloatingBounds(double left, double top, double width, double height)
        {
            if (double.IsNaN(left) || double.IsInfinity(left) ||
                double.IsNaN(top) || double.IsInfinity(top) ||
                double.IsNaN(width) || double.IsInfinity(width) ||
                double.IsNaN(height) || double.IsInfinity(height))
            {
                return;
            }

            _floatingLeft = left;
            _floatingTop = top;
            _floatingWidth = Math.Max(MinWidth, width);
            _floatingHeight = Math.Max(MinHeight, height);
            _hasCustomFloatingBounds = true;
            _suppressFloatingBoundsTracking = true;
            ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                $"SetFloatingBounds: input=({left:0.##},{top:0.##},{width:0.##}x{height:0.##}), cached=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##}), visible={IsVisible}, dock={_dockMode}");

            if (_dockMode == LiveCaptionDockMode.Floating && IsVisible)
            {
                ScheduleApplyLayout();
            }
        }

        public Rect GetFloatingBounds()
        {
            if (_dockMode == LiveCaptionDockMode.Floating && IsVisible)
            {
                return new Rect(Left, Top, Width, Height);
            }

            return new Rect(_floatingLeft, _floatingTop, _floatingWidth, _floatingHeight);
        }

        public double GetCaptionTextAvailableWidth()
        {
            double windowWidth = ActualWidth > 0 ? ActualWidth : Width;
            if (windowWidth <= 0)
            {
                windowWidth = _dockMode == LiveCaptionDockMode.Floating
                    ? _floatingWidth
                    : SystemParameters.WorkArea.Width;
            }

            double shell = _captionSurface.Margin.Left + _captionSurface.Margin.Right
                + _captionSurface.Padding.Left + _captionSurface.Padding.Right
                + _captionSurface.BorderThickness.Left + _captionSurface.BorderThickness.Right;

            double actionPanelWidth = 0;
            if (_actionPanel != null && _actionPanel.Visibility == Visibility.Visible)
            {
                double panelMeasured = _actionPanel.ActualWidth;
                if (panelMeasured <= 0)
                {
                    panelMeasured = _actionPanel.DesiredSize.Width;
                }

                actionPanelWidth = Math.Max(0, panelMeasured) + 16;
            }

            double available = windowWidth - shell - 24 - actionPanelWidth;
            return Math.Max(220, available);
        }

        public void ResetFloatingPlacement()
        {
            var wa = SystemParameters.WorkArea;
            _floatingWidth = Math.Max(MinWidth, _floatingWidth);
            _floatingHeight = Math.Max(MinHeight, _floatingHeight);
            _floatingLeft = wa.Left + (wa.Width - _floatingWidth) / 2;
            _floatingTop = wa.Top + 24;
            if (_dockMode == LiveCaptionDockMode.Floating && IsVisible)
            {
                ScheduleApplyLayout();
            }
        }

        private void ScheduleApplyLayout()
        {
            if (!IsVisible || _handle == IntPtr.Zero)
            {
                ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                    $"ScheduleApplyLayout: skipped visible={IsVisible}, handle=0x{_handle.ToInt64():X}, dock={_dockMode}, cachedFloating=({_floatingLeft:0.##},{_floatingTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##})");
                return;
            }

            if (_layoutApplyScheduled)
            {
                return;
            }

            _layoutApplyScheduled = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _layoutApplyScheduled = false;
                ApplyLayout();
            }), DispatcherPriority.Background);
        }

        private void ScheduleSettleLayoutRefresh()
        {
            if (!IsVisible || _dockMode == LiveCaptionDockMode.Floating || _workAreaMode != LiveCaptionWorkAreaMode.ReserveWithAppBar)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(140);
                if (!IsVisible || _dockMode == LiveCaptionDockMode.Floating)
                {
                    return;
                }

                RefreshDockLayoutNow();
                ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log("SettleLayout: delayed refresh applied.");
            });
        }

        private void ApplyLayout()
        {
            int seq = ++_layoutApplySequence;
            if (_dockMode == LiveCaptionDockMode.TopBand)
            {
                ReserveTopOrBottomBand(ABE_TOP);
                ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                    $"ApplyLayout#{seq}: dock=TopBand, dip=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##})");
                return;
            }

            if (_dockMode == LiveCaptionDockMode.BottomBand)
            {
                ReserveTopOrBottomBand(ABE_BOTTOM);
                ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                    $"ApplyLayout#{seq}: dock=BottomBand, dip=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##})");
                return;
            }

            RemoveAppBar();
            var wa = SystemParameters.WorkArea;
            _suppressFloatingBoundsTracking = true;
            try
            {
                Width = _floatingWidth;
                Height = _floatingHeight;
                if (!_hasCustomFloatingBounds)
                {
                    _floatingLeft = wa.Left + (wa.Width - Width) / 2;
                    _floatingTop = wa.Top + 24;
                }

                double minLeft = wa.Left;
                double maxLeft = wa.Right - Width;
                if (maxLeft < minLeft)
                {
                    maxLeft = minLeft;
                }

                double minTop = wa.Top;
                double maxTop = wa.Bottom - Height;
                if (maxTop < minTop)
                {
                    maxTop = minTop;
                }

                double targetLeft = _floatingLeft;
                double targetTop = _floatingTop;
                double appliedLeft = Math.Clamp(targetLeft, minLeft, maxLeft);
                double appliedTop = Math.Clamp(targetTop, minTop, maxTop);
                Left = appliedLeft;
                Top = appliedTop;
                _floatingLeft = appliedLeft;
                _floatingTop = appliedTop;
                _floatingWidth = Width;
                _floatingHeight = Height;

                ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                    $"ApplyLayout#{seq}: dock=Floating, wa=({wa.Left:0.##},{wa.Top:0.##},{wa.Width:0.##}x{wa.Height:0.##}), target=({targetLeft:0.##},{targetTop:0.##},{_floatingWidth:0.##}x{_floatingHeight:0.##}), applied=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##}), hasCustom={_hasCustomFloatingBounds}");
            }
            finally
            {
                _suppressFloatingBoundsTracking = false;
            }
        }

        private void ReserveTopOrBottomBand(uint edge)
        {
            if (_workAreaMode == LiveCaptionWorkAreaMode.ReserveWithAppBar)
            {
                ReserveAppBar(edge);
                return;
            }

            if (_appBarRegistered)
            {
                RemoveAppBar(force: true);
            }

            ReserveTopOrBottomBandWithoutAppBar(edge);
        }

        private void EnsureAppBarRegistered()
        {
            if (_appBarRegistered || _handle == IntPtr.Zero)
            {
                return;
            }

            var abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = _handle
            };
            SHAppBarMessage(ABM_NEW, ref abd);
            _appBarRegistered = true;
        }

        private void ReserveAppBar(uint edge)
        {
            if (_workAreaMode != LiveCaptionWorkAreaMode.ReserveWithAppBar)
            {
                ReserveTopOrBottomBandWithoutAppBar(edge);
                return;
            }

            if (!IsVisible)
            {
                return;
            }

            if (_handle == IntPtr.Zero)
            {
                var waDip = SystemParameters.WorkArea;
                Width = Math.Max(MinWidth, waDip.Width);
                Height = Math.Max(MinHeight, _bandHeight);
                Left = waDip.Left;
                Top = edge == ABE_TOP ? waDip.Top : waDip.Bottom - Height;
                return;
            }

            EnsureAppBarRegistered();

            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            int bandHeightPx = Math.Max(1, DipToPxY(_bandHeight));
            var abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = _handle,
                uEdge = edge,
                rc = new RECT
                {
                    left = bounds.Left,
                    top = bounds.Top,
                    right = bounds.Right,
                    bottom = bounds.Bottom
                }
            };

            if (edge == ABE_TOP)
            {
                abd.rc.bottom = abd.rc.top + bandHeightPx;
            }
            else
            {
                abd.rc.top = abd.rc.bottom - bandHeightPx;
            }

            SHAppBarMessage(ABM_QUERYPOS, ref abd);
            if (edge == ABE_TOP)
            {
                abd.rc.bottom = abd.rc.top + bandHeightPx;
            }
            else
            {
                abd.rc.top = abd.rc.bottom - bandHeightPx;
            }

            SHAppBarMessage(ABM_SETPOS, ref abd);

            double leftDip = PxToDipX(abd.rc.left);
            double topDip = PxToDipY(abd.rc.top);
            double widthDip = PxToDipX(abd.rc.right - abd.rc.left);
            double heightDip = PxToDipY(abd.rc.bottom - abd.rc.top);

            Left = leftDip;
            Top = topDip;
            Width = Math.Max(MinWidth, widthDip);
            Height = Math.Max(MinHeight, heightDip);
            _bandHeight = Height;

            ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log(
                $"ReserveAppBar: edge={(edge == ABE_TOP ? "Top" : "Bottom")} dpi={GetWindowScale():0.###} px=({abd.rc.left},{abd.rc.top},{abd.rc.right},{abd.rc.bottom}) dip=({Left:0.##},{Top:0.##},{Width:0.##}x{Height:0.##})");
        }

        private void ReserveTopOrBottomBandWithoutAppBar(uint edge)
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            double scale = GetWindowScale();
            double leftDip = bounds.Left / scale;
            double topDip = bounds.Top / scale;
            double widthDip = (bounds.Right - bounds.Left) / scale;
            double heightDip = Math.Max(MinHeight, _bandHeight);

            Left = leftDip;
            Width = Math.Max(MinWidth, widthDip);
            Height = heightDip;
            Top = edge == ABE_TOP
                ? topDip
                : (bounds.Bottom / scale) - Height;
        }

        public void RefreshDockLayoutNow()
        {
            if (!IsVisible || _handle == IntPtr.Zero)
            {
                return;
            }

            if (_workAreaMode == LiveCaptionWorkAreaMode.WindowOnly && _appBarRegistered)
            {
                RemoveAppBar(force: true);
            }

            ApplyLayout();
        }

        private void RemoveAppBar(bool force = false)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            if (!force && !_appBarRegistered)
            {
                return;
            }

            var abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = _handle
            };
            SHAppBarMessage(ABM_REMOVE, ref abd);
            _appBarRegistered = false;
            ImageColorChanger.Services.LiveCaption.LiveCaptionDebugLogger.Log("RemoveAppBar: released.");
        }

        private void TypingTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_typingTarget))
            {
                _typingTimer.Stop();
                return;
            }

            if (_typingIndex >= _typingTarget.Length)
            {
                RenderCaption(_typingTarget, _highlightStartIndex);
                _typingTimer.Stop();
                return;
            }

            int remain = _typingTarget.Length - _typingIndex;
            int step = remain > 30 ? 8 : remain > 15 ? 6 : 4;
            _typingIndex = Math.Min(_typingTarget.Length, _typingIndex + step);
            RenderCaption(_typingTarget.Substring(0, _typingIndex), _highlightStartIndex);
        }

        private void RenderCaption(string text, int highlightFrom)
        {
            _captionText.Inlines.Clear();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            bool hasSecondLine = text.IndexOf('\n') >= 0;
            double size = _captionText.FontSize > 0 ? _captionText.FontSize : 30;
            double effectiveGap = hasSecondLine ? _captionParagraphGap : 0;
            _captionText.LineHeight = size + effectiveGap;

            int split = Math.Clamp(highlightFrom, 0, text.Length);
            string spaced = ApplyLetterSpacing(text, out int[] indexMap);
            int splitSpaced = indexMap[Math.Clamp(split, 0, indexMap.Length - 1)];
            if (!_latestTextHighlightEnabled)
            {
                _captionText.Inlines.Add(new Run(spaced)
                {
                    Foreground = _baseTextBrush
                });
                return;
            }

            if (splitSpaced > 0)
            {
                _captionText.Inlines.Add(new Run(spaced.Substring(0, splitSpaced))
                {
                    Foreground = _baseTextBrush
                });
            }

            if (splitSpaced < spaced.Length)
            {
                _captionText.Inlines.Add(new Run(spaced.Substring(splitSpaced))
                {
                    Foreground = _latestTextHighlightBrush
                });
            }
        }

        private string ApplyLetterSpacing(string text, out int[] indexMap)
        {
            if (string.IsNullOrEmpty(text) || _captionLetterSpacing <= 0.01)
            {
                indexMap = BuildIdentityIndexMap(text ?? string.Empty);
                return text ?? string.Empty;
            }

            int repeat = Math.Clamp((int)Math.Round(_captionLetterSpacing), 1, 10);
            string spacer = new string('\u200A', repeat);
            var sb = new System.Text.StringBuilder(text.Length * (repeat + 1));
            indexMap = new int[text.Length + 1];
            for (int i = 0; i < text.Length; i++)
            {
                indexMap[i] = sb.Length;
                char ch = text[i];
                sb.Append(ch);
                if (ch != '\n' && ch != '\r' && i < text.Length - 1)
                {
                    char next = text[i + 1];
                    if (next != '\n' && next != '\r')
                    {
                        sb.Append(spacer);
                    }
                }
            }
            indexMap[text.Length] = sb.Length;

            return sb.ToString();
        }

        private static int[] BuildIdentityIndexMap(string text)
        {
            int length = text?.Length ?? 0;
            var map = new int[length + 1];
            for (int i = 0; i <= length; i++)
            {
                map[i] = i;
            }

            return map;
        }

        private static bool TryParseColor(string value, out WpfColor color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                object converted = System.Windows.Media.ColorConverter.ConvertFromString(value.Trim());
                if (converted is WpfColor parsed)
                {
                    color = parsed;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static int FindCommonPrefix(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return 0;
            }

            int max = Math.Min(a.Length, b.Length);
            for (int i = 0; i < max; i++)
            {
                if (a[i] != b[i])
                {
                    return i;
                }
            }

            return max;
        }

        private double GetWindowScale()
        {
            try
            {
                if (_handle != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(_handle);
                    if (dpi >= 96)
                    {
                        return dpi / 96.0;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return 1.0;
        }

        private double PxToDipX(int px)
        {
            return px / GetWindowScale();
        }

        private double PxToDipY(int px)
        {
            return px / GetWindowScale();
        }

        private int DipToPxY(double dip)
        {
            return Math.Max(1, (int)Math.Round(dip * GetWindowScale()));
        }
    }
}
