using System;
using System.Collections.Generic;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    /// <summary>
    /// 通知覆盖层渲染守卫，确保通知投影路径同样走安全渲染流程。
    /// </summary>
    public sealed class TextEditorNoticeOverlayRenderService
    {
        private readonly ITextEditorRenderSafetyService _renderSafetyService;

        public TextEditorNoticeOverlayRenderService(ITextEditorRenderSafetyService renderSafetyService)
        {
            _renderSafetyService = renderSafetyService ?? throw new ArgumentNullException(nameof(renderSafetyService));
        }

        public T ExecuteSafely<T>(
            IEnumerable<DraggableTextBox> textBoxes,
            Func<T> renderFunc)
        {
            if (renderFunc == null)
            {
                throw new ArgumentNullException(nameof(renderFunc));
            }

            return _renderSafetyService.Execute(textBoxes, renderFunc);
        }
    }
}
