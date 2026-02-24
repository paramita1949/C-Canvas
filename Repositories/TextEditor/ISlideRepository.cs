using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Repositories.TextEditor
{
    public interface ISlideRepository
    {
        Task<bool> ProjectHasSlidesAsync(int projectId);

        Task<int> GetCountByProjectAsync(int projectId);

        Task<int> GetMaxSortOrderAsync(int projectId);

        Task<Slide> AddAsync(Slide slide);

        Task AddRangeAsync(IEnumerable<Slide> slides);

        Task<Slide> GetByIdAsync(int slideId);

        Task UpdateAsync(Slide slide);

        Task UpdateThumbnailAsync(int slideId, string thumbnailPath);

        Task<List<Slide>> GetByProjectAsync(int projectId);

        Task<List<Slide>> GetByProjectWithElementsAsync(int projectId);

        Task UpdateSortOrdersAsync(IEnumerable<Slide> slides);

        Task ShiftSortOrdersAsync(int projectId, int fromSortOrder, int delta);

        Task DeleteAsync(int slideId);

        Task DeleteByProjectAsync(int projectId);
    }
}
