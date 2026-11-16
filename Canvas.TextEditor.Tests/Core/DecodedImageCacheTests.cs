using ImageColorChanger.Core;
using SkiaSharp;
using Xunit;

public sealed class DecodedImageCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "Canvas-slide-cache-tests", Guid.NewGuid().ToString("N"));
    public DecodedImageCacheTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private string Image(string name, SKColor color, int width = 10, int height = 10)
    {
        var path = Path.Combine(_dir, name);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void UnchangedFileReusesFullResolutionBitmap()
    {
        var path = Image("full.png", SKColors.Red, 200, 100);
        using var cache = new DecodedImageCache();
        using var a = cache.Borrow(path);
        using var b = cache.Borrow(path);
        Assert.True(ReferenceEquals(a.Bitmap, b.Bitmap), "unchanged file must reuse the bitmap instance");
        Assert.Equal(200, b.Bitmap.Width);
        Assert.Equal(100, b.Bitmap.Height);
        Assert.Equal(a.Bitmap.ByteCount, cache.ResidentBytes);
    }

    [Fact]
    public void NormalizedFilePathUsesSameIdentity()
    {
        var path = Image("normalized.png", SKColors.Red);
        using var cache = new DecodedImageCache();
        using var a = cache.Borrow(path);
        using var b = cache.Borrow(Path.Combine(_dir, ".", "normalized.png"));
        Assert.True(ReferenceEquals(a.Bitmap, b.Bitmap), "unchanged file must reuse the bitmap instance");
    }

    [Fact]
    public void MtimeTicksInvalidateSameLengthFile()
    {
        var path = Image("mtime.png", SKColors.Red);
        using var cache = new DecodedImageCache();
        using var a = cache.Borrow(path);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddTicks(100000));
        using var b = cache.Borrow(path);
        Assert.NotSame(a.Bitmap, b.Bitmap);
        Assert.Equal(SKColors.Red, a.Bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void FileLengthInvalidatesEvenIfMtimeIsPreserved()
    {
        var path = Image("length.png", SKColors.Red);
        using var cache = new DecodedImageCache();
        using var a = cache.Borrow(path);
        var time = File.GetLastWriteTimeUtc(path);
        using (var stream = new FileStream(path, FileMode.Append)) stream.WriteByte(0);
        File.SetLastWriteTimeUtc(path, time);
        using var b = cache.Borrow(path);
        Assert.NotSame(a.Bitmap, b.Bitmap);
        Assert.Equal(SKColors.Red, b.Bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void SamePathOverwriteKeepsOldBorrowAliveButNotResident()
    {
        var path = Image("overwrite.png", SKColors.Red);
        using var cache = new DecodedImageCache();
        var old = cache.Borrow(path);
        var oldBitmap = old.Bitmap;
        var time = File.GetLastWriteTimeUtc(path);
        Image("overwrite.png", SKColors.Blue);
        File.SetLastWriteTimeUtc(path, time.AddSeconds(1));
        using var newer = cache.Borrow(path);
        Assert.NotSame(oldBitmap, newer.Bitmap);
        Assert.Equal(SKColors.Blue, newer.Bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, oldBitmap.GetPixel(0, 0));
        old.Dispose();
        Assert.Equal(IntPtr.Zero, oldBitmap.Handle);
    }

    [Fact]
    public void DeletedFileIsNotServedAndEvictedLeaseLivesUntilRelease()
    {
        var path = Image("deleted.png", SKColors.Red);
        using var cache = new DecodedImageCache();
        var lease = cache.Borrow(path);
        var bitmap = lease.Bitmap;
        File.Delete(path);
        Assert.Null(cache.Borrow(path));
        Assert.Equal(0, cache.ResidentBytes);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        lease.Dispose();
        Assert.Equal(IntPtr.Zero, bitmap.Handle);
    }

    [Fact]
    public void InvalidImageDecodeIsNotCachedAndCanRecover()
    {
        var path = Path.Combine(_dir, "invalid.png");
        File.WriteAllText(path, "not an image");
        using var cache = new DecodedImageCache();
        Assert.Null(cache.Borrow(path));
        Assert.Equal(0, cache.ResidentBytes);
        Image("invalid.png", SKColors.Green);
        using var a = cache.Borrow(path);
        using var b = cache.Borrow(path);
        Assert.Equal(SKColors.Green, a.Bitmap.GetPixel(0, 0));
        Assert.True(ReferenceEquals(a.Bitmap, b.Bitmap), "unchanged file must reuse the bitmap instance");
    }

    [Fact]
    public void BudgetCountsDecodedBytesNotCompressedFileLength()
    {
        var path = Image("budget.png", SKColors.Red, 100, 100);
        using var decoded = SKBitmap.Decode(path);
        Assert.True(new FileInfo(path).Length < decoded.ByteCount);
        using var cache = new DecodedImageCache(decoded.ByteCount - 1);
        var lease = cache.Borrow(path);
        var first = lease.Bitmap;
        Assert.Equal(0, cache.ResidentBytes);
        lease.Dispose();
        Assert.Equal(IntPtr.Zero, first.Handle);
        using var second = cache.Borrow(path);
        Assert.NotSame(first, second.Bitmap);
        Assert.Equal(100, second.Bitmap.Width);
    }

    [Fact]
    public void ExactBudgetAdmitsAndDoesNotOverrun()
    {
        var path = Image("exact.png", SKColors.Red);
        using var bitmap = SKBitmap.Decode(path);
        using var cache = new DecodedImageCache(bitmap.ByteCount);
        using var a = cache.Borrow(path);
        using var b = cache.Borrow(path);
        Assert.True(ReferenceEquals(a.Bitmap, b.Bitmap), "unchanged file must reuse the bitmap instance");
        Assert.Equal(bitmap.ByteCount, cache.ResidentBytes);
    }

    [Fact]
    public void LruEvictsLeastRecentBitmapNotRecentlyBorrowedOne()
    {
        var ap = Image("a.png", SKColors.Red);
        var bp = Image("b.png", SKColors.Blue);
        var cp = Image("c.png", SKColors.Green);
        using var bitmap = SKBitmap.Decode(ap);
        using var cache = new DecodedImageCache(bitmap.ByteCount * 2L);
        var a = cache.Borrow(ap); var ab = a.Bitmap; a.Dispose();
        var b = cache.Borrow(bp); var bb = b.Bitmap; b.Dispose();
        using var touchA = cache.Borrow(ap);
        using var c = cache.Borrow(cp);
        Assert.True(ReferenceEquals(ab, touchA.Bitmap), "recently touched file must remain resident");
        Assert.Equal(IntPtr.Zero, bb.Handle);
        Assert.NotEqual(IntPtr.Zero, ab.Handle);
        Assert.Equal(bitmap.ByteCount * 2L, cache.ResidentBytes);
    }

    [Fact]
    public void EvictionWaitsForLastOfTwoLeasesBeforeDisposingNativeBitmap()
    {
        var ap = Image("held.png", SKColors.Red);
        var bp = Image("evicts.png", SKColors.Blue);
        using var bitmap = SKBitmap.Decode(ap);
        using var cache = new DecodedImageCache(bitmap.ByteCount);
        var a = cache.Borrow(ap);
        var anotherA = cache.Borrow(ap);
        var held = a.Bitmap;
        using var b = cache.Borrow(bp);
        Assert.Equal(SKColors.Red, held.GetPixel(0, 0));
        a.Dispose();
        Assert.NotEqual(IntPtr.Zero, held.Handle);
        anotherA.Dispose();
        Assert.Equal(IntPtr.Zero, held.Handle);
    }

    [Fact]
    public void DisposeCacheRetainsActiveLeaseAndReleasesIdleBitmap()
    {
        var ap = Image("active.png", SKColors.Red);
        var bp = Image("idle.png", SKColors.Blue);
        var cache = new DecodedImageCache();
        var active = cache.Borrow(ap);
        var ab = active.Bitmap;
        var idle = cache.Borrow(bp);
        var ib = idle.Bitmap;
        idle.Dispose();
        Assert.NotEqual(IntPtr.Zero, ib.Handle);
        cache.Dispose();
        Assert.Equal(0, cache.ResidentBytes);
        Assert.Equal(IntPtr.Zero, ib.Handle);
        Assert.Equal(SKColors.Red, ab.GetPixel(0, 0));
        active.Dispose();
        Assert.Equal(IntPtr.Zero, ab.Handle);
    }

    [Fact]
    public void DoubleCacheAndLeaseDisposeAreSafe()
    {
        var path = Image("double.png", SKColors.Red);
        var cache = new DecodedImageCache();
        var lease = cache.Borrow(path);
        var bitmap = lease.Bitmap;
        cache.Dispose(); cache.Dispose();
        Parallel.Invoke(lease.Dispose, lease.Dispose);
        Assert.Equal(IntPtr.Zero, bitmap.Handle);
    }

    [Fact]
    public void BorrowAfterDisposeIsRejectedEvenForMissingFile()
    {
        var cache = new DecodedImageCache();
        cache.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cache.Borrow(Path.Combine(_dir, "absent.png")));
    }

    [Fact]
    public void DisposedLeaseCannotExposeBitmap()
    {
        var path = Image("disposed.png", SKColors.Red);
        using var cache = new DecodedImageCache();
        var lease = cache.Borrow(path);
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.Bitmap);
    }

    [Fact]
    public async Task ConcurrentSameFileBorrowIsDeduplicated()
    {
        var path = Image("concurrent.png", SKColors.Red, 300, 200);
        using var cache = new DecodedImageCache();
        using var start = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() => { start.Wait(); return cache.Borrow(path); })).ToArray();
        start.Set();
        var leases = await Task.WhenAll(tasks);
        try
        {
            foreach (var lease in leases) Assert.True(ReferenceEquals(leases[0].Bitmap, lease.Bitmap), "parallel borrows must share one decode");
            using var render = new SKBitmap(192, 108);
            using var canvas = new SKCanvas(render);
            using var image = SKImage.FromBitmap(leases[0].Bitmap);
            canvas.DrawImage(image, new SKRect(0, 0, 192, 108), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            Assert.Equal(SKColors.Red, render.GetPixel(50, 50));
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    [Fact]
    public void ColorEffectCopyDoesNotPolluteCachedOriginal()
    {
        var path = Image("color.png", SKColors.Red);
        using var cache = new DecodedImageCache();
        using var a = cache.Borrow(path);
        using (var changed = a.Bitmap.Copy())
        {
            changed.Erase(SKColors.Yellow);
            Assert.Equal(SKColors.Yellow, changed.GetPixel(0, 0));
        }
        using var original = cache.Borrow(path);
        Assert.True(ReferenceEquals(a.Bitmap, original.Bitmap), "copy must not evict or replace the original");
        Assert.Equal(SKColors.Red, original.Bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void ZeroBudgetWorksWithoutResidentOwnership()
    {
        var path = Image("zero.png", SKColors.Red);
        using var cache = new DecodedImageCache(0);
        var a = cache.Borrow(path);
        var bitmap = a.Bitmap;
        Assert.Equal(0, cache.ResidentBytes);
        a.Dispose();
        Assert.Equal(IntPtr.Zero, bitmap.Handle);
    }

    [Fact]
    public void NegativeBudgetIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => new DecodedImageCache(-1));

    [Fact]
    public void MissingFileDoesNotPopulateCache()
    {
        using var cache = new DecodedImageCache();
        Assert.Null(cache.Borrow(Path.Combine(_dir, "absent.png")));
        Assert.Equal(0, cache.ResidentBytes);
    }

    [Fact]
    public async Task MetadataChangingDuringRealDecodeIsNotAdmitted()
    {
        // 回归夹具在测试内生成，不能依赖开发者个人Temp里的基准JPEG。
        var path = Path.Combine(_dir, "changing.jpg");
        using (var pixels = new SKBitmap(6000, 4000))
        {
            pixels.Erase(SKColors.CornflowerBlue);
            using var image = SKImage.FromBitmap(pixels);
            using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            File.WriteAllBytes(path, jpeg.ToArray());
        }
        using var cache = new DecodedImageCache();
        using var stop = new CancellationTokenSource();
        int writes = 0;
        var writer = Task.Run(() =>
        {
            long ticks = DateTime.UtcNow.Ticks;
            while (!stop.IsCancellationRequested)
            {
                File.SetLastWriteTimeUtc(path, new DateTime(ticks += TimeSpan.TicksPerSecond, DateTimeKind.Utc));
                Interlocked.Increment(ref writes);
                Thread.Sleep(1);
            }
        });
        DecodedImageCache.Lease during = null;
        try
        {
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref writes) >= 3, 5000), "metadata writer must start");
            int before = Volatile.Read(ref writes);
            during = cache.Borrow(path);
            Assert.NotNull(during);
            Assert.Equal(6000, during.Bitmap.Width);
            Assert.True(Volatile.Read(ref writes) > before + 1, "mtime changed during real full-size decode");
            Assert.Equal(0, cache.ResidentBytes);
        }
        catch
        {
            during?.Dispose();
            throw;
        }
        finally
        {
            stop.Cancel();
            await writer;
        }
        var oldBitmap = during.Bitmap;
        try
        {
            using var stable = cache.Borrow(path);
            Assert.Equal(96000000, cache.ResidentBytes);
            Assert.False(ReferenceEquals(oldBitmap, stable.Bitmap), "unstable decode must not become a cached hit");
        }
        finally { during.Dispose(); }
        Assert.Equal(IntPtr.Zero, oldBitmap.Handle);
    }
}
