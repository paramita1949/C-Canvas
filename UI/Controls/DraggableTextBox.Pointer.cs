using System;
using System.Windows;
using System.Windows.Documents;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace ImageColorChanger.UI.Controls
{
    public partial class DraggableTextBox
    {
        /// <summary>框内实际文字命中；不把最近字符或空白区域当作字形。</summary>
        internal bool HitTestPointerText(WpfPoint point)
        {
            if (_richTextBox == null || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
                return false;
            var textPoint = TranslatePoint(point, _richTextBox);
            var position = _richTextBox.GetPositionFromPoint(textPoint, false);
            if (position == null) return false;
            return ContainsGlyph(position, textPoint)
                || ContainsGlyph(position.GetNextInsertionPosition(LogicalDirection.Backward), textPoint);
        }

        private static bool ContainsGlyph(TextPointer start, WpfPoint point)
        {
            if (start == null || start.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
                return false;
            var text = start.GetTextInRun(LogicalDirection.Forward);
            if (text.Length == 0 || char.IsWhiteSpace(text[0])) return false;
            var end = start.GetNextInsertionPosition(LogicalDirection.Forward);
            if (end == null) return false;
            var a = start.GetCharacterRect(LogicalDirection.Forward);
            var b = end.GetCharacterRect(LogicalDirection.Backward);
            if (a.IsEmpty || b.IsEmpty || Math.Abs(a.Top - b.Top) > 1) return false;
            var glyph = new WpfRect(Math.Min(a.Left, b.Left), a.Top,
                Math.Max(a.Right, b.Right) - Math.Min(a.Left, b.Left), Math.Max(a.Height, b.Height));
            return glyph.Contains(point);
        }

        /// <summary>进入编辑并在控件局部落点定位插入点；文字命中与编辑吸附分别处理。</summary>
        public void FocusTextBoxAt(WpfPoint point)
        {
            if (_richTextBox == null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
            bool selectAll = _isPlaceholderText || _isNewlyCreated;
            EnterEditMode(selectAll);
            _isNewlyCreated = false;
            if (selectAll) return; // 新建/占位框继续保留原全选输入语义。
            _richTextBox.UpdateLayout();
            var position = _richTextBox.GetPositionFromPoint(TranslatePoint(point, _richTextBox), true);
            if (position != null)
            {
                _richTextBox.Selection.Select(position, position);
                _richTextBox.CaretPosition = position;
                UpdateCaretBrushForCurrentPosition();
            }
        }
    }
}
