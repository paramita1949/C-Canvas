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
                        .Select(kv => $"{kv.Key}:{EncodePart(kv.Value)}"));

            var textPart = context.TextStates == null
                ? string.Empty
                : string.Join(
                    "|",
                    context.TextStates.Select(BuildTextStateSignature));

            return string.Join(
                "#",
                EncodePart(imagePart),
                EncodePart(textPart),
                EncodePart(context.SplitMode),
                EncodePart(context.SplitDisplayMode),
                EncodePart(context.BackgroundColor),
                EncodePart(context.BackgroundImagePath),
                context.BackgroundGradientEnabled ? "1" : "0",
                EncodePart(context.BackgroundGradientStartColor),
                EncodePart(context.BackgroundGradientEndColor),
                context.BackgroundGradientDirection.ToString(CultureInfo.InvariantCulture),
                context.BackgroundOpacity.ToString(CultureInfo.InvariantCulture),
                context.BiblePopupOverlayVisible ? "1" : "0",
                EncodePart(context.BiblePopupOverlayReference),
                EncodePart(context.BiblePopupOverlayContent),
                EncodePart(context.BiblePopupOverlayPosition),
                EncodePart(context.BiblePopupOverlayBackgroundColor),
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
                EncodePart(state.Content),
                EncodePart(FormatInvariant(state.X)),
                EncodePart(FormatInvariant(state.Y)),
                EncodePart(FormatInvariant(state.Width)),
                EncodePart(FormatInvariant(state.Height)),
                EncodePart(FormatInvariant(state.FontSize)),
                EncodePart(state.FontFamily),
                EncodePart(state.FontColor),
                state.IsBold ? "1" : "0",
                state.IsItalic ? "1" : "0",
                state.IsUnderline ? "1" : "0",
                EncodePart(state.TextAlign),
                EncodePart(state.TextVerticalAlign),
                state.ZIndex.ToString(CultureInfo.InvariantCulture),
                EncodePart(state.BorderColor),
                EncodePart(FormatInvariant(state.BorderWidth)),
                EncodePart(FormatInvariant(state.BorderRadius)),
                EncodePart(FormatInvariant(state.BorderOpacity)),
                EncodePart(state.BackgroundColor),
                EncodePart(FormatInvariant(state.BackgroundRadius)),
                EncodePart(FormatInvariant(state.BackgroundOpacity)),
                EncodePart(FormatInvariant(state.LineSpacing)),
                EncodePart(FormatInvariant(state.LetterSpacing)),
                EncodePart(state.ComponentType),
                EncodePart(state.ComponentConfigJson));
        }

        private static string FormatInvariant(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string EncodePart(string value)
        {
            var safe = value ?? string.Empty;
            return $"{safe.Length}:{safe}";
        }
    }
}
