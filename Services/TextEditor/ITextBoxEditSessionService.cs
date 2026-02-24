using System;
using System.Collections.Generic;

namespace ImageColorChanger.Services.TextEditor
{
    public interface ITextBoxEditSessionService
    {
        TextBoxEditSessionState GetState(int textElementId);

        void SetSelected(int textElementId, bool isSelected);

        void SetEditing(int textElementId, bool isEditing);

        IDisposable BeginSaving(IEnumerable<int> textElementIds);
    }
}
