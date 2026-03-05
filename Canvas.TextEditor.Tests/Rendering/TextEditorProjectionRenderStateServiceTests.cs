using System;
using System.Collections.Generic;
using ImageColorChanger.Services.TextEditor.Rendering;
using SkiaSharp;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Rendering
{
    public sealed class TextEditorProjectionRenderStateServiceTests
    {
        [Fact]
        public void ShouldThrottle_RespectsThrottleWindow()
        {
            var service = new TextEditorProjectionRenderStateService();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            Assert.False(service.ShouldThrottle(now, 100));
            Assert.True(service.ShouldThrottle(now.AddMilliseconds(50), 100));
            Assert.False(service.ShouldThrottle(now.AddMilliseconds(101), 100));
        }

        [Fact]
        public void Cache_RoundTripAndClear_Works()
        {
            var service = new TextEditorProjectionRenderStateService();
            using var bitmap = new SKBitmap(2, 2);

            service.UpdateCache("cache-key", bitmap);
            Assert.True(service.TryGetCached("cache-key", out var cached));
            Assert.Same(bitmap, cached);

            service.ClearCache();
            Assert.False(service.TryGetCached("cache-key", out _));
        }

        [Fact]
        public void BuildCanvasCacheKey_IsStable_ForSameInput()
        {
            var service = new TextEditorProjectionRenderStateService();
            var context = new TextEditorProjectionCacheContext
            {
                RegionImagePaths = new Dictionary<int, string> { [0] = "a.png", [1] = "b.png" },
                TextStates = new[]
                {
                    new TextEditorProjectionTextState
                    {
                        Content = "Hello",
                        X = 10,
                        Y = 20,
                        Width = 100,
                        Height = 40,
                        FontSize = 24,
                        FontFamily = "Arial",
                        FontColor = "#FFFFFF",
                        IsBold = false,
                        IsItalic = false,
                        IsUnderline = false,
                        TextAlign = "Left",
                        ZIndex = 3,
                        BorderColor = "#000000",
                        BorderWidth = 0,
                        BorderRadius = 0,
                        BorderOpacity = 0,
                        BackgroundColor = "#000000",
                        BackgroundRadius = 0,
                        BackgroundOpacity = 0,
                        LineSpacing = 1.2,
                        LetterSpacing = 0
                    }
                },
                SplitMode = "Horizontal",
                SplitDisplayMode = "Fill",
                BackgroundColor = "#111111",
                BackgroundImagePath = "bg.png"
            };

            var key1 = service.BuildCanvasCacheKey(context);
            var key2 = service.BuildCanvasCacheKey(context);

            Assert.Equal(key1, key2);
            Assert.False(string.IsNullOrWhiteSpace(key1));
        }

        [Fact]
        public void BuildCanvasCacheKey_Changes_WhenSplitDisplayModeChanges()
        {
            var service = new TextEditorProjectionRenderStateService();
            var baseContext = new TextEditorProjectionCacheContext
            {
                RegionImagePaths = new Dictionary<int, string> { [0] = "a.png" },
                TextStates = Array.Empty<TextEditorProjectionTextState>(),
                SplitMode = "Horizontal",
                SplitDisplayMode = "FitCenter",
                BackgroundColor = "#111111",
                BackgroundImagePath = "bg.png"
            };

            var topContext = new TextEditorProjectionCacheContext
            {
                RegionImagePaths = baseContext.RegionImagePaths,
                TextStates = baseContext.TextStates,
                SplitMode = baseContext.SplitMode,
                SplitDisplayMode = "FitTop",
                BackgroundColor = baseContext.BackgroundColor,
                BackgroundImagePath = baseContext.BackgroundImagePath
            };

            var keyCenter = service.BuildCanvasCacheKey(baseContext);
            var keyTop = service.BuildCanvasCacheKey(topContext);

            Assert.NotEqual(keyCenter, keyTop);
        }

        [Fact]
        public void BuildCanvasCacheKey_Changes_WhenNoticeConfigChanges()
        {
            var service = new TextEditorProjectionRenderStateService();

            var baseContext = new TextEditorProjectionCacheContext
            {
                TextStates = new[]
                {
                    new TextEditorProjectionTextState
                    {
                        Content = "notice",
                        X = 100,
                        Y = 80,
                        Width = 1200,
                        Height = 120,
                        FontSize = 54,
                        FontFamily = "Microsoft YaHei UI",
                        FontColor = "#FFFFFF",
                        TextAlign = "Left",
                        TextVerticalAlign = "Middle",
                        ZIndex = 2,
                        ComponentType = "Notice",
                        ComponentConfigJson = "{\"Direction\":1,\"Speed\":50,\"DurationMinutes\":3,\"AutoClose\":true}"
                    }
                }
            };

            var changedContext = new TextEditorProjectionCacheContext
            {
                TextStates = new[]
                {
                    new TextEditorProjectionTextState
                    {
                        Content = "notice",
                        X = 100,
                        Y = 80,
                        Width = 1200,
                        Height = 120,
                        FontSize = 54,
                        FontFamily = "Microsoft YaHei UI",
                        FontColor = "#FFFFFF",
                        TextAlign = "Left",
                        TextVerticalAlign = "Middle",
                        ZIndex = 2,
                        ComponentType = "Notice",
                        ComponentConfigJson = "{\"Direction\":0,\"Speed\":70,\"DurationMinutes\":5,\"AutoClose\":true}"
                    }
                }
            };

            var key1 = service.BuildCanvasCacheKey(baseContext);
            var key2 = service.BuildCanvasCacheKey(changedContext);

            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void BuildCanvasCacheKey_HandlesGradientSpecWithSeparators()
        {
            var service = new TextEditorProjectionRenderStateService();

            var contextA = new TextEditorProjectionCacheContext
            {
                TextStates = new[]
                {
                    new TextEditorProjectionTextState
                    {
                        Content = "A|B",
                        FontColor = "GRAD|#FF0000|#00FF00|LeftToRight",
                        BackgroundColor = "GRAD|#220000FF|#2200FFFF|TopToBottom",
                        BorderColor = "#FF00FF",
                        Width = 100,
                        Height = 40
                    }
                }
            };

            var contextB = new TextEditorProjectionCacheContext
            {
                TextStates = new[]
                {
                    new TextEditorProjectionTextState
                    {
                        Content = "A|B",
                        FontColor = "GRAD|#FF0000|#00FF00|RightToLeft",
                        BackgroundColor = "GRAD|#220000FF|#2200FFFF|TopToBottom",
                        BorderColor = "#FF00FF",
                        Width = 100,
                        Height = 40
                    }
                }
            };

            var key1 = service.BuildCanvasCacheKey(contextA);
            var key2 = service.BuildCanvasCacheKey(contextA);
            var key3 = service.BuildCanvasCacheKey(contextB);

            Assert.Equal(key1, key2);
            Assert.NotEqual(key1, key3);
        }
    }
}
