using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ImageColorChanger.Core;
using ImageColorChanger.Utils;

namespace ImageColorChanger
{
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// 依赖注入服务提供者
        /// </summary>
        public static IServiceProvider ServiceProvider { get; private set; }

        /// <summary>
        /// 互斥锁，用于防止同一目录下启动多个实例
        /// </summary>
        private static Mutex _instanceMutex;
        private static TextWriterTraceListener _debugFileTraceListener;

        /// <summary>
        /// 应用程序启动
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            StartupPerfLogger.Initialize("App.OnStartup.Begin");
            InitializeDebugFileLogging();

            // 🔧 在 WPF 初始化之前设置 Per-Monitor DPI Awareness
            SetProcessDpiAwareness();
            StartupPerfLogger.Mark("App.DpiAwareness.Configured");

            base.OnStartup(e);
            StartupPerfLogger.Mark("App.BaseOnStartup.Completed");

            // 🔒 检查是否已有实例在同一目录运行
            if (!CheckSingleInstance())
            {
                StartupPerfLogger.Mark("App.SingleInstance.DuplicateDetected");
                System.Windows.MessageBox.Show(
                    "程序已经启动,请勿重复启动！",
                    "咏慕投影",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }
            StartupPerfLogger.Mark("App.SingleInstance.Verified");

            try
            {
                // 配置依赖注入
                var services = new ServiceCollection();
                ConfigureServices(services);
                ServiceProvider = services.BuildServiceProvider();
                StartupPerfLogger.Mark("App.DependencyInjection.Ready");

                // 初始化资源加载器（检测PAK或使用文件系统）
                ResourceLoader.Initialize();
                StartupPerfLogger.Mark("App.ResourceLoader.Initialized");

                // 全局异常处理
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                DispatcherUnhandledException += OnDispatcherUnhandledException;
                StartupPerfLogger.Mark("App.GlobalExceptionHandlers.Registered", $"LogFile={StartupPerfLogger.LogFilePath}");
            }
            catch (Exception ex)
            {
                StartupPerfLogger.Error("App.OnStartup.Failed", ex);
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [FATAL] 应用程序启动失败: {ex.Message}");
                #endif
                System.Windows.MessageBox.Show($"应用程序启动失败：{ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// 配置服务
        /// </summary>
        private void ConfigureServices(IServiceCollection services)
        {
            // 注册所有Canvas Cast服务
            services.AddCanvasCastServices();

            // 注册MainWindow（需要时手动从ServiceProvider获取依赖）
            // services.AddTransient<UI.MainWindow>();
        }

        /// <summary>
        /// 应用程序退出
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            StartupPerfLogger.Mark("App.OnExit");

            // 兜底：确保认证状态在应用退出前尽量完成落盘。
            try
            {
                ServiceProvider?
                    .GetService<ImageColorChanger.Services.Interfaces.IAuthService>()?
                    .FlushAuthStateAsync()
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // 忽略flush异常，不阻断退出
            }

            if (_debugFileTraceListener != null)
            {
                Trace.Listeners.Remove(_debugFileTraceListener);
                _debugFileTraceListener.Flush();
                _debugFileTraceListener.Close();
                _debugFileTraceListener = null;
            }

            // 释放互斥锁
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
            
            base.OnExit(e);
        }

        private static void InitializeDebugFileLogging()
        {
            try
            {
                if (_debugFileTraceListener != null)
                {
                    return;
                }

                string debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug");
                Directory.CreateDirectory(debugDir);

                string logFilePath = Path.Combine(debugDir, $"debug-{DateTime.Now:yyyyMMdd}.log");
                var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                _debugFileTraceListener = new TextWriterTraceListener(writer, "DebugFileListener");

                Trace.Listeners.Add(_debugFileTraceListener);
                Trace.AutoFlush = true;

                Trace.WriteLine($"✅ [日志] Debug日志已写入: {logFilePath}");
            }
            catch
            {
            }
        }
        
        /// <summary>
        /// 检查单实例（基于当前目录）
        /// </summary>
        /// <returns>如果是唯一实例返回true，否则返回false</returns>
        private static bool CheckSingleInstance()
        {
            try
            {
                // 获取当前程序所在目录的完整路径
                string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                
                // 生成基于目录路径的唯一互斥锁名称
                string mutexName = GenerateMutexName(currentDirectory);
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔒 [单实例检查] 当前目录: {currentDirectory}");
                System.Diagnostics.Debug.WriteLine($"🔒 [单实例检查] 互斥锁名称: {mutexName}");
                #endif
                
                // 尝试创建互斥锁
                bool createdNew;
                _instanceMutex = new Mutex(true, mutexName, out createdNew);
                
                if (!createdNew)
                {
                    // 互斥锁已存在，说明该目录下已有实例在运行
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"🔒 [单实例检查] 检测到重复实例");
                    #endif
                    return false;
                }
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔒 [单实例检查] 首次启动，创建互斥锁成功");
                #endif
                
                return true;
            }
            catch (Exception 
                #if DEBUG
                ex
                #endif
                )
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔒 [单实例检查] 异常: {ex.Message}");
                #endif
                // 如果出现异常，允许启动（避免因检查失败而无法启动）
                return true;
            }
        }
        
