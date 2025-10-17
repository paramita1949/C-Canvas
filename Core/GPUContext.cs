using System;
using System.Diagnostics;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// GPU上下文管理器
    /// 负责GPU初始化、状态管理、降级策略
    /// </summary>
    public class GPUContext : IDisposable
    {
        private static GPUContext _instance;
        private static readonly object _lock = new object();

        private GRContext _grContext;
        private bool _isGpuAvailable;
        private bool _isInitialized;
        private string _gpuInfo;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static GPUContext Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new GPUContext();
                        }
                    }
                }
                return _instance;
            }
        }

        private GPUContext()
        {
            Initialize();
        }

        /// <summary>
        /// GPU是否可用
        /// </summary>
        public bool IsGpuAvailable => _isGpuAvailable;

        /// <summary>
        /// GPU信息
        /// </summary>
        public string GpuInfo => _gpuInfo;

        /// <summary>
        /// 获取GPU渲染上下文
        /// </summary>
        public GRContext GetContext() => _grContext;

        /// <summary>
        /// 初始化GPU上下文
        /// </summary>
        private void Initialize()
        {
            if (_isInitialized)
                return;

            try
            {
                Debug.WriteLine("🎮 [GPUContext] 开始初始化GPU加速...");
                Debug.WriteLine("   环境: WPF应用");
                Debug.WriteLine("   说明: WPF默认无OpenGL上下文，GPU加速受限");
                Debug.WriteLine("   策略: 使用CPU高性能优化方案");

                // ⚠️ WPF环境说明：
                // WPF应用默认没有OpenGL渲染上下文，SkiaSharp的GPU加速需要：
                // 1. OpenGL上下文（需要GLControl或自定义窗口）
                // 2. Vulkan后端（需要额外配置）
                // 3. Direct3D（需要SkiaSharp.Views.Desktop特殊配置）
                // 
                // 由于WPF已有自己的GPU加速渲染管线（DirectX），
                // 强制使用SkiaSharp GPU反而可能导致性能下降。
                //
                // 最优方案：使用高度优化的CPU渲染 + WPF自身的GPU合成

                GRGlInterface glInterface = null;
                
                try
                {
                    // 尝试创建OpenGL接口（通常在WPF中会失败）
                    glInterface = GRGlInterface.Create();
                    Debug.WriteLine($"   尝试OpenGL接口: {(glInterface != null ? "成功" : "失败")}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"   OpenGL接口创建异常: {ex.GetType().Name}");
                }

                if (glInterface != null && glInterface.Validate())
                {
                    try
                    {
                        _grContext = GRContext.CreateGl(glInterface);
                        
                        if (_grContext != null)
                        {
                            _isGpuAvailable = true;
                            _gpuInfo = GetGpuInfoString();
                            Debug.WriteLine($"✅ [GPUContext] SkiaSharp GPU加速已启用");
                            Debug.WriteLine($"   GPU信息: {_gpuInfo}");
                            Debug.WriteLine($"   后端: OpenGL");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"   GRContext创建异常: {ex.Message}");
                    }
                }

                // 降级到CPU高性能模式
                Debug.WriteLine("ℹ️ [GPUContext] 使用CPU高性能模式");
                Debug.WriteLine("   优势: CPU ScalePixels已高度优化（SIMD并行）");
                Debug.WriteLine("   优势: WPF自动使用GPU合成渲染结果");
                Debug.WriteLine("   优势: 避免CPU↔GPU数据传输开销");
                FallbackToCpu();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ [GPUContext] GPU初始化异常: {ex.Message}");
                FallbackToCpu();
            }
            finally
            {
                _isInitialized = true;
            }
        }

        /// <summary>
        /// 降级到CPU模式
        /// </summary>
        private void FallbackToCpu()
        {
            _isGpuAvailable = false;
            _gpuInfo = "CPU高性能模式（SIMD优化 + WPF GPU合成）";
            Debug.WriteLine("✅ [GPUContext] CPU高性能模式已就绪");
        }

        /// <summary>
        /// 获取GPU信息字符串
        /// </summary>
        private string GetGpuInfoString()
        {
            try
            {
                if (_grContext == null)
                    return "未知";

                return $"SkiaSharp GPU (Backend: {_grContext.Backend})";
            }
            catch
            {
                return "GPU已启用";
            }
        }

        /// <summary>
        /// 使用GPU缩放图片
        /// </summary>
        public SKBitmap ScaleImageGpu(SKBitmap source, int targetWidth, int targetHeight, SKFilterQuality quality = SKFilterQuality.High)
        {
            if (source == null || targetWidth <= 0 || targetHeight <= 0)
                return null;

            // GPU不可用，降级到CPU
            if (!_isGpuAvailable || _grContext == null)
            {
                return ScaleImageCpu(source, targetWidth, targetHeight, quality);
            }

            try
            {
                var sw = Stopwatch.StartNew();

                // 创建GPU表面
                var info = new SKImageInfo(targetWidth, targetHeight, source.ColorType, source.AlphaType);
                using var surface = SKSurface.Create(_grContext, false, info);

                if (surface == null)
                {
                    Debug.WriteLine("⚠️ [GPUContext] GPU表面创建失败，降级到CPU");
                    return ScaleImageCpu(source, targetWidth, targetHeight, quality);
                }

                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                // 使用GPU绘制缩放后的图片
                var paint = new SKPaint
                {
                    FilterQuality = quality,
                    IsAntialias = true
                };

                var destRect = new SKRect(0, 0, targetWidth, targetHeight);
                canvas.DrawBitmap(source, destRect, paint);
                canvas.Flush();

                // 从GPU读取结果
                var image = surface.Snapshot();
                var result = SKBitmap.FromImage(image);

                sw.Stop();
                Debug.WriteLine($"🎮 [GPUContext] GPU缩放完成: {source.Width}x{source.Height} -> {targetWidth}x{targetHeight}, 耗时: {sw.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ [GPUContext] GPU缩放失败: {ex.Message}，降级到CPU");
                return ScaleImageCpu(source, targetWidth, targetHeight, quality);
            }
        }

        /// <summary>
        /// CPU高性能缩放（优化方案）
        /// </summary>
        private SKBitmap ScaleImageCpu(SKBitmap source, int targetWidth, int targetHeight, SKFilterQuality quality)
        {
            try
            {
                var sw = Stopwatch.StartNew();

                // 创建目标位图
                var info = new SKImageInfo(targetWidth, targetHeight, source.ColorType, source.AlphaType);
                var result = new SKBitmap(info);

                // 🚀 使用高性能缩放
                // ScalePixels内部使用SIMD指令（SSE2/AVX）并行处理像素
                // 这是SkiaSharp在CPU上的最优方案
                source.ScalePixels(result, quality);

                sw.Stop();
                
                // 只在耗时较长时输出日志（减少日志噪音）
                if (sw.ElapsedMilliseconds > 10)
                {
                    Debug.WriteLine($"⚡ [CPU] 缩放: {source.Width}x{source.Height} -> {targetWidth}x{targetHeight}, 耗时: {sw.ElapsedMilliseconds}ms");
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ [GPUContext] CPU缩放失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 释放GPU资源
        /// </summary>
        public void Dispose()
        {
            if (_grContext != null)
            {
                Debug.WriteLine("🎮 [GPUContext] 释放GPU上下文");
                _grContext.Dispose();
                _grContext = null;
            }
        }

        /// <summary>
        /// 重置GPU上下文（用于错误恢复）
        /// </summary>
        public void Reset()
        {
            Debug.WriteLine("🔄 [GPUContext] 重置GPU上下文");
            _isInitialized = false;
            
            _grContext?.Dispose();
            _grContext = null;
            
            Initialize();
        }

        /// <summary>
        /// 刷新GPU命令队列
        /// </summary>
        public void Flush()
        {
            try
            {
                _grContext?.Flush();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ [GPUContext] Flush失败: {ex.Message}");
            }
        }
    }
}

