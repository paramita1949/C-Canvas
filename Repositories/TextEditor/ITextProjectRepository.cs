using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Repositories.TextEditor
{
    public interface ITextProjectRepository
    {
        Task<TextProject> CreateAsync(string name, int canvasWidth = 1920, int canvasHeight = 1080);

        Task<TextProject> LoadWithElementsAndRichTextAsync(int projectId);

        Task<TextProject> GetByIdAsync(int projectId);

        Task<List<TextProject>> GetAllAsync();

        Task SaveAsync(TextProject project);

        Task DeleteAsync(int projectId);

        Task UpdateBackgroundImageAsync(int projectId, string imagePath);

        Task UpdateModifiedTimeAsync(int projectId);
    }
}
