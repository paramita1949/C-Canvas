using System;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public sealed class TextEditorProjectionRenderStateService : ITextEditorProjectionRenderStateService
    {
        private readonly object _syncRoot = new();
        private SKBitmap _lastCanvasRenderCache;
        private string _lastCanvasCacheKey = string.Empty;
        private DateTime _lastCanvasUpdateTime = DateTime.MinValue;

        public bool ShouldThrottle(DateTime now, int throttleMs)
        {
            lock (_syncRoot)
            {
                if ((now - _lastCanvasUpdateTime).TotalMilliseconds < throttleMs)
                {
                    return true;
                }

                _lastCanvasUpdateTime = now;
                return false;
            }
        }

        public string BuildCanvasCacheKey(TextEditorProjectionCacheContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var imagePart = context.RegionImagePaths == null
                ? string.Empty
                : string.Join(
                    "|",
                    context.RegionImagePaths
                        .OrderBy(kv => kv.Key)
                        .Select(kv => $"{kv.Key}:{kv.Value}"));

            var textPart = context.TextStates == null
                ? string.Empty
                : string.Join(
                    "|",
                    context.TextStates.Select(BuildTextStateSignature));

            return string.Join(
                "#",
                imagePart,
                textPart,
                context.SplitMode ?? string.Empty,
                context.SplitDisplayMode ?? string.Empty,
                context.BackgroundColor ?? string.Empty,
                context.BackgroundImagePath ?? string.Empty,
                context.BiblePopupOverlayVisible ? "1" : "0",
                context.BiblePopupOverlayReference ?? string.Empty,
                context.BiblePopupOverlayContent ?? string.Empty,
                context.BiblePopupOverlayPosition ?? string.Empty,
                context.BiblePopupOverlayBackgroundColor ?? string.Empty,
                context.BiblePopupOverlayBackgroundOpacity.ToString(CultureInfo.InvariantCulture),
                context.BiblePopupOverlayScrollOffset.ToString("G17", CultureInfo.InvariantCulture));
        }

        public bool TryGetCached(string cacheKey, out SKBitmap cachedBitmap)
        {
            lock (_syncRoot)
            {
                var hit = string.Equals(cacheKey, _lastCanvasCacheKey, StringComparison.Ordinal)
                    && _lastCanvasRenderCache != null;
                cachedBitmap = hit ? _lastCanvasRenderCache : null;
                return hit;
            }
        }

        public void UpdateCache(string cacheKey, SKBitmap renderedBitmap)
        {
            if (renderedBitmap == null)
            {
                return;
            }

            lock (_syncRoot)
            {
                if (!ReferenceEquals(_lastCanvasRenderCache, renderedBitmap))
                {
                    _lastCanvasRenderCache?.Dispose();
                }

                _lastCanvasRenderCache = renderedBitmap;
                _lastCanvasCacheKey = cacheKey ?? string.Empty;
            }
        }

        public void ClearCache()
        {
            lock (_syncRoot)
            {
                _lastCanvasRenderCache?.Dispose();
                _lastCanvasRenderCache = null;
                _lastCanvasCacheKey = string.Empty;
                _lastCanvasUpdateTime = DateTime.MinValue;
            }
        }

        private static string BuildTextStateSignature(TextEditorProjectionTextState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            return string.Join(
                "_",
                state.Content ?? string.Empty,
                FormatInvariant(state.X),
                FormatInvariant(state.Y),
                FormatInvariant(state.Width),
                FormatInvariant(state.Height),
                FormatInvariant(state.FontSize),
                state.FontFamily ?? string.Empty,
                state.FontColor ?? string.Empty,
                state.IsBold ? "1" : "0",
                state.IsItalic ? "1" : "0",
                state.IsUnderline ? "1" : "0",
                state.TextAlign ?? string.Empty,
                state.ZIndex.ToString(CultureInfo.InvariantCulture),
                state.BorderColor ?? string.Empty,
                FormatInvariant(state.BorderWidth),
                FormatInvariant(state.BorderRadius),
                FormatInvariant(state.BorderOpacity),
                state.BackgroundColor ?? string.Empty,
                FormatInvariant(state.BackgroundRadius),
                FormatInvariant(state.BackgroundOpacity),
                FormatInvariant(state.LineSpacing),
                FormatInvariant(state.LetterSpacing));
        }

        private static string FormatInvariant(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }
    }
}
