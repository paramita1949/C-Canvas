using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Data;
using System.Windows.Threading;
using ImageColorChanger.UI.Controls;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Panel = System.Windows.Controls.Panel;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>编辑画布的临时命中/预览状态；不改文本数据或持久层级。</summary>
    internal sealed class TextBoxPointerSelectionController : IDisposable
    {
        private readonly Canvas _canvas;
        private readonly Action<DraggableTextBox> _select;
        private readonly Rectangle _outline;
        private bool _disposed;
        private DraggableTextBox _explicitTarget;
        private readonly Dictionary<DraggableTextBox, bool> _inputLease = new();

        public DraggableTextBox HoveredTextBox { get; private set; }

        public TextBoxPointerSelectionController(Canvas canvas, Action<DraggableTextBox> select)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _select = select ?? throw new ArgumentNullException(nameof(select));
            _outline = new Rectangle
            {
                Stroke = Brushes.DodgerBlue, StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 5, 3 },
                Fill = Brushes.Transparent, IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Panel.SetZIndex(_outline, int.MaxValue);
            _canvas.Children.Add(_outline);
            _canvas.PreviewMouseMove += OnPointerMove;
            _canvas.PreviewMouseLeftButtonDown += OnLeftClick;
            _canvas.PreviewMouseRightButtonDown += OnRightClick;
            _canvas.PreviewKeyDown += OnModifierKeyDown;
            _canvas.MouseLeave += OnPointerLeave;
            _canvas.Unloaded += OnUnloaded;
            _canvas.LayoutUpdated += OnLayoutUpdated;
        }

        public IReadOnlyList<DraggableTextBox> GetCandidates(Point point)
        {
            if (_disposed || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
                return Array.Empty<DraggableTextBox>();

            return _canvas.Children.Cast<UIElement>()
                .Select((child, index) => (Box: child as DraggableTextBox, Index: index))
                .Where(item => IsAvailable(item.Box) && GetBounds(item.Box).Contains(point))
                .OrderByDescending(item => Panel.GetZIndex(item.Box))
                .ThenByDescending(item => item.Index)
                .Select(item => item.Box).ToArray();
        }

        /// <summary>悬停和单击共用落点规则，文字/边框优先于覆盖它们的透明空白。</summary>
        public DraggableTextBox ResolvePointerTarget(Point point)
        {
            var candidates = GetCandidates(point);
            if (candidates.Count == 0) return null;
            if (IsAvailable(_explicitTarget) && _explicitTarget.IsSelected && GetBounds(_explicitTarget).Contains(point))
                return _explicitTarget;
            var editing = candidates.FirstOrDefault(box => box.IsInEditMode);
            if (editing != null) return editing; // 编辑内文字选择不被下层候选抢走。
            foreach (var box in candidates)
            {
                var local = _canvas.TranslatePoint(point, box);
                var inner = new Rect(box.RenderSize);
                inner.Inflate(-Math.Min(4, inner.Width / 2), -Math.Min(4, inner.Height / 2));
                if (!inner.Contains(local) || box.HitTestPointerText(local)) return box;
            }
            return candidates[0]; // 空框和空白仍然可选，不吸附到框外最近文字。
        }

        public void PreviewAt(Point point) => Preview(ResolvePointerTarget(point));
        public void ClearPreview() => Preview(null);

        public MenuItem CreateSelectionMenu(Point point)
        {
            var candidates = GetCandidates(point);
            if (candidates.Count < 2)
            {
                Preview(null);
                return null;
            }
            var menu = new MenuItem { Header = "选择文本框" };
            for (int index = 0; index < candidates.Count; index++)
            {
                var box = candidates[index];
                var summary = string.Join(" ", (box.Data?.Content ?? "").Split(
                    new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                if (summary.Length == 0) summary = "空文本框";
                if (summary.Length > 24) summary = summary.Substring(0, 24) + "…";
                var item = new MenuItem { Header = $"{index + 1}. {summary}", Tag = box };
                item.MouseEnter += (s, e) => Preview(box);
                item.GotKeyboardFocus += (s, e) => Preview(box);
                item.Click += (s, e) =>
                {
                    if (Select(box))
                    {
                        // WPF 菜单关闭后会恢复旧焦点；明确候选必须在恢复之后仍是焦点目标。
                        if (ItemsControl.ItemsControlFromItemContainer(menu) is ContextMenu host)
                            host.Closed += (sender, args) => box.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                                new Action(() => { if (_explicitTarget == box && IsAvailable(box)) { _select(box); box.Focus(); } }));
                    }
                    Preview(null);
                };
                menu.Items.Add(item);
            }
            menu.SubmenuClosed += (s, e) => Preview(null);
            return menu;
        }

        public bool Select(DraggableTextBox box)
        {
            if (_disposed || !IsAvailable(box)) return false;
            ResetExplicitTarget();
            _explicitTarget = box;
            box.EditModeChanged += OnExplicitEditModeChanged;
            _select(box);
            box.Focus();
            UpdateInputLease();
            return true;
        }

        /// <summary>仅用户明确选过的候选获得临时输入目标；默认单击仍由最上层处理。</summary>
        public bool HandleExplicitLeftClick(Point point, int clickCount)
        {
            if (_explicitTarget == null) return false;
            if (!IsAvailable(_explicitTarget) || !_explicitTarget.IsSelected)
            {
                ResetExplicitTarget(); return false;
            }
            if (!GetBounds(_explicitTarget).Contains(point))
            {
                var defaultTarget = GetCandidates(point).FirstOrDefault();
                ResetExplicitTarget();
                // 恢复 hit-testing 不会重新计算本次 OriginalSource，主动完成这一点击。
                if (defaultTarget != null) { _select(defaultTarget); defaultTarget.Focus(); return true; }
                return false;
            }
            if (_explicitTarget.IsInEditMode) return false; // 交回原生 RichTextBox 光标/选区。
            if (clickCount >= 2) _explicitTarget.FocusTextBoxAt(_canvas.TranslatePoint(point, _explicitTarget));
            else _explicitTarget.Focus(); // 首次点击不拖动/不切回覆盖它的上层。
            return true;
        }

        private void OnLeftClick(object sender, MouseButtonEventArgs e)
        {
            if (DispatchPointerDown(e.GetPosition(_canvas), e)) e.Handled = true;
        }

        public bool DispatchPointerDown(Point point, MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
            {
                if (IsAvailable(_explicitTarget) && _explicitTarget.IsInEditMode && GetBounds(_explicitTarget).Contains(point))
                    return false; // Ctrl/Shift 点击仍由正在编辑的原生文本框接收。
                var nativeTarget = GetCandidates(point).FirstOrDefault();
                bool restoringInput = _inputLease.Count > 0;
                ResetExplicitTarget(false);
                if (restoringInput && nativeTarget != null)
                {
                    nativeTarget.HandlePointerMouseDown(e); // 目标外首击已计算旧命中，恢复后补交原入口。
                    return true;
                }
                return false;
            }
            if (_explicitTarget != null && (!IsAvailable(_explicitTarget) || !_explicitTarget.IsSelected))
                ResetExplicitTarget();
            // 明确目标现在可原生命中；手柄即便伸到框外也必须放行完整的 Thumb 手势。
            DependencyObject source = e.OriginalSource as DependencyObject;
            var target = ResolvePointerTarget(point);
            while (source != null)
            {
                if (source is System.Windows.Controls.Primitives.Thumb) return false;
                if (source == target || source == _explicitTarget) return false;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            if (target == null) return false;
            if (_explicitTarget == null)
            {
                bool wasSelected = target.IsSelected;
                Select(target); // 下层文字直接获得选择/输入目标，不要求先开候选菜单。
                if (e.ClickCount >= 2) target.FocusTextBoxAt(_canvas.TranslatePoint(point, target));
                else if (wasSelected) target.HandlePointerMouseDown(e);
                return true; // 首次单击只选择，避免立即产生拖动捕获。
            }
            if (target != _explicitTarget) ResetExplicitTarget();
            // 原始 hit 已经产生；将同一个按下交给控件，保留拖动捕获与双击状态。
            target.HandlePointerMouseDown(e);
            return true;
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            if (_inputLease.Count == 0) return;
            var point = e.GetPosition(_canvas);
            var target = IsAvailable(_explicitTarget) && GetBounds(_explicitTarget).Contains(point)
                ? _explicitTarget : GetCandidates(point).FirstOrDefault();
            if (target != _explicitTarget) ResetExplicitTarget();
            if (target != null) { target.OpenPointerContextMenu(e); e.Handled = true; }
        }

        private void OnExplicitEditModeChanged(object sender, bool editing)
        {
            if (editing) UpdateInputLease();
            else ResetExplicitTarget(false);
        }

        private void UpdateInputLease()
        {
            if (!IsAvailable(_explicitTarget) || !_explicitTarget.IsSelected)
            { ResetExplicitTarget(); return; }
            var bounds = GetBounds(_explicitTarget);
            var covered = _canvas.Children.OfType<DraggableTextBox>()
                .Where(box => box != _explicitTarget && IsAvailable(box) && GetBounds(box).IntersectsWith(bounds))
                .ToHashSet();
            foreach (var box in _inputLease.Keys.Where(box => !covered.Contains(box)).ToArray()) RestoreInput(box);
            foreach (var box in covered)
            {
                if (!_inputLease.ContainsKey(box)) _inputLease.Add(box, box.IsHitTestVisible);
                box.SetCurrentValue(UIElement.IsHitTestVisibleProperty, false);
            }
        }

        private void RestoreInput(DraggableTextBox box)
        {
            bool original = _inputLease[box]; _inputLease.Remove(box);
            box.SetCurrentValue(UIElement.IsHitTestVisibleProperty, original);
            BindingOperations.GetBindingExpression(box, UIElement.IsHitTestVisibleProperty)?.UpdateTarget();
        }

        private void ResetExplicitTarget(bool exitEditing = true)
        {
            var previous = _explicitTarget; _explicitTarget = null;
            if (previous != null)
            {
                previous.EditModeChanged -= OnExplicitEditModeChanged;
                if (exitEditing && previous.IsInEditMode) previous.ExitEditMode();
            }
            foreach (var box in _inputLease.Keys.ToArray()) RestoreInput(box);
        }

        private bool IsAvailable(DraggableTextBox box) => box != null && box.Data != null
            && box.Parent == _canvas && box.Visibility == Visibility.Visible
            && (!_canvas.IsLoaded || box.IsVisible) && box.IsEnabled
            && (box.IsHitTestVisible || _inputLease.TryGetValue(box, out bool originalHit) && originalHit);

        private Rect GetBounds(DraggableTextBox box) => box.TransformToAncestor(_canvas)
            .TransformBounds(new Rect(box.RenderSize));

        private void Preview(DraggableTextBox box)
        {
            HoveredTextBox = !_disposed && IsAvailable(box) ? box : null;
            UpdateOutline();
        }

        private void UpdateOutline()
        {
            if (!IsAvailable(HoveredTextBox))
            {
                HoveredTextBox = null;
                _outline.Visibility = Visibility.Collapsed;
                return;
            }
            // 画布清空/重载可能移除装饰；按需恢复，不依赖旧候选列表。
            if (_outline.Parent != _canvas) _canvas.Children.Add(_outline);
            var bounds = GetBounds(HoveredTextBox);
            Canvas.SetLeft(_outline, bounds.Left); Canvas.SetTop(_outline, bounds.Top);
            _outline.Width = bounds.Width; _outline.Height = bounds.Height;
            _outline.Visibility = Visibility.Visible;
        }

        private void OnPointerMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed || Mouse.Captured != null)
                Preview(null);
            else PreviewAt(e.GetPosition(_canvas));
        }
        private void OnPointerLeave(object sender, RoutedEventArgs e) => Preview(null);
        private void OnModifierKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                if (IsAvailable(_explicitTarget) && _explicitTarget.IsInEditMode) return;
                ResetExplicitTarget(false); // 仅对象选择态归还兄弟；编辑态保持原生输入/焦点/选区。
            }
        }
        private void OnUnloaded(object sender, RoutedEventArgs e) { Preview(null); ResetExplicitTarget(); }
        private void OnLayoutUpdated(object sender, EventArgs e)
        {
            if (_explicitTarget != null)
            {
                if (!IsAvailable(_explicitTarget) || !_explicitTarget.IsSelected) ResetExplicitTarget();
                else UpdateInputLease();
            }
            if (HoveredTextBox != null) UpdateOutline();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Preview(null); ResetExplicitTarget(); _disposed = true;
            _canvas.PreviewMouseMove -= OnPointerMove;
            _canvas.PreviewMouseLeftButtonDown -= OnLeftClick;
            _canvas.PreviewMouseRightButtonDown -= OnRightClick;
            _canvas.PreviewKeyDown -= OnModifierKeyDown;
            _canvas.MouseLeave -= OnPointerLeave;
            _canvas.Unloaded -= OnUnloaded;
            _canvas.LayoutUpdated -= OnLayoutUpdated;
            _canvas.Children.Remove(_outline);
        }
    }
}
