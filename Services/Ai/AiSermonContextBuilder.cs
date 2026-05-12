using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor.Application;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiSermonContextBuilder
    {
        private static readonly Regex ScriptureReferenceRegex = new(
            @"[\u4e00-\u9fa5]{1,8}(?:书|记|音|传|录|篇)?\s*[0-9一二三四五六七八九十百]+[章:：]\s*[0-9一二三四五六七八九十百]+(?:[-~—到至][0-9一二三四五六七八九十百]+)?[节]?",
            RegexOptions.Compiled);

        private readonly ITextProjectService _textProjectService;

        public AiSermonContextBuilder(ITextProjectService textProjectService)
        {
            _textProjectService = textProjectService ?? throw new ArgumentNullException(nameof(textProjectService));
        }

        public async Task<AiProjectContextEnvelope> BuildAsync(int projectId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextProject project = await _textProjectService.LoadProjectAsync(projectId).ConfigureAwait(false);
            var slides = await _textProjectService.GetSlidesByProjectWithElementsAsync(projectId).ConfigureAwait(false);
            return Build(project, slides);
        }

        internal static AiProjectContextEnvelope Build(TextProject project, IReadOnlyList<Slide> slides)
        {
            project ??= new TextProject { Name = "未命名项目" };
            slides ??= Array.Empty<Slide>();

            var references = new SortedSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.AppendLine($"项目：{project.Name}");
            sb.AppendLine($"画布：{project.CanvasWidth}x{project.CanvasHeight}");
            sb.AppendLine($"幻灯片数量：{slides.Count}");
            sb.AppendLine();
            sb.AppendLine("幻灯片摘要：");

            foreach (var slide in slides.OrderBy(s => s.SortOrder).ThenBy(s => s.Id))
            {
                string title = string.IsNullOrWhiteSpace(slide.Title) ? $"第{slide.SortOrder + 1}页" : slide.Title.Trim();
                sb.AppendLine($"{slide.SortOrder + 1}. {title}");

                var elementTexts = (slide.Elements ?? Array.Empty<TextElement>())
                    .OrderBy(e => e.ZIndex)
                    .ThenBy(e => e.Id)
                    .Select(ExtractElementText)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Take(8)
                    .ToList();

                foreach (string text in elementTexts)
                {
                    foreach (Match match in ScriptureReferenceRegex.Matches(text))
                    {
                        references.Add(match.Value.Trim());
                    }
                    sb.AppendLine($"   - {TrimForContext(text, 160)}");
                }
            }

            var orderedSlides = slides.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
            string context = TrimForContext(sb.ToString(), 12000);
            return new AiProjectContextEnvelope
            {
                ProjectId = project.Id,
                ProjectName = project.Name ?? string.Empty,
                ContextText = context,
                RuntimeContextText = BuildRuntimeContext(project, orderedSlides, references),
                ExplicitReferences = references.ToList()
            };
        }

        private static string BuildRuntimeContext(
            TextProject project,
            IReadOnlyList<Slide> orderedSlides,
            IReadOnlyCollection<string> references)
        {
            var sb = new StringBuilder();
            sb.AppendLine("稳定上下文摘要：");
            sb.AppendLine($"项目：{project.Name}");
            sb.AppendLine($"幻灯片数量：{orderedSlides.Count}");

            if (references.Count > 0)
            {
                sb.AppendLine($"显式经文线索：{string.Join("；", references.Take(24))}");
            }

            sb.AppendLine("幻灯片索引：");
            foreach (var slide in orderedSlides)
            {
                string title = string.IsNullOrWhiteSpace(slide.Title) ? $"第{slide.SortOrder + 1}页" : slide.Title.Trim();
                sb.AppendLine($"{slide.SortOrder + 1}. {TrimForContext(title, 80)}");
            }

            var keyTexts = orderedSlides
                .SelectMany(slide => (slide.Elements ?? Array.Empty<TextElement>())
                    .OrderBy(e => e.ZIndex)
                    .ThenBy(e => e.Id)
                    .Select(ExtractElementText)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Take(2)
                    .Select(text => $"{slide.SortOrder + 1}. {TrimForContext(text, 120)}"))
                .Take(24)
                .ToList();

            if (keyTexts.Count > 0)
            {
                sb.AppendLine("关键文本摘录：");
                foreach (string text in keyTexts)
                {
                    sb.AppendLine($"- {text}");
                }
            }

            return TrimForContext(sb.ToString(), 4000);
        }

        private static string ExtractElementText(TextElement element)
        {
            if (element == null)
            {
                return string.Empty;
            }

            if (element.RichTextSpans != null && element.RichTextSpans.Count > 0)
            {
                string richText = string.Concat(element.RichTextSpans
                    .OrderBy(s => s.ParagraphIndex ?? 0)
                    .ThenBy(s => s.RunIndex ?? s.SpanOrder)
                    .ThenBy(s => s.SpanOrder)
                    .Select(s => s.Text ?? string.Empty));
                if (!string.IsNullOrWhiteSpace(richText))
                {
                    return NormalizeWhitespace(richText);
                }
            }

            return NormalizeWhitespace(element.Content);
        }

        private static string NormalizeWhitespace(string text)
        {
            return Regex.Replace((text ?? string.Empty).Trim(), @"\s+", " ");
        }

        private static string TrimForContext(string text, int maxLength)
        {
            string value = text ?? string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "\n...[已截断]";
        }
    }
}
