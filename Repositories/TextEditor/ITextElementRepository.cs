using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Repositories.TextEditor
{
    public interface ITextElementRepository
    {
        Task<TextElement> AddAsync(TextElement element);

        Task UpdateAsync(TextElement element);

        Task UpdateRangeAsync(IEnumerable<TextElement> elements);

        Task DeleteAsync(int elementId);

        Task DeleteByProjectAsync(int projectId);

        Task<List<TextElement>> GetByProjectAsync(int projectId);

        Task<List<TextElement>> GetBySlideWithRichTextAsync(int slideId);

        Task RebindProjectElementsToSlideAsync(int projectId, int targetSlideId);
    }
}
