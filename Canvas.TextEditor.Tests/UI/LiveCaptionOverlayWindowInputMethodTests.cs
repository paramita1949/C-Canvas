using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImageColorChanger.UI;
using ImageColorChanger.Managers;
using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class LiveCaptionOverlayWindowInputMethodTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("前一句已经提交。")]
        public void CompositionCommit_RetractThenRestore_KeepsProjectionVisible(string prefix)
        {
            WithWindow(window =>
            {
                SetMode(window, true);
                var editor = Assert.Single(Descendants<TextBox>(window));
                var composer = new LiveCaptionDisplayComposer();
                var ndiComposer = new LiveCaptionDisplayComposer(lineCharLimit: 20);
                var frames = new List<string>();
                var ndiFrames = new List<string>();
                using var projection = new CaptionProjection(window);
                window.InputMethodTextChanged += text =>
                {
                    var frame = composer.PushInputSnapshot(text);
                    frames.Add(frame.Display);
                    ndiFrames.Add(ndiComposer.PushInputSnapshot(text).Display);
                    window.UpdateCaption(frame.Display, frame.HighlightStart);
                    projection.Manager.UpdateProjectionCaptionOverlay(frame.Display, frame.HighlightStart);
                };
                const string spoken = "正在连续口述的这一句字幕";
                string expected = prefix + spoken;
                Compose(editor, TextCompositionManager.PreviewTextInputStartEvent);
                editor.Text = expected;
                Assert.Equal(expected, projection.Text);
                int before = frames.Count;

                // 现场TSF时序：撤下预编辑文字 → composition结束 → 下一次提交恢复同一文本。
                editor.Text = prefix;
                Compose(editor, TextCompositionManager.PreviewTextInputEvent);
                Pump(window);
                Assert.Equal(expected, GetInputText(window));
                Assert.Equal(expected, projection.Text);
                Assert.Equal(Visibility.Visible, projection.Container.Visibility);
                Compose(editor, TextCompositionManager.PreviewTextInputStartEvent);
                editor.Text = expected;
                Compose(editor, TextCompositionManager.PreviewTextInputEvent, spoken);
                PumpFor(TimeSpan.FromMilliseconds(180));
                Assert.DoesNotContain(frames.Skip(before), frame => frame != expected);
                Assert.DoesNotContain(ndiFrames, string.IsNullOrEmpty);
                Assert.Equal(expected, projection.Text);

                // 与ASR流式的同一撤回/恢复输入比较，提交后显示应一致。
                var asr = new LiveCaptionDisplayComposer();
                asr.Push(new LiveCaptionAsrText(expected, false));
                asr.Push(new LiveCaptionAsrText(prefix, false));
                var final = asr.Push(new LiveCaptionAsrText(expected, true));
                Assert.Equal(final.Display, composer.CurrentDisplay);
            });
        }

        [Fact]
        public void CompositionCancellation_SettlesToEmptyButExplicitClearIsImmediate()
        {
            WithWindow(window =>
            {
                SetMode(window, true);
                var editor = Assert.Single(Descendants<TextBox>(window));
                var frames = new List<string>();
                window.InputMethodTextChanged += frames.Add;
                Compose(editor, TextCompositionManager.PreviewTextInputStartEvent);
                editor.Text = "取消前的候选";
                editor.Clear();
                Compose(editor, TextCompositionManager.PreviewTextInputEvent);
                Assert.Equal("取消前的候选", frames.Last());
                PumpFor(TimeSpan.FromMilliseconds(180));
                Assert.Equal(string.Empty, frames.Last());

                Compose(editor, TextCompositionManager.PreviewTextInputStartEvent);
                editor.Text = "主动清空";
                window.ClearInputMethodText();
                Assert.Equal(string.Empty, frames.Last());
                Assert.Equal(string.Empty, GetInputText(window));
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PendingCompositionSnapshot_DisableOrClose_DoesNotPublishLater(bool close)
        {
            WithWindow(window =>
            {
                SetMode(window, true);
                var editor = Assert.Single(Descendants<TextBox>(window));
                var frames = new List<string>();
                window.InputMethodTextChanged += frames.Add;
                Compose(editor, TextCompositionManager.PreviewTextInputStartEvent);
                editor.Text = "最后一帧";
                editor.Clear();
                if (close) window.Close(); else SetMode(window, false);
                int count = frames.Count;
                PumpFor(TimeSpan.FromMilliseconds(180));
                Assert.Equal(count, frames.Count);
                Assert.Equal("最后一帧", frames.Last());
            });
        }

        private static void Compose(TextBox editor, RoutedEvent routedEvent, string text = "")
        {
            var composition = new TextComposition(InputManager.Current, editor, text);
            editor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = routedEvent });
        }

        private static void PumpFor(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = duration };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private sealed class CaptionProjection : IDisposable
        {
            public ProjectionManager Manager { get; } = (ProjectionManager)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ProjectionManager));
            public Grid Container { get; } = new();
            private readonly TextBlock _text = new();
            private readonly Window _window = new() { Width = 1280, Height = 720 };
            public string Text => new TextRange(_text.ContentStart, _text.ContentEnd).Text;

            public CaptionProjection(Window main)
            {
                var border = new Border { Child = _text };
                Container.Children.Add(border);
                _window.Content = Container;
                Set("_mainWindow", main);
                Set("_projectionWindow", _window);
                Set("_projectionCaptionOverlayContainer", Container);
                Set("_projectionCaptionOverlayBorder", border);
                Set("_projectionCaptionOverlayText", _text);
                Set("_projectionCaptionBaseBrush", Brushes.White);
                Set("_projectionCaptionLatestBrush", Brushes.Yellow);
                Set("_projectionCaptionPreferredFontSize", 46d);
                Set("_projectionCaptionPreferredPadding", 28d);
                Set("_projectionCaptionPreferredLineGap", 12d);
            }

            private void Set(string name, object value) => typeof(ProjectionManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Manager, value);
            public void Dispose() => _window.Close();
        }

        [Fact]
        public void Constructor_InputMethodIsFirstSourceAndEditorIsInactive()
        {
            WithWindow(window =>
            {
                var toggles = Descendants<CheckBox>(window).ToArray();
                Assert.Equal(new[] { "输入法", "实时语音", "经文识别" }, toggles.Select(toggle => toggle.Content));
                Assert.False(toggles[0].IsChecked ?? false);
                var editor = Assert.Single(Descendants<TextBox>(window));
                Assert.Equal(Visibility.Collapsed, editor.Visibility);
                Assert.False(editor.IsEnabled);
                Assert.True(InputMethod.GetIsInputMethodEnabled(editor));
                Assert.True(editor.AcceptsReturn);
                Assert.Equal(ScrollBarVisibility.Hidden, editor.VerticalScrollBarVisibility);
                Assert.True(editor.MaxHeight <= 72);
            });
        }

        [Fact]
        public void SetInputMethodMode_SynchronizesVisibilityWithoutRaisingToggleRequests()
        {
            WithWindow(window =>
            {
                var requests = new List<bool>();
                Subscribe(window, "InputMethodToggleRequested", (Action<bool>)requests.Add);
                SetMode(window, true);
                var editor = Assert.Single(Descendants<TextBox>(window));
                var toggle = Descendants<CheckBox>(window).Single(item => Equals(item.Content, "输入法"));
                Assert.True(toggle.IsChecked);
                Assert.True(editor.IsEnabled);
                Assert.Equal(Visibility.Visible, editor.Visibility);
                SetMode(window, false);
                Assert.False(toggle.IsChecked);
                Assert.False(editor.IsEnabled);
                Assert.Equal(Visibility.Collapsed, editor.Visibility);
                Assert.Empty(requests);

                toggle.IsChecked = true;
                toggle.IsChecked = false;
                Assert.Equal(new[] { true, false }, requests);
            });
        }

        [Fact]
        public void Editing_EmitsCompleteSnapshotsAndRenderPreservesSelection()
        {
            WithWindow(window =>
            {
                var snapshots = new List<string>();
                Subscribe(window, "InputMethodTextChanged", (Action<string>)snapshots.Add);
                SetMode(window, true);
                var editor = Assert.Single(Descendants<TextBox>(window));
                editor.Text = "中文输入第一句";
                editor.Select(4, 3);
                editor.SelectedText = "第二句";
                Assert.Equal("中文输入第二句", snapshots.Last());
                editor.Select(2, 2);
                int countBeforeRender = snapshots.Count;

                window.UpdateCaption("已经排版\n显示两行", 0);
                window.SetLatestTextHighlightEnabled(false);
                window.SetCaptionLetterSpacing(1);
                Assert.Equal("中文输入第二句", editor.Text);
                Assert.Equal(2, editor.SelectionStart);
                Assert.Equal(2, editor.SelectionLength);
                Assert.Equal(countBeforeRender, snapshots.Count);

                editor.SelectedText = string.Empty;
                Assert.Equal("中文第二句", snapshots.Last());
                Assert.Equal(editor.Text, GetInputText(window));
                Invoke(window, "ClearInputMethodText");
                Assert.Equal(string.Empty, snapshots.Last());
                Assert.Equal(string.Empty, editor.Text);
            });
        }

        [Fact]
        public void InactiveEditor_DoesNotPublishChangesOrDiscardItsDraft()
        {
            WithWindow(window =>
            {
                var snapshots = new List<string>();
                Subscribe(window, "InputMethodTextChanged", (Action<string>)snapshots.Add);
                var editor = Assert.Single(Descendants<TextBox>(window));
                editor.Text = "未启用";
                Assert.Empty(snapshots);
                SetMode(window, true);
                editor.Text = "保留草稿";
                SetMode(window, false);
                window.UpdateCaption("实时语音显示");
                Assert.Equal("保留草稿", editor.Text);
                editor.Text = "停用期间不投影";
                Invoke(window, "ClearInputMethodText");
                Assert.Equal(new[] { "保留草稿" }, snapshots);
            });
        }

        [Fact]
        public void InputMethodMode_HasNoVisibleEditorOrExtraLayoutRow()
        {
            WithWindow(window =>
            {
                double before = window.MinHeight;
                SetMode(window, true);
                Assert.Equal(before, window.MinHeight);
                var editor = Assert.Single(Descendants<TextBox>(window));
                Assert.Equal(0, editor.Opacity);
                Assert.Equal(new Thickness(0), editor.BorderThickness);
                Assert.False(editor.IsHitTestVisible);
                editor.Text = string.Join("\n", Enumerable.Repeat("较长输入", 20));
                window.UpdateCaption("字幕第一行\n字幕第二行");
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(window.Width, window.MinHeight));
                root.Arrange(new Rect(0, 0, window.Width, window.MinHeight));
                root.UpdateLayout();
                var caption = Descendants<TextBlock>(window).Single(text =>
                    new TextRange(text.ContentStart, text.ContentEnd).Text.Contains("字幕第一行"));
                var captionBottom = caption.TranslatePoint(new Point(0, caption.ActualHeight), root);
                var editorTop = editor.TranslatePoint(new Point(0, 0), root);
                var editorBottom = editor.TranslatePoint(new Point(0, editor.ActualHeight), root);
                Assert.True(editor.ActualHeight <= 1);
                Assert.True(caption.ActualHeight >= caption.LineHeight * 2);
                Assert.True(editorBottom.Y <= window.MinHeight);
                Assert.True(editor.ActualHeight <= editor.MaxHeight);
            });
        }

        [Fact]
        public void InputMethodMode_AutomaticallyFocusesAtTailOnActivation()
        {
            WithWindow(window =>
            {
                window.SetDockMode(LiveCaptionDockMode.Floating);
                window.SetWorkAreaReservationEnabled(false);
                window.Show();
                var editor = Assert.Single(Descendants<TextBox>(window));
                editor.Text = "已经输入的字幕";
                editor.Select(0, 3);
                SetMode(window, true);
                Pump(window);
                Assert.True(editor.IsKeyboardFocusWithin);
                Assert.Equal(editor.Text.Length, editor.CaretIndex);
                Assert.Equal(0, editor.SelectionLength);
                SetMode(window, false);
                Assert.False(editor.IsKeyboardFocusWithin);
            });
        }

        [Theory]
        [InlineData(640, 72, 0)]
        [InlineData(800, 88, 6)]
        [InlineData(1320, 29, 1)]
        [InlineData(640, 88, 10)]
        public void InputMethodLongText_AlwaysShowsTailWithinActualCaptionWidth(double width, double size, double spacing)
        {
            WithWindow(window =>
            {
                window.Width = width;
                window.SetCaptionTypography("Microsoft YaHei UI", size, 24, size + 8);
                window.SetCaptionLetterSpacing(spacing);
                SetMode(window, true);
                var editor = Assert.Single(Descendants<TextBox>(window));
                editor.Text = new string('国', 200) + "最后一句";
                Layout(window);
                var capacityMethod = typeof(LiveCaptionOverlayWindow).GetMethod("GetInputMethodLineCharLimit");
                Assert.NotNull(capacityMethod);
                int capacity = (int)capacityMethod.Invoke(window, null);
                var composer = new ImageColorChanger.Services.LiveCaption.LiveCaptionDisplayComposer(capacity);
                var frame = composer.PushInputSnapshot(editor.Text);
                window.UpdateCaption(frame.Display, frame.HighlightStart);
                Layout(window);
                var caption = (TextBlock)typeof(LiveCaptionOverlayWindow)
                    .GetField("_captionText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                string rendered = new TextRange(caption.ContentStart, caption.ContentEnd).Text;
                Assert.EndsWith("最后一句", rendered.Replace("\u200A", "").Replace("\r", "").Replace("\n", ""));
                foreach (string line in rendered.Split('\n'))
                {
                    var measure = new TextBlock { Text = line.TrimEnd('\r'), FontFamily = caption.FontFamily,
                        FontSize = caption.FontSize, FontWeight = caption.FontWeight };
                    TextOptions.SetTextFormattingMode(measure, TextFormattingMode.Display);
                    measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.True(measure.DesiredSize.Width <= caption.ActualWidth + 1,
                        $"lineWidth={measure.DesiredSize.Width} available={caption.ActualWidth} capacity={capacity}");
                    var origin = caption.TranslatePoint(new Point(0, 0), (FrameworkElement)window.Content);
                    Assert.True(origin.X + measure.DesiredSize.Width < width);
                }
                Assert.Equal(2, rendered.Split('\n').Length);
            });
        }

        [Fact]
        public void InputMethodMode_ToolbarClickReturnsFocusAndResizeRepublishesSnapshot()
        {
            WithWindow(window =>
            {
                window.SetDockMode(LiveCaptionDockMode.Floating);
                window.SetWorkAreaReservationEnabled(false);
                window.Show();
                SetMode(window, true);
                Pump(window);
                var editor = Assert.Single(Descendants<TextBox>(window));
                editor.Text = new string('国', 100) + "末尾";
                var snapshots = new List<string>();
                Subscribe(window, "InputMethodTextChanged", (Action<string>)snapshots.Add);
                var toggle = Descendants<CheckBox>(window).First();
                toggle.Focus();
                Pump(window);
                Assert.True(editor.IsKeyboardFocusWithin);
                window.Width = 640;
                Pump(window);
                window.UpdateLayout();
                Assert.Contains(editor.Text, snapshots);
                int narrow = (int)Invoke(window, "GetInputMethodLineCharLimit");
                window.Width = 1320;
                Pump(window);
                window.UpdateLayout();
                Assert.True((int)Invoke(window, "GetInputMethodLineCharLimit") > narrow);
                Assert.EndsWith("末尾", editor.Text);
            });
        }

        private static void Layout(LiveCaptionOverlayWindow window)
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(window.Width, window.MinHeight));
            root.Arrange(new Rect(0, 0, window.Width, window.MinHeight));
            root.UpdateLayout();
        }

        private static void Pump(Window window) => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        private static void SetMode(LiveCaptionOverlayWindow window, bool enabled) => Invoke(window, "SetInputMethodMode", enabled);

        private static object Invoke(LiveCaptionOverlayWindow window, string name, params object[] values)
        {
            MethodInfo method = typeof(LiveCaptionOverlayWindow).GetMethod(name);
            Assert.NotNull(method);
            return method.Invoke(window, values);
        }

        private static string GetInputText(LiveCaptionOverlayWindow window)
        {
            PropertyInfo property = typeof(LiveCaptionOverlayWindow).GetProperty("InputMethodText");
            Assert.NotNull(property);
            return (string)property.GetValue(window);
        }

        private static void Subscribe(LiveCaptionOverlayWindow window, string name, Delegate handler)
        {
            EventInfo eventInfo = typeof(LiveCaptionOverlayWindow).GetEvent(name);
            Assert.NotNull(eventInfo);
            eventInfo.AddEventHandler(window, handler);
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T item)
            {
                yield return item;
            }

            foreach (DependencyObject child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                foreach (T nested in Descendants<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private static void WithWindow(Action<LiveCaptionOverlayWindow> action)
        {
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new LiveCaptionOverlayWindow();
                    try
                    {
                        action(window);
                    }
                    finally
                    {
                        window.Close();
                    }
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (captured != null)
            {
                ExceptionDispatchInfo.Capture(captured).Throw();
            }
        }
    }
}
