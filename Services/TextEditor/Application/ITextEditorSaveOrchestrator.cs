using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.TextEditor.Application.Models;

namespace ImageColorChanger.Services.TextEditor.Application
{
    public interface ITextEditorSaveOrchestrator
    {
        Task<TextEditorSaveResult> SaveAsync(TextEditorSaveRequest request, CancellationToken ct = default);
    }
}
