using System.Collections.Generic;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor.Models;

namespace ImageColorChanger.UI.Controls
{
    public partial class DraggableTextBox
    {
        /// <summary>
        /// 无副作用提取保存快照，不改变编辑态与焦点。
        /// </summary>
        public TextBoxSnapshot CaptureSnapshotForSave()
        {
            if (_richTextBox != null && !_isPlaceholderText)
            {
                SyncTextFromRichTextBox();
            }

            var richTextSpans = new List<RichTextSpan>();
            if (_richTextBox != null && !_isPlaceholderText)
            {
                richTextSpans = ExtractRichTextSpansFromFlowDocument() ?? new List<RichTextSpan>();
            }

            if (Data != null)
            {
                Data.Content = Data.Content ?? string.Empty;
                Data.RichTextSpans = richTextSpans;
            }

            return new TextBoxSnapshot(
                Data,
                Data?.Content ?? string.Empty,
                richTextSpans,
                _textLayoutProfile,
                IsInEditMode);
        }

        private void ApplyTextLayoutProfile()
        {
            _textLayoutService.ApplyLayout(_richTextBox, _textLayoutProfile, Data);
        }

        public void RefreshTextLayoutProfile()
        {
            ApplyTextLayoutProfile();
        }
    }
}
