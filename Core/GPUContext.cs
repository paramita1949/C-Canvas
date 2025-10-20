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
                //Debug.WriteLine("🎮 [GPUContext] 开始初始化GPU加速...");
                //Debug.WriteLine("   环境: WPF应用");
                //Debug.WriteLine("   说明: WPF默认无OpenGL上下文，GPU加速受限");
                //Debug.WriteLine("   策略: 使用CPU高性能优化方案");

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
                    //Debug.WriteLine($"   尝试OpenGL接口: {(glInterface != null ? "成功" : "失败")}");
                }
                catch (Exception)
                {
                    //Debug.WriteLine($"   OpenGL接口创建异常: {ex.GetType().Name}");
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
                            //Debug.WriteLine($"✅ [GPUContext] SkiaSharp GPU加速已启用");
                            //Debug.WriteLine($"   GPU信息: {_gpuInfo}");
                            //Debug.WriteLine($"   后端: OpenGL");
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        //Debug.WriteLine($"   GRContext创建异常: {ex.Message}");
                    }
                }

                // 降级到CPU高性能模式
                //Debug.WriteLine("ℹ️ [GPUContext] 使用CPU高性能模式");
                //Debug.WriteLine("   优势: CPU ScalePixels已高度优化（SIMD并行）");
                //Debug.WriteLine("   优势: WPF自动使用GPU合成渲染结果");
                //Debug.WriteLine("   优势: 避免CPU↔GPU数据传输开销");
                FallbackToCpu();
            }
            catch (Exception)
            {
                //Debug.WriteLine($"❌ [GPUContext] GPU初始化异常: {ex.Message}");
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
            //Debug.WriteLine("✅ [GPUContext] CPU高性能模式已就绪");
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
#if DEBUG
                    Debug.WriteLine("⚠️ [GPUContext] GPU表面创建失败，降级到CPU");
#endif
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
#if DEBUG
                Debug.WriteLine($"🎮 [GPUContext] GPU缩放完成: {source.Width}x{source.Height} -> {targetWidth}x{targetHeight}, 耗时: {sw.ElapsedMilliseconds}ms");
#endif

                return result;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"❌ [GPUContext] GPU缩放失败: {ex.Message}，降级到CPU");
#else
                _ = ex; // 避免未使用变量警告
#endif
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
                
#if DEBUG
                // 只在耗时较长时输出日志（减少日志噪音）
                //if (sw.ElapsedMilliseconds > 10)
                //{
                //    Debug.WriteLine($"⚡ [CPU] 缩放: {source.Width}x{source.Height} -> {targetWidth}x{targetHeight}, 耗时: {sw.ElapsedMilliseconds}ms");
                //}
#endif

                return result;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"❌ [GPUContext] CPU缩放失败: {ex.Message}");
#else
                _ = ex; // 避免未使用变量警告
#endif
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
#if DEBUG
                Debug.WriteLine("🎮 [GPUContext] 释放GPU上下文");