        /// <summary>
        /// 根据目录路径生成唯一的互斥锁名称
        /// </summary>
        private static string GenerateMutexName(string directoryPath)
        {
            // 使用MD5生成目录路径的哈希值，确保互斥锁名称唯一且合法
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(directoryPath.ToLower()));
                string hashString = BitConverter.ToString(hash).Replace("-", "");
                return $"Global\\CanvasCast_{hashString}";
            }
        }

        /// <summary>
        /// 处理未捕获的异常（非UI线程）
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                StartupPerfLogger.Error("AppDomain.UnhandledException", ex);
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [FATAL] 未处理的异常: {ex.Message}\n{ex.StackTrace}");
                #endif
                System.Windows.MessageBox.Show($"发生严重错误：{ex.Message}", 
                    "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);

                // 非UI线程未处理异常通常不可恢复，统一退出避免进入脏状态
                Shutdown();
            }
        }

        /// <summary>
        /// 处理未捕获的异常（UI线程）
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            StartupPerfLogger.Error("Dispatcher.UnhandledException", e.Exception);
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"❌ [ERROR] UI线程未处理的异常: {e.Exception.Message}\n{e.Exception.StackTrace}");
            #endif

            if (IsRecoverableUiException(e.Exception))
            {
                System.Windows.MessageBox.Show($"发生错误：{e.Exception.Message}", 
                    "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
                return;
            }

            System.Windows.MessageBox.Show(
                $"发生严重错误，程序将退出以保护数据。\n\n{e.Exception.Message}",
                "严重错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = false;
            Shutdown();
        }

        /// <summary>
        /// 判断UI异常是否可恢复
        /// </summary>
        private static bool IsRecoverableUiException(Exception ex)
        {
            return ex is InvalidOperationException
                || ex is ArgumentException
                || ex is IOException;
        }

        /// <summary>
        /// 获取服务实例
        /// </summary>
        public static T GetService<T>() where T : class
        {
            return ServiceProvider?.GetService<T>();
        }

        /// <summary>
        /// 获取必需的服务实例（如果不存在会抛出异常）
        /// </summary>
        public static T GetRequiredService<T>() where T : class
        {
            return ServiceProvider?.GetRequiredService<T>();
        }

        /// <summary>
        /// 从MainWindow.xaml的Title属性中提取版本号
        /// </summary>
        /// <returns>版本号字符串，如果提取失败则返回默认值</returns>
        private static string GetVersionFromTitle()
        {
            try
            {
                // 从MainWindow.xaml的Title属性中提取版本号
                // Title格式: "咏慕投影 V5.3.5"
                string title = "咏慕投影 V5.3.5"; // 从MainWindow.xaml中获取的实际Title（显示用中文）

                // 使用正则表达式提取版本号
                var match = Regex.Match(title, @"V(\d+\.\d+\.\d+)");
                if (match.Success)
                {
                    return match.Groups[1].Value; // 返回版本号部分，如 "2.5.8"
                }

                // 如果正则匹配失败，返回默认版本号
                return "3.0.6";
            }
            catch (Exception)
            {
                return "3.0.6"; // 返回默认版本号
            }
        }

        #region DPI Awareness

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(int dpiFlag);

        [DllImport("SHCore.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwareness(PROCESS_DPI_AWARENESS awareness);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private enum PROCESS_DPI_AWARENESS
        {
            Process_DPI_Unaware = 0,
            Process_System_DPI_Aware = 1,
            Process_Per_Monitor_DPI_Aware = 2
        }

        private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

        /// <summary>
        /// 设置进程的 DPI Awareness
        /// </summary>
        private static void SetProcessDpiAwareness()
        {
            try
            {
                if (Environment.OSVersion.Version >= new Version(6, 3, 0)) // Windows 8.1+
                {
                    if (Environment.OSVersion.Version >= new Version(10, 0, 15063)) // Windows 10 Creators Update+
                    {
                        // 尝试设置 Per-Monitor V2（最佳）
                        if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                        {
                            // 回退到 Per-Monitor V1
                            SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.Process_Per_Monitor_DPI_Aware);
                        }
#if DEBUG
                        System.Diagnostics.Debug.WriteLine("✅ [DPI] 已设置为 Per-Monitor DPI Aware V2");
#endif
                    }
                    else
                    {
                        // Windows 8.1 - 10（Creators Update 之前）
                        SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.Process_Per_Monitor_DPI_Aware);
#if DEBUG
                        System.Diagnostics.Debug.WriteLine("✅ [DPI] 已设置为 Per-Monitor DPI Aware");
#endif
                    }
                }
                else
                {
                    // Windows 7 及更早版本
                    SetProcessDPIAware();
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("✅ [DPI] 已设置为 System DPI Aware");
#endif
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [DPI] 设置 DPI Awareness 失败: {ex.Message}");
#else
                _ = ex; // 避免编译警告
#endif
            }
        }

        #endregion
    }
}

