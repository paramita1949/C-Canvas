using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Repositories.TextEditor;
using ImageColorChanger.Services.TextEditor.Models;

namespace ImageColorChanger.Services.TextEditor
{
    public sealed class TextElementPersistenceService : ITextElementPersistenceService
    {
        private readonly ITextElementRepository _textElementRepository;
        private readonly IRichTextSpanRepository _richTextSpanRepository;
        private readonly ITextBoxEditSessionService _editSessionService;
        private readonly IRichTextSerializer _richTextSerializer;

        public TextElementPersistenceService(
            ITextElementRepository textElementRepository,
            IRichTextSpanRepository richTextSpanRepository,
            ITextBoxEditSessionService editSessionService,
            IRichTextSerializer richTextSerializer)
        {
            _textElementRepository = textElementRepository ?? throw new ArgumentNullException(nameof(textElementRepository));
            _richTextSpanRepository = richTextSpanRepository ?? throw new ArgumentNullException(nameof(richTextSpanRepository));
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

                await _textElementRepository.UpdateRangeAsync(validSnapshots.Select(s => s.Element));

                foreach (var snapshot in validSnapshots)
                {
                    cancellationToken.ThrowIfCancellationRequested();

#if DEBUG
                    // int lineCount = (snapshot.Content ?? string.Empty)
                    //     .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                    //     .Length;
                    // int spanCount = snapshot.RichTextSpans?.Count ?? 0;
                    // int paragraphSpanCount = snapshot.RichTextSpans?.Count(s => s.ParagraphIndex.HasValue) ?? 0;
                    // System.Diagnostics.Debug.WriteLine(
                    //     $"[换行诊断][Persist] textElementId={snapshot.TextElementId}, " +
                    //     $"contentLen={(snapshot.Content ?? string.Empty).Length}, lineCount={lineCount}, " +
                    //     $"spanCount={spanCount}, paragraphSpanCount={paragraphSpanCount}");
#endif

                    var spansToSave = _richTextSerializer
                        .UpgradeToV2(snapshot.Content, snapshot.RichTextSpans?.ToList() ?? new List<Database.Models.RichTextSpan>(), snapshot.TextElementId)
                        .ToList();

                    if (spansToSave.Count > 0)
                    {
                        await _richTextSpanRepository.SaveForTextElementAsync(snapshot.TextElementId, spansToSave);
                    }
                    else
                    {
                        await _richTextSpanRepository.DeleteByTextElementIdAsync(snapshot.TextElementId);
                    }

                    snapshot.Element.RichTextSpans = spansToSave;
                }
            }
        }
    }
}
