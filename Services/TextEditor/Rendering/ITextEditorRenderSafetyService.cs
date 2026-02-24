using System;
using System.Collections.Generic;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public interface ITextEditorRenderSafetyService
    {
        void Execute(
            IEnumerable<DraggableTextBox> textBoxes,
            Action renderAction,
            Action beforeRenderAction = null,
            Action afterRenderAction = null);

        T Execute<T>(
            IEnumerable<DraggableTextBox> textBoxes,
            Func<T> renderFunc,
            Action beforeRenderAction = null,
            Action afterRenderAction = null);
    }
}
