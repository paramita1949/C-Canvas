using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Managers;
using ImageColorChanger.Services.TextEditor.Models;

namespace ImageColorChanger.Services.TextEditor
{
    public sealed class TextElementPersistenceService : ITextElementPersistenceService
    {
        private readonly TextProjectManager _textProjectManager;
        private readonly ITextBoxEditSessionService _editSessionService;
        private readonly IRichTextSerializer _richTextSerializer;

        public TextElementPersistenceService(
            TextProjectManager textProjectManager,
            ITextBoxEditSessionService editSessionService,
            IRichTextSerializer richTextSerializer)
        {
            _textProjectManager = textProjectManager ?? throw new ArgumentNullException(nameof(textProjectManager));
            _editSessionService = editSessionService ?? throw new ArgumentNullException(nameof(editSessionService));
            _richTextSerializer = richTextSerializer ?? throw new ArgumentNullException(nameof(richTextSerializer));
        }

        public async Task SaveAsync(IReadOnlyCollection<TextBoxSnapshot> snapshots, CancellationToken cancellationToken = default)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                return;
            }

            var validSnapshots = snapshots
                .Where(s => s?.Element != null && s.TextElementId > 0)
                .ToList();

            if (validSnapshots.Count == 0)
            {
                return;
            }

            foreach (var snapshot in validSnapshots)
            {
                _editSessionService.SetEditing(snapshot.TextElementId, snapshot.WasInEditMode);
            }

            using (_editSessionService.BeginSaving(validSnapshots.Select(s => s.TextElementId)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var snapshot in validSnapshots)
                {
                    snapshot.Element.Content = snapshot.Content ?? string.Empty;
                }

                await _textProjectManager.UpdateElementsAsync(validSnapshots.Select(s => s.Element));

                foreach (var snapshot in validSnapshots)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var spansToSave = _richTextSerializer
                        .UpgradeToV2(snapshot.Content, snapshot.RichTextSpans?.ToList() ?? new List<Database.Models.RichTextSpan>(), snapshot.TextElementId)
                        .ToList();

                    if (spansToSave.Count > 0)
                    {
                        await _textProjectManager.SaveRichTextSpansAsync(snapshot.TextElementId, spansToSave);
                    }
                    else
                    {
                        await _textProjectManager.DeleteRichTextSpansByElementIdAsync(snapshot.TextElementId);
                    }

                    snapshot.Element.RichTextSpans = spansToSave;
                }
            }
        }
    }
}
