using System;
using SkiaSharp;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public interface ITextEditorProjectionRenderStateService
    {
        bool ShouldThrottle(DateTime now, int throttleMs);

        string BuildCanvasCacheKey(TextEditorProjectionCacheContext context);

        bool TryGetCached(string cacheKey, out SKBitmap cachedBitmap);

        void UpdateCache(string cacheKey, SKBitmap renderedBitmap);

        void ClearCache();
    }
}
