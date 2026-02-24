using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Repositories.TextEditor
{
    public sealed class EfRichTextSpanRepository : IRichTextSpanRepository
    {
        private readonly CanvasDbContext _dbContext;

        public EfRichTextSpanRepository(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<RichTextSpan> AddAsync(RichTextSpan span)
        {
            if (span == null)
            {
                throw new ArgumentNullException(nameof(span));
            }

            if (string.IsNullOrWhiteSpace(span.FormatVersion))
            {
                span.FormatVersion = RichTextDocumentV2.CurrentFormatVersion;
            }
            if (!span.ParagraphIndex.HasValue)
            {
                span.ParagraphIndex = 0;
            }
            if (!span.RunIndex.HasValue)
            {
                span.RunIndex = span.SpanOrder;
            }

            _dbContext.RichTextSpans.Add(span);
            await _dbContext.SaveChangesAsync();
            return span;
        }

        public async Task DeleteByTextElementIdAsync(int textElementId)
        {
            var spans = await _dbContext.RichTextSpans
                .Where(s => s.TextElementId == textElementId)
                .ToListAsync();

            if (spans.Count == 0)
            {
                return;
            }

            _dbContext.RichTextSpans.RemoveRange(spans);
            await _dbContext.SaveChangesAsync();
        }

        public async Task SaveForTextElementAsync(int textElementId, IEnumerable<RichTextSpan> spans)
        {
            var spanList = spans?.ToList() ?? new List<RichTextSpan>();
            NormalizeRichTextSpansForSave(textElementId, spanList);

            var oldSpans = await _dbContext.RichTextSpans
                .Where(s => s.TextElementId == textElementId)
                .ToListAsync();

            if (oldSpans.Count > 0)
            {
                _dbContext.RichTextSpans.RemoveRange(oldSpans);
            }

            if (spanList.Count > 0)
            {
                _dbContext.RichTextSpans.AddRange(spanList);
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<RichTextSpan>> GetByTextElementIdAsync(int textElementId)
        {
            return await _dbContext.RichTextSpans
                .AsNoTracking()
                .Where(s => s.TextElementId == textElementId)
                .OrderBy(s => s.SpanOrder)
                .ToListAsync();
        }

        private static void NormalizeRichTextSpansForSave(int textElementId, List<RichTextSpan> spans)
        {
            var paragraphRunCursors = new Dictionary<int, int>();
            foreach (var span in spans.OrderBy(s => s.SpanOrder))
            {
                span.TextElementId = textElementId;

                if (string.IsNullOrWhiteSpace(span.FormatVersion))
                {
                    span.FormatVersion = RichTextDocumentV2.CurrentFormatVersion;
                }

                if (!span.ParagraphIndex.HasValue)
                {
                    span.ParagraphIndex = 0;
                }

                int paragraphIndex = span.ParagraphIndex.Value;
                if (!paragraphRunCursors.TryGetValue(paragraphIndex, out var runCursor))
                {
                    runCursor = 0;
                }

                if (!span.RunIndex.HasValue)
                {
                    span.RunIndex = runCursor;
                }

                paragraphRunCursors[paragraphIndex] = Math.Max(runCursor + 1, span.RunIndex.Value + 1);
            }
        }
    }
}
