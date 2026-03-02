using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Repositories.Interfaces;
using ImageColorChanger.Repositories.TextEditor;

namespace ImageColorChanger.Services.TextEditor.Application
{
    public sealed class TextProjectService : ITextProjectService
    {
        private readonly ITextProjectRepository _textProjectRepository;
        private readonly ISlideRepository _slideRepository;
        private readonly ITextElementRepository _textElementRepository;
        private readonly IRichTextSpanRepository _richTextSpanRepository;
        private readonly IMediaFileRepository _mediaFileRepository;

        public TextProjectService(
            ITextProjectRepository textProjectRepository,
            ISlideRepository slideRepository,
            ITextElementRepository textElementRepository,
            IRichTextSpanRepository richTextSpanRepository,
            IMediaFileRepository mediaFileRepository)
        {
            _textProjectRepository = textProjectRepository ?? throw new ArgumentNullException(nameof(textProjectRepository));
            _slideRepository = slideRepository ?? throw new ArgumentNullException(nameof(slideRepository));
            _textElementRepository = textElementRepository ?? throw new ArgumentNullException(nameof(textElementRepository));
            _richTextSpanRepository = richTextSpanRepository ?? throw new ArgumentNullException(nameof(richTextSpanRepository));
            _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        }

        public Task<TextProject> CreateProjectAsync(string name, int canvasWidth = 1920, int canvasHeight = 1080)
            => _textProjectRepository.CreateAsync(name, canvasWidth, canvasHeight);

        public Task<TextProject> LoadProjectAsync(int projectId)
            => _textProjectRepository.LoadWithElementsAndRichTextAsync(projectId);

        public Task<List<TextProject>> GetAllProjectsAsync()
            => _textProjectRepository.GetAllAsync();

        public Task SaveProjectAsync(TextProject project)
            => _textProjectRepository.SaveAsync(project);

        public Task DeleteProjectAsync(int projectId)
            => _textProjectRepository.DeleteAsync(projectId);

        public Task UpdateBackgroundImageAsync(int projectId, string imagePath)
            => _textProjectRepository.UpdateBackgroundImageAsync(projectId, imagePath);

        public Task<bool> ProjectHasSlidesAsync(int projectId)
            => _slideRepository.ProjectHasSlidesAsync(projectId);

        public Task<int> GetSlideCountAsync(int projectId)
            => _slideRepository.GetCountByProjectAsync(projectId);

        public Task<int> GetMaxSlideSortOrderAsync(int projectId)
            => _slideRepository.GetMaxSortOrderAsync(projectId);

        public Task<Slide> AddSlideAsync(Slide slide)
            => _slideRepository.AddAsync(slide);

        public Task AddSlidesAsync(IEnumerable<Slide> slides)
            => _slideRepository.AddRangeAsync(slides);

        public Task<Slide> GetSlideByIdAsync(int slideId)
            => _slideRepository.GetByIdAsync(slideId);

        public Task UpdateSlideAsync(Slide slide)
            => _slideRepository.UpdateAsync(slide);

        public Task UpdateSlideThumbnailAsync(int slideId, string thumbnailPath)
            => _slideRepository.UpdateThumbnailAsync(slideId, thumbnailPath);

        public Task<List<Slide>> GetSlidesByProjectAsync(int projectId)
            => _slideRepository.GetByProjectAsync(projectId);

        public Task<List<Slide>> GetSlidesByProjectWithElementsAsync(int projectId)
            => _slideRepository.GetByProjectWithElementsAsync(projectId);

        public Task UpdateSlideSortOrdersAsync(IEnumerable<Slide> slides)
            => _slideRepository.UpdateSortOrdersAsync(slides);

        public Task ShiftSlideSortOrdersAsync(int projectId, int fromSortOrder, int delta)
            => _slideRepository.ShiftSortOrdersAsync(projectId, fromSortOrder, delta);

        public Task DeleteSlideAsync(int slideId)
            => _slideRepository.DeleteAsync(slideId);

        public Task DeleteSlidesByProjectAsync(int projectId)
            => _slideRepository.DeleteByProjectAsync(projectId);

        public Task<List<TextElement>> GetElementsBySlideWithRichTextAsync(int slideId)
            => _textElementRepository.GetBySlideWithRichTextAsync(slideId);

        public Task RebindProjectElementsToSlideAsync(int projectId, int targetSlideId)
            => _textElementRepository.RebindProjectElementsToSlideAsync(projectId, targetSlideId);

        public Task<TextElement> AddElementAsync(TextElement element)
            => _textElementRepository.AddAsync(element);

        public Task UpdateElementAsync(TextElement element)
            => _textElementRepository.UpdateAsync(element);

        public Task UpdateElementsAsync(IEnumerable<TextElement> elements)
            => _textElementRepository.UpdateRangeAsync(elements);

        public Task<RichTextSpan> AddRichTextSpanAsync(RichTextSpan span)
            => _richTextSpanRepository.AddAsync(span);

        public Task DeleteRichTextSpansByElementIdAsync(int textElementId)
            => _richTextSpanRepository.DeleteByTextElementIdAsync(textElementId);

        public Task SaveRichTextSpansAsync(int textElementId, List<RichTextSpan> spans)
            => _richTextSpanRepository.SaveForTextElementAsync(textElementId, spans);

        public Task DeleteElementAsync(int elementId)
            => _textElementRepository.DeleteAsync(elementId);

        public Task DeleteAllElementsAsync(int projectId)
            => _textElementRepository.DeleteByProjectAsync(projectId);

        public Task<List<TextElement>> GetElementsByProjectAsync(int projectId)
            => _textElementRepository.GetByProjectAsync(projectId);

        public Task<MediaFile> GetMediaFileByPathAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.FromResult<MediaFile>(null);
            }

            return _mediaFileRepository.GetByPathAsync(path);
        }

        public TextElement CloneElement(TextElement source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new TextElement
            {
                ProjectId = source.ProjectId,
                SlideId = source.SlideId,
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height,
                ZIndex = source.ZIndex,
                Content = source.Content,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                FontColor = source.FontColor,
                IsBold = source.IsBold,
                IsUnderline = source.IsUnderline,
                IsItalic = source.IsItalic,
                TextAlign = source.TextAlign,
                TextVerticalAlign = source.TextVerticalAlign,
                BorderColor = source.BorderColor,
                BorderWidth = source.BorderWidth,
                BorderRadius = source.BorderRadius,
                BorderOpacity = source.BorderOpacity,
                BackgroundColor = source.BackgroundColor,
                BackgroundRadius = source.BackgroundRadius,
                BackgroundOpacity = source.BackgroundOpacity,
                ShadowType = source.ShadowType,
                ShadowPreset = source.ShadowPreset,
                ShadowColor = source.ShadowColor,
                ShadowOffsetX = source.ShadowOffsetX,
                ShadowOffsetY = source.ShadowOffsetY,
                ShadowBlur = source.ShadowBlur,
                ShadowOpacity = source.ShadowOpacity,
                LineSpacing = source.LineSpacing,
                LetterSpacing = source.LetterSpacing
            };
        }
    }
}
