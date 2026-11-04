using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using WpfCanvas = System.Windows.Controls.Canvas;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Database.Models;
using ImageColorChanger.UI.Controls;

namespace Canvas.TextEditor.Tests.UI;

[Collection("Pointer WPF exclusive")]
public sealed class PointerSelectionBehaviorTests
{
    [Theory]
    [InlineData(640, 360)]
    [InlineData(640, 480)]
    public void CursorPosition_DirectHoverAndClick_SelectLowerTextThroughUpperBlank(int width, int height)
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = width, Height = height, Background = Brushes.White };
            var lower = Add(canvas, 1, "民数记第三章", 40, 120, 280, 120, 1);
            var upper = Add(canvas, 2, "上层标题", 20, 20, 480, 280, 8);
            var window = new Window { Content = canvas, Width = width + 20, Height = height + 40,
                ShowActivated = false, Left = -10000, Top = -10000 };
            object controller = null;
            try
            {
                window.Show(); canvas.UpdateLayout();
                controller = CreateController(canvas, box =>
                {
                    foreach (var sibling in canvas.Children.OfType<DraggableTextBox>())
                    {
                        if (sibling != box && sibling.IsInEditMode) sibling.ExitEditMode();
                        sibling.SetSelected(sibling == box);
                    }
                });
                var point = GlyphPoint(lower, canvas, 2);
                var resolver = controller.GetType().GetMethod("ResolvePointerTarget");
                Assert.NotNull(resolver);
                Assert.Same(lower, resolver.Invoke(controller, new object[] { point }));
                Invoke(controller, "PreviewAt", point);
                Assert.Same(lower, Get(controller, "HoveredTextBox"));
                Assert.False(lower.IsSelected); Assert.False(upper.IsSelected);
                SaveCanvas(canvas, $"cursor-direct-{width}x{height}.png");
                var down = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = upper };
                Assert.True((bool)Invoke(controller, "DispatchPointerDown", point, down));
                Assert.True(lower.IsSelected); Assert.False(upper.IsSelected);
                Assert.False(lower.IsInEditMode); Assert.False(lower.IsMouseCaptured);
                Assert.False(upper.IsHitTestVisible);
                Assert.Equal(1, lower.Data.ZIndex); Assert.Equal(8, upper.Data.ZIndex);
                Assert.True((bool)Invoke(controller, "HandleExplicitLeftClick", point, 2));
                Assert.True(lower.IsInEditMode); Assert.True(lower.RichTextBox.Selection.IsEmpty);
                Console.WriteLine($"CURSOR {width}x{height}: hover=lower; direct-click=lower; first-click-drag=False; doubleclick=lower; z=1,8");
            }
            finally { (controller as IDisposable)?.Dispose(); window.Close(); }
        });
    }

    [Fact]
    public void CursorPosition_CaretUsesTranslatedClickAndClearsOldSelection()
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360 };
            var box = Add(canvas, 1, "光标定位到这里继续编辑", 80, 80, 360, 150, 1);
            box.RenderTransform = new ScaleTransform(1.1, 1.2);
            var window = new Window { Content = canvas, Width = 660, Height = 400,
                ShowActivated = false, Left = -10000, Top = -10000 };
            try
            {
                window.Show(); canvas.UpdateLayout();
                var canvasPoint = GlyphPoint(box, canvas, 5);
                var local = canvas.TranslatePoint(canvasPoint, box);
                var richPoint = box.TranslatePoint(local, box.RichTextBox);
                var expected = box.RichTextBox.GetPositionFromPoint(richPoint, true);
                Assert.NotNull(expected);
                var method = typeof(DraggableTextBox).GetMethod("FocusTextBoxAt");
                Assert.NotNull(method);
                box.RichTextBox.SelectAll();
                method.Invoke(box, new object[] { local });
                Assert.True(box.IsInEditMode); Assert.True(box.RichTextBox.Selection.IsEmpty);
                Assert.Equal(0, expected.CompareTo(box.RichTextBox.CaretPosition));
                Assert.True(box.RichTextBox.Document.ContentStart.GetOffsetToPosition(box.RichTextBox.CaretPosition) > 3);
                SaveCanvas(canvas, "cursor-caret-transformed.png");
                Console.WriteLine("CURSOR caret: translated=True; click-position=True; stale-selection=False");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void CursorPosition_EmptyBorderHiddenAndInvalidPointsHaveSafeFallbacks()
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360 };
            var upper = Add(canvas, 1, "上层", 20, 20, 480, 280, 8);
            var lower = Add(canvas, 2, "", 80, 120, 220, 100, 1);
            var controller = CreateController(canvas, box => box.SetSelected(true));
            try
            {
                canvas.Measure(new Size(640, 360)); canvas.Arrange(new Rect(0, 0, 640, 360));
                var method = controller.GetType().GetMethod("ResolvePointerTarget");
                Assert.NotNull(method);
                Assert.Same(lower, method.Invoke(controller, new object[] { new Point(81, 170) }));
                Assert.Same(upper, method.Invoke(controller, new object[] { new Point(450, 250) }));
                Assert.Null(method.Invoke(controller, new object[] { new Point(double.NaN, 0) }));
                Assert.Null(method.Invoke(controller, new object[] { new Point(600, 340) }));
                lower.Visibility = Visibility.Hidden;
                Assert.Same(upper, method.Invoke(controller, new object[] { new Point(81, 170) }));
                Console.WriteLine("CURSOR fallback: empty-border=lower; blank=top; invalid=none; hidden=ignored");
            }
            finally { ((IDisposable)controller).Dispose(); }
        });
    }

    private static Point GlyphPoint(DraggableTextBox box, WpfCanvas canvas, int index)
    {
        var pointer = box.RichTextBox.Document.ContentStart.GetInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);
        for (int i = 0; i < index; i++)
            pointer = pointer.GetNextInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);
        var next = pointer.GetNextInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);
        var a = pointer.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
        var b = next.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
        return box.RichTextBox.TranslatePoint(new Point((a.Left + b.Right) / 2, a.Top + a.Height / 2), canvas);
    }

    [Fact]
    public void CursorPosition_EditingPreservesNativeSelectionOnRapidClicks()
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360 };
            var box = Add(canvas, 1, "编辑时保留原生文字选区", 40, 60, 380, 160, 1);
            var window = new Window { Content = canvas, Width = 660, Height = 400,
                ShowActivated = false, Left = -10000, Top = -10000 };
            try
            {
                window.Show(); canvas.UpdateLayout(); box.FocusTextBox();
                box.RichTextBox.SelectAll();
                var selection = box.RichTextBox.Selection.Text;
                for (int i = 0; i < 2; i++)
                    box.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, i,
                        System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                Assert.Equal(selection, box.RichTextBox.Selection.Text);
                Assert.True(box.IsInEditMode); Assert.False(box.IsMouseCaptured);
                Console.WriteLine("CURSOR edit: native-selection-preserved=True; rapid-click-does-not-reset-caret=True");
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.LeftCtrl)]
    [InlineData(System.Windows.Input.Key.LeftShift)]
    public void CursorPosition_ModifierKeyRestoresSiblingInputBeforeNextMouseHit(System.Windows.Input.Key key)
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360 };
            var lower = Add(canvas, 1, "下层", 40, 120, 240, 100, 1);
            var upper = Add(canvas, 2, "上层", 20, 20, 480, 280, 8);
            var window = new Window { Content = canvas, Width = 660, Height = 400,
                ShowActivated = false, Left = -10000, Top = -10000 };
            object controller = null;
            try
            {
                window.Show(); canvas.UpdateLayout();
                controller = CreateController(canvas, box => box.SetSelected(true));
                Invoke(controller, "Select", lower);
                Assert.False(upper.IsHitTestVisible);
                var keyDown = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window), 0, key) { RoutedEvent = UIElement.PreviewKeyDownEvent };
                lower.RaiseEvent(keyDown);
                Assert.False(keyDown.Handled);
                Assert.True(upper.IsHitTestVisible);
                var hit = canvas.InputHitTest(new Point(450, 240)) as DependencyObject;
                while (hit != null && hit is not DraggableTextBox)
                    hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
                Assert.Same(upper, hit);
                Console.WriteLine($"CURSOR modifier {key}: lease-released=True; next-native-hit=upper; key-not-consumed=True");
            }
            finally { (controller as IDisposable)?.Dispose(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.LeftCtrl)]
    [InlineData(System.Windows.Input.Key.LeftShift)]
    public void CursorPosition_ModifierDuringCoveredEditingKeepsNativeCaretTarget(System.Windows.Input.Key key)
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360 };
            var lower = Add(canvas, 1, "下层编辑文字光标", 40, 120, 280, 120, 1);
            var upper = Add(canvas, 2, "上层标题", 20, 20, 480, 280, 8);
            var window = new Window { Content = canvas, Width = 660, Height = 400,
                ShowActivated = false, Left = -10000, Top = -10000 };
            object controller = null;
            try
            {
                window.Show(); canvas.UpdateLayout();
                controller = CreateController(canvas, box => box.SetSelected(true));
                Invoke(controller, "Select", lower); lower.FocusTextBox(); lower.RichTextBox.SelectAll();
                var selectedText = lower.RichTextBox.Selection.Text;
                lower.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window), 0, key)
                    { RoutedEvent = UIElement.PreviewKeyDownEvent });
                Assert.True(lower.IsInEditMode); Assert.False(upper.IsHitTestVisible);
                Assert.True(lower.RichTextBox.IsKeyboardFocusWithin);
                Assert.Equal(selectedText, lower.RichTextBox.Selection.Text);
                var point = GlyphPoint(lower, canvas, 3);
                var hit = canvas.InputHitTest(point) as DependencyObject;
                var down = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = hit };
                Assert.False((bool)Invoke(controller, "DispatchPointerDown", point, down));
                Assert.Equal(selectedText, lower.RichTextBox.Selection.Text);
                Console.WriteLine($"CURSOR modifier-edit {key}: lower-native-hit=True; selection-preserved=True; rich-focus=True; next-click-not-consumed=True");
            }
            finally { (controller as IDisposable)?.Dispose(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(640, 360)]
    [InlineData(640, 480)]
    public void PointerCandidates_PreviewAndExplicitSelection_DoNotChangeLayers(int width, int height)
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = width, Height = height, Background = Brushes.White };
            var bottom = Add(canvas, 1, "民数记", 20, 20, 280, 140, 3);
            var top = Add(canvas, 2, "标题文本", 20, 20, 420, 240, 9);
            var window = new Window { Content = canvas, Width = width + 20, Height = height + 40,
                ShowActivated = false, Left = -10000, Top = -10000 };
            try
            {
                window.Show(); canvas.UpdateLayout();
                var controller = CreateController(canvas, box =>
                {
                    foreach (var sibling in canvas.Children.OfType<DraggableTextBox>())
                    {
                        if (sibling != box && sibling.IsInEditMode) sibling.ExitEditMode();
                        sibling.SetSelected(sibling == box);
                    }
                });
                var point = new Point(80, 60);
                Assert.Equal(new[] { top, bottom }, Candidates(controller, point));
                Invoke(controller, "PreviewAt", point);
                Assert.Same(top, Get(controller, "HoveredTextBox"));
                Assert.False(top.IsSelected); Assert.False(bottom.IsSelected);
                Assert.Equal(9, top.Data.ZIndex); Assert.Equal(3, bottom.Data.ZIndex);
                var menu = (MenuItem)Invoke(controller, "CreateSelectionMenu", point);
                Assert.Equal("选择文本框", menu.Header);
                Assert.Equal(2, menu.Items.Count);
                var lower = (MenuItem)menu.Items[1];
                Assert.Contains("民数记", lower.Header.ToString());
                lower.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                    { RoutedEvent = UIElement.MouseEnterEvent });
                Assert.Same(bottom, Get(controller, "HoveredTextBox"));
                Assert.False(bottom.IsSelected);
                SaveCanvas(canvas, $"pointer-{width}x{height}.png");
                top.FocusTextBox();
                lower.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.True(bottom.IsSelected); Assert.False(top.IsSelected); Assert.False(top.IsInEditMode);
                Assert.Equal(9, Panel.GetZIndex(top)); Assert.Equal(3, Panel.GetZIndex(bottom));
                Assert.Equal(9, top.Data.ZIndex); Assert.Equal(3, bottom.Data.ZIndex);
                bottom.FocusTextBox(); Assert.True(bottom.IsInEditMode);
                Invoke(controller, "PreviewAt", new Point(width - 10, height - 10));
                Assert.Null(Get(controller, "HoveredTextBox"));
                Console.WriteLine($"POINTER {width}x{height}: top-first; hover=bottom; selected=bottom; z=9,3; old-edit=False; new-edit=True");
                ((IDisposable)controller).Dispose();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void EmptyHiddenRemovedAndDuplicateCandidates_AreHandledDeterministically()
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360 };
            var controller = CreateController(canvas, box => box.SetSelected(true));
            Assert.Empty(Candidates(controller, new Point(1, 1)));
            Assert.Null(Invoke(controller, "CreateSelectionMenu", new Point(1, 1)));
            var first = Add(canvas, 1, "相同文字", 20, 20, 200, 100, 2);
            var second = Add(canvas, 2, "相同文字", 20, 20, 200, 100, 2);
            canvas.Measure(new Size(640, 360)); canvas.Arrange(new Rect(0, 0, 640, 360));
            var point = new Point(30, 30);
            Assert.Equal(new[] { second, first }, Candidates(controller, point));
            var menu = (MenuItem)Invoke(controller, "CreateSelectionMenu", point);
            var a = (MenuItem)menu.Items[0]; var b = (MenuItem)menu.Items[1];
            Assert.NotEqual(a.Header, b.Header);
            canvas.Children.Remove(second);
            a.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(second.IsSelected);
            first.Visibility = Visibility.Hidden;
            Assert.Empty(Candidates(controller, point));
            Assert.Empty(Candidates(controller, new Point(double.NaN, 0)));
            Assert.False((bool)Invoke(controller, "Select", second));
            ((IDisposable)controller).Dispose();
            Console.WriteLine("POINTER negative: empty=0; ties=last-child-first; duplicate-labels=distinct; removed-click=ignored; hidden=0; invalid=0");
        });
    }

    [Fact]
    public void ExplicitCoveredCandidate_DoubleClickTargetsLowerAndRestoresSiblingHitTesting()
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360 };
            var lower = Add(canvas, 1, "下层", 20, 20, 200, 100, 1);
            var upper = Add(canvas, 2, "上层", 20, 20, 400, 200, 8);
            var window = new Window { Content = canvas, Width = 660, Height = 400,
                ShowActivated = false, Left = -10000, Top = -10000 };
            try
            {
            window.Show(); canvas.UpdateLayout();
            var controller = CreateController(canvas, box =>
            {
                foreach (var sibling in canvas.Children.OfType<DraggableTextBox>())
                {
                    if (sibling != box && sibling.IsInEditMode) sibling.ExitEditMode();
                    sibling.SetSelected(sibling == box);
                }
            });
            canvas.Measure(new Size(640, 360)); canvas.Arrange(new Rect(0, 0, 640, 360));
            var click = controller.GetType().GetMethod("HandleExplicitLeftClick");
            Assert.True(click != null, "Explicit covered selection needs real preview-input routing, not only SetSelected.");
            Invoke(controller, "Select", lower);
            Assert.False(upper.IsHitTestVisible); // 明确选择后原生拖动/手柄也必须能命中下层。
            Assert.True((bool)click.Invoke(controller, new object[] { new Point(40, 40), 1 }));
            Assert.False(lower.IsInEditMode);
            Assert.True((bool)click.Invoke(controller, new object[] { new Point(40, 40), 2 }));
            Assert.True(lower.IsInEditMode); Assert.False(upper.IsHitTestVisible);
            Assert.Equal(2, Candidates(controller, new Point(40, 40)).Length);
            var hit = canvas.InputHitTest(new Point(40, 40)) as DependencyObject;
            while (hit != null && hit is not DraggableTextBox)
                hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
            Assert.Same(lower, hit);
            lower.ExitEditMode(); Assert.True(upper.IsHitTestVisible);
            Invoke(controller, "Select", lower);
            click.Invoke(controller, new object[] { new Point(40, 40), 2 });
            canvas.Children.Remove(lower); canvas.UpdateLayout();
            Assert.True(upper.IsHitTestVisible);
            Assert.Equal(1, lower.Data.ZIndex); Assert.Equal(8, upper.Data.ZIndex);
            ((IDisposable)controller).Dispose();
            Console.WriteLine("POINTER input: covered-doubleclick=lower; native-hit=lower; exit-restored=True; delete-restored=True; z=1,8");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void StyledNativeMenu_ClosesWithLowerStillSelectedAndFocused()
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360, Background = Brushes.White };
            var lower = Add(canvas, 1, "民数记", 20, 20, 280, 120, 1);
            var upper = Add(canvas, 2, "标题文本", 20, 20, 420, 200, 8);
            var window = new Window { Content = canvas, Width = 660, Height = 400,
                ShowActivated = false, Left = -10000, Top = -10000 };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/CanvasCast;component/UI/Resources/Styles/MainWindow.BaseStyles.xaml", UriKind.Relative)
            });
            window.Resources["ColorGlobalIcon"] = Color.FromRgb(0x91, 0x78, 0x78);
            object controller = null;
            try
            {
                window.Show(); canvas.UpdateLayout();
                controller = CreateController(canvas, box =>
                {
                    foreach (var sibling in canvas.Children.OfType<DraggableTextBox>())
                    {
                        if (sibling != box && sibling.IsInEditMode) sibling.ExitEditMode();
                        sibling.SetSelected(sibling == box);
                    }
                });
                typeof(DraggableTextBox).GetProperty("PointerSelectionMenuProvider").SetValue(upper,
                    new Func<Point, MenuItem>(_ => (MenuItem)Invoke(controller, "CreateSelectionMenu", new Point(40, 40))));
                upper.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
                    { RoutedEvent = UIElement.MouseRightButtonDownEvent });
                var menu = upper.ContextMenu;
                Assert.NotNull(menu); Assert.True(menu.IsOpen);
                Assert.Same(window.Resources["NoBorderContextMenuStyle"], menu.Style);
                var choose = (MenuItem)menu.Items[0]; choose.IsSubmenuOpen = true;
                DrainDispatcher(); menu.UpdateLayout(); choose.UpdateLayout();
                var popup = (System.Windows.Controls.Primitives.Popup)choose.Template.FindName("PART_Popup", choose);
                Assert.NotNull(popup); Assert.True(popup.IsOpen);
                Assert.Equal(2, choose.Items.Count);
                SaveVisual(menu, "pointer-native-menu.png");
                SaveVisual((FrameworkElement)popup.Child, "pointer-native-candidates.png");
                ((MenuItem)choose.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.IsOpen = false; DrainDispatcher();
                Assert.True(lower.IsSelected); Assert.False(upper.IsSelected);
                Assert.True(lower.IsKeyboardFocusWithin);
                Assert.Equal(1, lower.Data.ZIndex); Assert.Equal(8, upper.Data.ZIndex);
                Console.WriteLine("POINTER native-menu: style=reused; submenu-open=True; close-selected=lower; close-focus=lower; z=1,8");
            }
            finally { upper.ContextMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false); (controller as IDisposable)?.Dispose(); window.Close(); }
        });
    }

    [Fact]
    public void InputLease_PreservesBindingAndRoutesOutsideFirstClick()
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360 };
            var lower = Add(canvas, 1, "下层", 20, 20, 200, 100, 1);
            var upper = Add(canvas, 2, "上层", 20, 20, 400, 200, 8);
            var state = new HitState { Enabled = true };
            var binding = System.Windows.Data.BindingOperations.SetBinding(upper, UIElement.IsHitTestVisibleProperty,
                new System.Windows.Data.Binding(nameof(HitState.Enabled)) { Source = state });
            var controller = CreateController(canvas, box =>
            {
                foreach (var sibling in canvas.Children.OfType<DraggableTextBox>()) sibling.SetSelected(sibling == box);
            });
            canvas.Measure(new Size(640, 360)); canvas.Arrange(new Rect(0, 0, 640, 360));
            Invoke(controller, "Select", lower);
            Invoke(controller, "HandleExplicitLeftClick", new Point(40, 40), 2);
            Assert.False(upper.IsHitTestVisible);
            Assert.True((bool)Invoke(controller, "HandleExplicitLeftClick", new Point(350, 180), 1));
            Assert.True(upper.IsSelected); Assert.False(lower.IsInEditMode); Assert.True(upper.IsHitTestVisible);
            Assert.Same(binding, System.Windows.Data.BindingOperations.GetBindingExpression(upper, UIElement.IsHitTestVisibleProperty));
            Invoke(controller, "Select", lower);
            Invoke(controller, "HandleExplicitLeftClick", new Point(40, 40), 2);
            state.Enabled = false;
            lower.ExitEditMode();
            Assert.False(upper.IsHitTestVisible);
            Assert.Same(binding, System.Windows.Data.BindingOperations.GetBindingExpression(upper, UIElement.IsHitTestVisibleProperty));
            ((IDisposable)controller).Dispose();
            Console.WriteLine("POINTER lease: outside-firstclick=upper; binding-preserved=True; changed-binding-restored=False");
        });
    }

    public sealed class HitState { public bool Enabled { get; set; } }

    [Fact]
    public void ExplicitSelection_NativeDragResizeAndOutsideDoubleClickRemainReachable()
    {
        RunInSta(() =>
        {
            var canvas = new WpfCanvas { Width = 640, Height = 360 };
            var lower = Add(canvas, 1, "下层", 20, 20, 200, 100, 1);
            var upper = Add(canvas, 2, "上层", 20, 20, 400, 200, 8);
            var window = new Window { Content = canvas, Width = 660, Height = 400, Left = -10000, Top = -10000, ShowActivated = false };
            object controller = null;
            try
            {
                window.Show(); canvas.UpdateLayout();
                controller = CreateController(canvas, box =>
                {
                    foreach (var sibling in canvas.Children.OfType<DraggableTextBox>()) sibling.SetSelected(sibling == box);
                });
                Invoke(controller, "Select", lower);
                var dispatch = controller.GetType().GetMethod("DispatchPointerDown");
                Assert.True(dispatch != null, "Explicit input must preserve native drag/Thumb and record the outside first down.");
                var down = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = lower };
                Assert.False((bool)dispatch.Invoke(controller, new object[] { new Point(40, 40), down }));
                lower.RaiseEvent(down); Assert.True(lower.IsMouseCaptured);
                lower.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 1,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                Assert.False(lower.IsMouseCaptured);
                var thumb = FindVisualChild<System.Windows.Controls.Primitives.Thumb>(lower);
                Assert.NotNull(thumb);
                var handleDown = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 2,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = thumb };
                Assert.False((bool)dispatch.Invoke(controller, new object[] { new Point(14, 14), handleDown }));
                double oldWidth = lower.Width;
                thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(20, 0)
                    { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
                Assert.NotEqual(oldWidth, lower.Width);
                var outsideDown = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 3,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = canvas };
                Assert.True((bool)dispatch.Invoke(controller, new object[] { new Point(350, 180), outsideDown }));
                Assert.True(upper.IsSelected); Assert.False(upper.IsInEditMode);
                upper.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 4,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                Assert.True(upper.IsInEditMode);
                Console.WriteLine("POINTER gestures: lower-drag-captured=True; release=True; resize-changed=True; outside-doubleclick=upper");
            }
            finally { (controller as IDisposable)?.Dispose(); window.Close(); }
        });
    }

    [Fact]
    public void HostSelection_DoesNotCallAutomaticBringToFront_AndWiresPointerMenu()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "UI/MainWindow.TextEditor.Helpers.cs"));
        var selection = source.Substring(source.IndexOf("// 监听选中事件", StringComparison.Ordinal));
        selection = selection.Substring(0, selection.IndexOf("textBox.EditModeChanged", StringComparison.Ordinal));
        Assert.DoesNotContain("BringTextBoxToFront(textBox);", selection);
        Assert.Contains("PointerSelectionMenuProvider", source);
        var drag = File.ReadAllText(Path.Combine(root, "UI/Controls/DraggableTextBox.Drag.cs"));
        Assert.Contains("PointerSelectionMenuProvider", drag);
        Assert.Contains("置于顶层", drag);
    }

    private static DraggableTextBox Add(WpfCanvas canvas, int id, string text, double x, double y, double w, double h, int z)
    {
        var box = new DraggableTextBox(new TextElement { Id = id, Content = text, X = x, Y = y,
            Width = w, Height = h, ZIndex = z, FontColor = "#FF222222", FontSize = 28 });
        canvas.Children.Add(box); return box;
    }

    private static object CreateController(WpfCanvas canvas, Action<DraggableTextBox> select)
    {
        var type = typeof(DraggableTextBox).Assembly.GetType("ImageColorChanger.UI.Modules.TextBoxPointerSelectionController");
        Assert.True(type != null, "Missing pointer-selection controller (baseline has no explicit disambiguation).");
        return Activator.CreateInstance(type, canvas, select);
    }
    private static object Invoke(object obj, string method, params object[] args) => obj.GetType().GetMethod(method).Invoke(obj, args);
    private static object Get(object obj, string property) => obj.GetType().GetProperty(property).GetValue(obj);
    private static DraggableTextBox[] Candidates(object controller, Point point) =>
        ((IEnumerable<DraggableTextBox>)Invoke(controller, "GetCandidates", point)).ToArray();
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UI/MainWindow.xaml"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }
    private static void SaveCanvas(WpfCanvas canvas, string filename)
    {
        var output = Environment.GetEnvironmentVariable("CANVAS_POINTER_EVIDENCE");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, filename)); encoder.Save(stream);
    }
    private static void SaveVisual(FrameworkElement visual, string filename)
    {
        var output = Environment.GetEnvironmentVariable("CANVAS_POINTER_EVIDENCE");
        if (string.IsNullOrWhiteSpace(output)) return;
        visual.UpdateLayout();
        Assert.True(visual.ActualWidth > 0 && visual.ActualHeight > 0);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, filename)); encoder.Save(stream);
    }
    private static void DrainDispatcher() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
        System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
    private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child); if (nested != null) return nested;
        }
        return null;
    }
    private static void RunInSta(Action action)
    {
        Exception captured = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { captured = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF scenario timed out.");
        if (captured != null) ExceptionDispatchInfo.Capture(captured).Throw();
    }
}

[CollectionDefinition("Pointer WPF exclusive", DisableParallelization = true)]
public sealed class PointerWpfExclusiveCollection { }
