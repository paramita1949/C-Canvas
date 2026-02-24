using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.TextEditor.Models;

namespace ImageColorChanger.Services.TextEditor
{
    public interface ITextElementPersistenceService
    {
        Task SaveAsync(IReadOnlyCollection<TextBoxSnapshot> snapshots, CancellationToken cancellationToken = default);
    }
}
