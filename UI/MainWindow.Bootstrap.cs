using System;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 启动初始化相关
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 初始化FPS监控器
        /// </summary>
        private void InitializeFpsMonitor()
        {
            try
            {
                _fpsMonitor = new Utils.RealTimeFpsMonitor(this);
                // 默认不开启监控，只在滚动时开启
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ FPS监控器初始化失败: {ex.Message}");
            }
        }

        private void InitializeGpuProcessor()
        {
            // 初始化GPU上下文（自动检测GPU可用性）
            var gpuContext = Core.GPUContext.Instance;

            // 强制启用WPF硬件加速
            Core.GPUContext.ForceEnableHardwareAcceleration();

            // 验证WPF GPU加速状态
            bool isWpfGpuEnabled = Core.GPUContext.VerifyWPFHardwareAcceleration();

#if DEBUG
            //System.Diagnostics.Debug.WriteLine("========================================");
            //System.Diagnostics.Debug.WriteLine($"SkiaSharp GPU状态: {(gpuContext.IsGpuAvailable ? "已启用" : "CPU模式")}");
            //System.Diagnostics.Debug.WriteLine($"SkiaSharp GPU信息: {gpuContext.GpuInfo}");
            //System.Diagnostics.Debug.WriteLine($"WPF GPU加速状态: {(isWpfGpuEnabled ? "Tier 2完全启用" : "未完全启用")}");
            //System.Diagnostics.Debug.WriteLine("========================================");
#endif

            // 如果WPF GPU未启用，显示警告（仅在Release模式）
#if !DEBUG
            if (!isWpfGpuEnabled)
            {
                System.Windows.MessageBox.Show(
                    "警告：检测到GPU硬件加速未完全启用，可能影响滚动性能。\n\n建议：\n1. 更新显卡驱动\n2. 检查系统设置中的图形性能选项",
                    "性能警告",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning
                );
            }
#endif

            // 在UI显示GPU状态
            Dispatcher.InvokeAsync(() =>
            {
                if (gpuContext.IsGpuAvailable)
                {
                    ShowStatus($"🎮 GPU加速已启用 - {gpuContext.GpuInfo}");
                }
                else
                {
                    ShowStatus("⚠️ GPU不可用，已降级到CPU渲染");
                }
            });
        }
    }
}
