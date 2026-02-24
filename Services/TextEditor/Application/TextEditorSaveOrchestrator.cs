using System;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.TextEditor.Application.Models;

namespace ImageColorChanger.Services.TextEditor.Application
{
    public sealed class TextEditorSaveOrchestrator : ITextEditorSaveOrchestrator
    {
        private readonly ITextElementPersistenceService _textElementPersistenceService;

        public TextEditorSaveOrchestrator(ITextElementPersistenceService textElementPersistenceService)
        {
            _textElementPersistenceService = textElementPersistenceService ?? throw new ArgumentNullException(nameof(textElementPersistenceService));
        }

        public async Task<TextEditorSaveResult> SaveAsync(TextEditorSaveRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            bool textSaved = false;
            bool additionalSaved = false;
            bool thumbnailSaved = false;
            string thumbnailPath = null;

            try
            {
                ct.ThrowIfCancellationRequested();
                await _textElementPersistenceService.SaveAsync(request.Snapshots, ct);
                textSaved = true;

                if (request.PersistAdditionalStateAsync != null)
                {
                    ct.ThrowIfCancellationRequested();
                    await request.PersistAdditionalStateAsync(ct);
                    additionalSaved = true;
                }

                if (request.SaveThumbnailAsync != null)
                {
                    ct.ThrowIfCancellationRequested();
                    thumbnailPath = await request.SaveThumbnailAsync(ct);
                    thumbnailSaved = !string.IsNullOrEmpty(thumbnailPath);
                }

                return TextEditorSaveResult.Success(
                    request.Trigger,
                    textElementsSaved: textSaved,
                    additionalStateSaved: additionalSaved,
                    thumbnailSaved: thumbnailSaved,
                    thumbnailPath: thumbnailPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return TextEditorSaveResult.Failure(
                    request.Trigger,
                    ex,
                    textElementsSaved: textSaved,
                    additionalStateSaved: additionalSaved,
                    thumbnailSaved: thumbnailSaved,
                    thumbnailPath: thumbnailPath);
            }
        }
    }
}
