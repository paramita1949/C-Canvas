using System;

namespace ImageColorChanger.Services.LiveCaption
{
    /// <summary>
    /// 输入法完整快照的发布策略。时钟和控件时序由适配器负责，输出不能读取 PendingText。
    /// </summary>
    internal sealed class InputMethodCaptionSnapshotBuffer
    {
        public string PublishedText { get; private set; } = string.Empty;
        public string PendingText { get; private set; } = string.Empty;
        public bool IsComposing { get; private set; }
        public bool HasPendingSnapshot { get; private set; }

        public void BeginComposition() => IsComposing = true;

        public bool ObserveText(string text)
        {
            string next = text ?? string.Empty;
            PendingText = next;
            if (string.Equals(next, PublishedText, StringComparison.Ordinal))
            {
                HasPendingSnapshot = false;
                return false;
            }

            if (next.Length < PublishedText.Length)
            {
                // 合成回缩等待结束；普通删除/替换由适配器短暂合并后提交。
                HasPendingSnapshot = true;
                return false;
            }

            return Publish(next);
        }

        public bool FlushPending()
        {
            if (IsComposing || !HasPendingSnapshot) return false;
            return Publish(PendingText);
        }

        public bool CompleteComposition(string finalText)
        {
            IsComposing = false;
            return Publish(finalText ?? string.Empty);
        }

        public bool Reset(string text = "")
        {
            IsComposing = false;
            return Publish(text ?? string.Empty);
        }

        private bool Publish(string text)
        {
            bool changed = !string.Equals(text, PublishedText, StringComparison.Ordinal);
            PublishedText = text;
            PendingText = text;
            HasPendingSnapshot = false;
            return changed;
        }
    }
}
