using System;
using Microsoft.Extensions.DependencyInjection;

namespace ImageColorChanger.UI.Composition
{
    /// <summary>
    /// MainWindow 统一装配入口（P1-2B）。
    /// 将 MainWindow 对 App.ServiceProvider 的直接依赖收敛为单一组合根。
    /// </summary>
    public sealed class MainWindowComposer
    {
        private readonly IServiceProvider _serviceProvider;

        public MainWindowComposer(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public MainWindowServices Compose()
        {
            return new MainWindowServices(_serviceProvider);
        }

        public static MainWindowComposer CreateDefault()
        {
            if (App.ServiceProvider == null)
            {
                throw new InvalidOperationException("App.ServiceProvider is not initialized.");
            }

            return new MainWindowComposer(App.ServiceProvider);
        }
    }

    /// <summary>
    /// MainWindow 使用的服务解析器包装，避免在分部类里散落 ServiceProvider 访问。
    /// </summary>
    public sealed class MainWindowServices
    {
        private readonly IServiceProvider _serviceProvider;

        public MainWindowServices(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public T GetRequired<T>() where T : class
        {
            return _serviceProvider.GetRequiredService<T>();
        }
    }
}
