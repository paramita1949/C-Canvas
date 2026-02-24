using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Repositories.TextEditor;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 文本项目管理器（兼容层）。
    /// 新代码请优先使用 Repositories.TextEditor 与应用层 orchestrator。
    /// </summary>
    [Obsolete("TextProjectManager is a compatibility facade. Prefer Repositories.TextEditor + application services.")]
    public class TextProjectManager
    {
        private readonly CanvasDbContext _dbContext;
        private readonly ITextProjectRepository _textProjectRepository;
        private readonly ISlideRepository _slideRepository;
        private readonly ITextElementRepository _textElementRepository;
        private readonly IRichTextSpanRepository _richTextSpanRepository;

        public TextProjectManager(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _textProjectRepository = new EfTextProjectRepository(_dbContext);
            _slideRepository = new EfSlideRepository(_dbContext);
            _textElementRepository = new EfTextElementRepository(_dbContext);
            _richTextSpanRepository = new EfRichTextSpanRepository(_dbContext);
        }

        #region 项目管理

        public Task<TextProject> CreateProjectAsync(string name, int canvasWidth = 1920, int canvasHeight = 1080)
        {
            return _textProjectRepository.CreateAsync(name, canvasWidth, canvasHeight);
        }

        public Task<TextProject> LoadProjectAsync(int projectId)
        {
            return _textProjectRepository.LoadWithElementsAndRichTextAsync(projectId);
        }

        public Task<List<TextProject>> GetAllProjectsAsync()
        {
            return _textProjectRepository.GetAllAsync();
        }

        public Task SaveProjectAsync(TextProject project)
        {
            return _textProjectRepository.SaveAsync(project);
        }

        public Task DeleteProjectAsync(int projectId)
        {
            return _textProjectRepository.DeleteAsync(projectId);
        }

        public Task UpdateBackgroundImageAsync(int projectId, string imagePath)
        {
            return _textProjectRepository.UpdateBackgroundImageAsync(projectId, imagePath);
        }

        #endregion

        #region 幻灯片管理

        public Task<bool> ProjectHasSlidesAsync(int projectId)
        {
            return _slideRepository.ProjectHasSlidesAsync(projectId);
        }

        public Task<int> GetSlideCountAsync(int projectId)
        {
            return _slideRepository.GetCountByProjectAsync(projectId);
        }

        public Task<int> GetMaxSlideSortOrderAsync(int projectId)
        {
            return _slideRepository.GetMaxSortOrderAsync(projectId);
        }

        public Task<Slide> AddSlideAsync(Slide slide)
        {
            return _slideRepository.AddAsync(slide);
        }

        public Task AddSlidesAsync(IEnumerable<Slide> slides)
        {
            return _slideRepository.AddRangeAsync(slides);
        }

        public Task<Slide> GetSlideByIdAsync(int slideId)
        {
            return _slideRepository.GetByIdAsync(slideId);
        }

        public Task UpdateSlideAsync(Slide slide)
        {
            return _slideRepository.UpdateAsync(slide);
        }

        public Task UpdateSlideThumbnailAsync(int slideId, string thumbnailPath)
        {
            return _slideRepository.UpdateThumbnailAsync(slideId, thumbnailPath);
        }

        public Task<List<Slide>> GetSlidesByProjectAsync(int projectId)
        {
            return _slideRepository.GetByProjectAsync(projectId);
        }

        public Task<List<Slide>> GetSlidesByProjectWithElementsAsync(int projectId)
        {
            return _slideRepository.GetByProjectWithElementsAsync(projectId);
        }

        public Task UpdateSlideSortOrdersAsync(IEnumerable<Slide> slides)
        {
            return _slideRepository.UpdateSortOrdersAsync(slides);
        }

        public Task ShiftSlideSortOrdersAsync(int projectId, int fromSortOrder, int delta)
        {
            return _slideRepository.ShiftSortOrdersAsync(projectId, fromSortOrder, delta);
        }

        public Task DeleteSlideAsync(int slideId)
        {
            return _slideRepository.DeleteAsync(slideId);
        }

        public Task DeleteSlidesByProjectAsync(int projectId)
        {
            return _slideRepository.DeleteByProjectAsync(projectId);
        }

        public Task<List<TextElement>> GetElementsBySlideWithRichTextAsync(int slideId)
        {
            return _textElementRepository.GetBySlideWithRichTextAsync(slideId);
        }

        public Task RebindProjectElementsToSlideAsync(int projectId, int targetSlideId)
        {
            return _textElementRepository.RebindProjectElementsToSlideAsync(projectId, targetSlideId);
        }

        #endregion

        #region 元素管理

        public Task<TextElement> AddElementAsync(TextElement element)
        {
            return _textElementRepository.AddAsync(element);
        }

        public Task UpdateElementAsync(TextElement element)
        {
            return _textElementRepository.UpdateAsync(element);
        }

        public Task UpdateElementsAsync(IEnumerable<TextElement> elements)
        {
            return _textElementRepository.UpdateRangeAsync(elements);
        }

        public Task<RichTextSpan> AddRichTextSpanAsync(RichTextSpan span)
        {
            return _richTextSpanRepository.AddAsync(span);
        }

        public Task DeleteRichTextSpansByElementIdAsync(int textElementId)
        {
            return _richTextSpanRepository.DeleteByTextElementIdAsync(textElementId);
        }

        public Task SaveRichTextSpansAsync(int textElementId, List<RichTextSpan> spans)
        {
            return _richTextSpanRepository.SaveForTextElementAsync(textElementId, spans);
        }

        public Task DeleteElementAsync(int elementId)
        {
            return _textElementRepository.DeleteAsync(elementId);
        }

        public Task DeleteAllElementsAsync(int projectId)
        {
            return _textElementRepository.DeleteByProjectAsync(projectId);
        }

        public Task<List<TextElement>> GetElementsByProjectAsync(int projectId)
        {
            return _textElementRepository.GetByProjectAsync(projectId);
        }

        #endregion

        #region 辅助方法

        public async Task<MediaFile> GetMediaFileByPathAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return await _dbContext.MediaFiles.FirstOrDefaultAsync(m => m.Path == path);
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

        #endregion
    }
}
