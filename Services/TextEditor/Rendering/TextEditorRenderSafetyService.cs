using System;
using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public sealed class TextEditorRenderSafetyService : ITextEditorRenderSafetyService
    {
        public void Execute(
            IEnumerable<DraggableTextBox> textBoxes,
            Action renderAction,
            Action beforeRenderAction = null,
            Action afterRenderAction = null)
        {
            if (renderAction == null)
            {
                throw new ArgumentNullException(nameof(renderAction));
            }

            Execute(
                textBoxes,
                () =>
                {
                    renderAction();
                    return true;
                },
                beforeRenderAction,
                afterRenderAction);
        }

        public T Execute<T>(
            IEnumerable<DraggableTextBox> textBoxes,
            Func<T> renderFunc,
            Action beforeRenderAction = null,
            Action afterRenderAction = null)
        {
            if (renderFunc == null)
            {
                throw new ArgumentNullException(nameof(renderFunc));
            }

            var safeTextBoxes = textBoxes?.Where(tb => tb != null).ToList() ?? new List<DraggableTextBox>();
            try
            {
                foreach (var textBox in safeTextBoxes)
                {
                    _ = textBox.CaptureSnapshotForSave();
                    textBox.HideDecorations();
                }

                beforeRenderAction?.Invoke();
                return renderFunc();
            }
            finally
            {
                try
                {
                    afterRenderAction?.Invoke();
                }
                finally
                {
                    foreach (var textBox in safeTextBoxes)
                    {
                        textBox.RestoreDecorations();
                    }
                }
            }
        }
    }
}
