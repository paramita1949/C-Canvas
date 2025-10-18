using System;
using System.Windows;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ImageColorChanger.Core;

namespace ImageColorChanger
{
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// 依赖注入服务提供者
        /// </summary>
        public static IServiceProvider ServiceProvider { get; private set; }

        /// <summary>
        /// 应用程序启动
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);


            try
            {
                // 初始化资源加载器（检测PAK或使用文件系统）
                ResourceLoader.Initialize();
                
                // 配置依赖注入
                var services = new ServiceCollection();
                ConfigureServices(services);
                ServiceProvider = services.BuildServiceProvider();

                // 全局异常处理
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                DispatcherUnhandledException += OnDispatcherUnhandledException;
            }
            catch (Exception ex)
            {
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
            base.OnExit(e);
        }

        /// <summary>
        /// 处理未捕获的异常（非UI线程）
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [FATAL] 未处理的异常: {ex.Message}\n{ex.StackTrace}");
                #endif
                System.Windows.MessageBox.Show($"发生严重错误：{ex.Message}", 
                    "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理未捕获的异常（UI线程）
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"❌ [ERROR] UI线程未处理的异常: {e.Exception.Message}\n{e.Exception.StackTrace}");
            #endif
            System.Windows.MessageBox.Show($"发生错误：{e.Exception.Message}", 
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // 标记为已处理，防止应用程序崩溃
            e.Handled = true;
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
                // Title格式: "Canvas Cast V2.5.8"
                string title = "Canvas Cast V3.0.6"; // 从MainWindow.xaml中获取的实际Title
                
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
    }
}

