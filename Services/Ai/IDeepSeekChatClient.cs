using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Ai
{
    public interface IDeepSeekChatClient
    {
        Task<AiChatStreamResult> StreamChatAsync(
            AiChatRequest request,
            Action<string> onContentDelta,
            CancellationToken cancellationToken);
    }
}
