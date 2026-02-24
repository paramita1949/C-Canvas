using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Services.TextEditor.Application
{
    public interface ITextProjectService
    {
        Task<TextProject> CreateProjectAsync(string name, int canvasWidth = 1920, int canvasHeight = 1080);

        Task<TextProject> LoadProjectAsync(int projectId);

        Task<List<TextProject>> GetAllProjectsAsync();

        Task SaveProjectAsync(TextProject project);

        Task DeleteProjectAsync(int projectId);

        Task UpdateBackgroundImageAsync(int projectId, string imagePath);

        Task<bool> ProjectHasSlidesAsync(int projectId);

        Task<int> GetSlideCountAsync(int projectId);

        Task<int> GetMaxSlideSortOrderAsync(int projectId);

        Task<Slide> AddSlideAsync(Slide slide);

        Task AddSlidesAsync(IEnumerable<Slide> slides);

        Task<Slide> GetSlideByIdAsync(int slideId);

        Task UpdateSlideAsync(Slide slide);

        Task UpdateSlideThumbnailAsync(int slideId, string thumbnailPath);

        Task<List<Slide>> GetSlidesByProjectAsync(int projectId);

        Task<List<Slide>> GetSlidesByProjectWithElementsAsync(int projectId);

        Task UpdateSlideSortOrdersAsync(IEnumerable<Slide> slides);

        Task ShiftSlideSortOrdersAsync(int projectId, int fromSortOrder, int delta);

        Task DeleteSlideAsync(int slideId);

        Task DeleteSlidesByProjectAsync(int projectId);

        Task<List<TextElement>> GetElementsBySlideWithRichTextAsync(int slideId);

        Task RebindProjectElementsToSlideAsync(int projectId, int targetSlideId);

        Task<TextElement> AddElementAsync(TextElement element);

        Task UpdateElementAsync(TextElement element);

        Task UpdateElementsAsync(IEnumerable<TextElement> elements);

        Task<RichTextSpan> AddRichTextSpanAsync(RichTextSpan span);

        Task DeleteRichTextSpansByElementIdAsync(int textElementId);

        Task SaveRichTextSpansAsync(int textElementId, List<RichTextSpan> spans);

        Task DeleteElementAsync(int elementId);

        Task DeleteAllElementsAsync(int projectId);

        Task<List<TextElement>> GetElementsByProjectAsync(int projectId);

        Task<MediaFile> GetMediaFileByPathAsync(string path);

        TextElement CloneElement(TextElement source);
    }
}
