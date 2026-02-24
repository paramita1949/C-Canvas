using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Repositories.TextEditor
{
    public interface IRichTextSpanRepository
    {
        Task<RichTextSpan> AddAsync(RichTextSpan span);

        Task DeleteByTextElementIdAsync(int textElementId);

        Task SaveForTextElementAsync(int textElementId, IEnumerable<RichTextSpan> spans);

        Task<List<RichTextSpan>> GetByTextElementIdAsync(int textElementId);
    }
}
