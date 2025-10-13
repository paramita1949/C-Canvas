using System;
using System.Windows;
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
        /// 应用程序启动
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 初始化日志系统
            Logger.Initialize();
            Logger.Info("========== Canvas Cast 应用程序启动 ==========");
            Logger.Info("版本: {Version}", typeof(App).Assembly.GetName().Version);

            try
            {
                // 配置依赖注入
                var services = new ServiceCollection();
                ConfigureServices(services);
                ServiceProvider = services.BuildServiceProvider();

                Logger.Info("依赖注入配置完成");

                // 全局异常处理
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                DispatcherUnhandledException += OnDispatcherUnhandledException;

                Logger.Info("Canvas Cast 初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, "应用程序启动失败");
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
            Logger.Info("========== Canvas Cast 应用程序退出 ==========");
            Logger.Shutdown();

            base.OnExit(e);
        }

        /// <summary>
        /// 处理未捕获的异常（非UI线程）
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Fatal(ex, "未处理的异常");
                System.Windows.MessageBox.Show($"发生严重错误：{ex.Message}\n\n请查看日志文件获取详细信息。", 
                    "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理未捕获的异常（UI线程）
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "UI线程未处理的异常");
            System.Windows.MessageBox.Show($"发生错误：{e.Exception.Message}\n\n请查看日志文件获取详细信息。", 
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
    }
}

