using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Interfaces
{
    public interface IBibleTextRecognitionService
    {
        Task<BibleTextRecognitionResult> ResolveAsync(string text, bool isFinal, CancellationToken cancellationToken);
    }
}