#endif
                _grContext.Dispose();
                _grContext = null;
            }
        }

        /// <summary>
        /// 重置GPU上下文（用于错误恢复）
        /// </summary>
        public void Reset()
        {
#if DEBUG
            Debug.WriteLine("🔄 [GPUContext] 重置GPU上下文");
#endif
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
#if DEBUG
                Debug.WriteLine($"⚠️ [GPUContext] Flush失败: {ex.Message}");
#else
                _ = ex; // 避免未使用变量警告
#endif
            }
        }

        /// <summary>
        /// 验证WPF硬件加速状态
        /// </summary>
        /// <returns>是否完全启用GPU加速</returns>
        public static bool VerifyWPFHardwareAcceleration()
        {
            try
            {
                int renderingTier = (System.Windows.Media.RenderCapability.Tier >> 16);
                
                #if DEBUG
                //Debug.WriteLine($"🎮 [GPU验证] WPF渲染层级: Tier {renderingTier}");
                //Debug.WriteLine($"   Tier 0 = 软件渲染（无GPU）");
                //Debug.WriteLine($"   Tier 1 = 部分GPU加速");
                //Debug.WriteLine($"   Tier 2 = 完全GPU加速 ✅");
                #endif
                
                if (renderingTier < 2)
                {
                    #if DEBUG
                    //Debug.WriteLine($"⚠️ [GPU警告] 当前未完全启用GPU加速！建议检查显卡驱动。");
                    #endif
                    return false;
                }
                
                #if DEBUG
                //Debug.WriteLine($"✅ [GPU验证] WPF硬件加速已完全启用");
                #endif
                return true;
            }
            catch
            {
                #if DEBUG
                //Debug.WriteLine($"❌ [GPU验证] 检测失败");
                #endif
                return false;
            }
        }

        /// <summary>
        /// 强制启用硬件加速（在应用启动时调用）
        /// </summary>
        public static void ForceEnableHardwareAcceleration()
        {
            try
            {
                // 设置进程渲染模式为默认（自动选择最佳模式）
                System.Windows.Media.RenderOptions.ProcessRenderMode = 
                    System.Windows.Interop.RenderMode.Default;
                
                #if DEBUG
                //Debug.WriteLine($"✅ [GPU] 已设置硬件加速为默认模式（自动优化）");
                #endif
            }
            catch
            {
                #if DEBUG
                //Debug.WriteLine($"⚠️ [GPU] 设置硬件加速失败");
                #endif
            }
        }

        /// <summary>
        /// 为UI元素启用GPU缓存（减少重复渲染）
        /// </summary>
        /// <param name="element">要优化的UI元素</param>
        /// <param name="enableHighQuality">是否启用高质量模式</param>
        public static void EnableBitmapCache(System.Windows.UIElement element, bool enableHighQuality = true)
        {
            try
            {
                // 为元素启用位图缓存（GPU会缓存渲染结果）
                element.CacheMode = new System.Windows.Media.BitmapCache
                {
                    EnableClearType = false, // 关闭ClearType以提升性能
                    RenderAtScale = 1.0,
                    SnapsToDevicePixels = true
                };
                
                if (enableHighQuality)
                {
                    // 高质量渲染设置
                    System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                        element, 
                        System.Windows.Media.BitmapScalingMode.HighQuality
                    );
                }
                else
                {
                    // 性能优先设置
                    System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                        element, 
                        System.Windows.Media.BitmapScalingMode.LowQuality
                    );
                }
                
                // 启用边缘抗锯齿
                System.Windows.Media.RenderOptions.SetEdgeMode(
                    element, 
                    System.Windows.Media.EdgeMode.Aliased
                );
                
                #if DEBUG
                //Debug.WriteLine($"✅ [GPU] 已为元素启用位图缓存（高质量={enableHighQuality}）");
                #endif
            }
            catch
            {
                #if DEBUG
                //Debug.WriteLine($"⚠️ [GPU] 启用位图缓存失败");
                #endif
            }
        }

        /// <summary>
        /// 获取GPU性能诊断信息
        /// </summary>
        /// <returns>GPU性能报告</returns>
        public static string GetPerformanceDiagnostics()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== GPU性能诊断 ===");
                
                // WPF渲染层级
                int tier = (System.Windows.Media.RenderCapability.Tier >> 16);
                sb.AppendLine($"WPF渲染层级: Tier {tier}");
                
                // 显卡信息
                sb.AppendLine($"是否支持PixelShader3.0: {System.Windows.Media.RenderCapability.IsPixelShaderVersionSupported(3, 0)}");
                sb.AppendLine($"是否支持PixelShader2.0: {System.Windows.Media.RenderCapability.IsPixelShaderVersionSupported(2, 0)}");
                
                // 最大纹理尺寸
                sb.AppendLine($"最大纹理宽度: {System.Windows.Media.RenderCapability.MaxHardwareTextureSize.Width}");
                sb.AppendLine($"最大纹理高度: {System.Windows.Media.RenderCapability.MaxHardwareTextureSize.Height}");
                
                // SkiaSharp GPU状态
                var instance = GPUContext.Instance;
                sb.AppendLine($"SkiaSharp GPU可用: {instance.IsGpuAvailable}");
                sb.AppendLine($"SkiaSharp GPU信息: {instance.GpuInfo}");
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"GPU诊断失败: {ex.Message}";
            }
        }
    }
}

