using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;
using Xunit;

namespace Canvas.TextEditor.Tests.Core
{
    public class ImageProcessorCacheTests
    {
        [Fact]
        public void PreRenderUsesActualAspectFitDimensionsAndFrozenPixels() => OnSta(() =>
        {
            WithImage((processor, path, rawKey, bitmap) =>
            {
                Assert.True(processor.PreRenderImage(path, 100, 100, false));
                var cache = RenderCache(processor);
                Assert.True(cache.TryGetValue(rawKey + "|100x50|normal", out var photo));
                Assert.Equal(100, photo.PixelWidth); Assert.Equal(50, photo.PixelHeight); Assert.True(photo.IsFrozen);
                Assert.False(cache.ContainsKey(rawKey + "|100x100|normal"));
            });
        });

        [Fact]
        public void SamePathOverwriteCannotUseOldDecodedVersion() => OnSta(() =>
        {
            WithImage((processor, path, rawKey, bitmap) =>
            {
                File.AppendAllText(path, "overwrite");
                Assert.False(processor.PreRenderImage(path, 100, 100, false));
                Assert.Empty(RenderCache(processor));
            });
        });

        [Fact]
        public void InversionUsesPrivatePixelsAndSeparateActualRenderKeys() => OnSta(() =>
        {
            WithImage((processor, path, rawKey, bitmap) =>
            {
                var pixels = bitmap.Bytes;
                Assert.True(processor.PreRenderImage(path, 100, 100, false));
                Assert.True(processor.PreRenderImage(path, 100, 100, true));
                Assert.True(RenderCache(processor).ContainsKey(rawKey + "|100x50|normal"));
                Assert.True(RenderCache(processor).ContainsKey(rawKey + "|100x50|inverted"));
                Assert.Equal(pixels, bitmap.Bytes);
            });
        });

        [Fact]
        public void ClearRemovesPixelsAndAccessTimesAndInvalidTargetsRemainSafe() => OnSta(() =>
        {
            WithImage((processor, path, rawKey, bitmap) =>
            {
                Assert.False(processor.PreRenderImage(path, 0, 100, false));
                Assert.False(processor.PreRenderImage(path + "missing", 100, 100, false));
                Assert.True(processor.PreRenderImage(path, 100, 100, false));
                processor.ClearCache(); Assert.Empty(RenderCache(processor));
                var times = (Dictionary<string, DateTime>)typeof(ImageProcessor).GetField("imageCacheAccessTime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(processor);
                Assert.Empty(times);
                Assert.False(processor.PreRenderImage(path, 100, 100, false));
            });
        });

        [Fact]
        public void ExplicitBackgroundPreRenderAndClearAreThreadSafe() => OnSta(() =>
        {
            WithImage((processor, path, rawKey, bitmap) =>
            {
                var tasks = new Task[8];
                for (int i = 0; i < tasks.Length; i++) tasks[i] = Task.Run(() =>
                {
                    for (int n = 0; n < 30; n++) { processor.PreRenderImage(path, 100+n, 100, n%2==0); processor.ClearCache(); }
                });
                Task.WaitAll(tasks);
                processor.ClearCache(); Assert.Empty(RenderCache(processor));
                Assert.False(processor.PreRenderImage(path, 100, 100, false));
            });
        });

        [Fact]
        public void NavigationRollbackRetainsDecodedIdentityAfterFileDeletion() => OnSta(() =>
        {
            WithImage((processor, path, rawKey, bitmap) =>
            {
                const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
                var identity=typeof(ImageProcessor).GetField("currentImageCacheKey",fields);
                Assert.NotNull(identity);
                typeof(ImageProcessor).GetField("originalImage",fields).SetValue(processor,bitmap);
                typeof(ImageProcessor).GetField("currentImage",fields).SetValue(processor,bitmap);
                typeof(ImageProcessor).GetField("currentImagePath",fields).SetValue(processor,path);
                identity.SetValue(processor,rawKey);
                var restore=processor.CaptureImageRestoreAction();
                identity.SetValue(processor,"next-image-version");
                typeof(ImageProcessor).GetField("currentImagePath",fields).SetValue(processor,"next-image");
                File.Delete(path); restore();
                Assert.True(ReferenceEquals(bitmap,processor.CurrentImage));
                Assert.Equal(path,processor.CurrentImagePath); Assert.Equal(rawKey,identity.GetValue(processor));
                var render=typeof(ImageProcessor).GetMethod("GetOrCreateCachedImage",fields);
                Assert.NotNull(render.Invoke(processor,new object[]{100,50}));
                Assert.True(RenderCache(processor).ContainsKey(rawKey+"|100x50|normal"));
            });
        });

        private static Dictionary<string, BitmapSource> RenderCache(ImageProcessor processor)
            => (Dictionary<string, BitmapSource>)typeof(ImageProcessor).GetField("imageCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(processor);
        private static void WithImage(Action<ImageProcessor, string, string, SKBitmap> action)
        {
            var path = Path.Combine(Path.GetTempPath(), "Canvas-render-cache-" + Guid.NewGuid() + ".png");
            File.WriteAllText(path, "identity fixture; native pixels injected");
            using var bitmap = new SKBitmap(20, 10); bitmap.Erase(SKColors.White);
            using var gpu = new GPUContext();
            using var processor = new ImageProcessor(null, gpu, new ScrollViewer(), new Image(), new Grid());
            var file = new FileInfo(path);
            string rawKey = $"{file.FullName.ToUpperInvariant()}|{file.LastWriteTimeUtc.Ticks}|{file.Length}";
            processor.GetMemoryCache().Set(path, bitmap, new MemoryCacheEntryOptions { Size=1 }); // baseline policy fixture
            processor.GetMemoryCache().Set(rawKey, bitmap, new MemoryCacheEntryOptions { Size=1 });
            try { action(processor, path, rawKey, bitmap); } finally { File.Delete(path); }
        }
        private static void OnSta(Action action)
        {
            Exception error=null; var thread=new Thread(() => { try { action(); } catch(Exception ex) { error=ex; } });
            thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
            if(error!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
