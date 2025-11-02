using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using ImageColorChanger.Database;
using ImageColorChanger.Managers.Keyframes;
using ImageColorChanger.Services.StateMachine;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 依赖注入服务注册扩展
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册所有Canvas Cast服务
        /// </summary>
        public static IServiceCollection AddCanvasCastServices(this IServiceCollection services)
        {
            // ========== 数据库服务 ==========
            services.AddDatabase();

            // ========== Repository层 ==========
            services.AddRepositories();

            // ========== 核心服务层 ==========
            services.AddCoreServices();

            // ========== Manager层 ==========
            services.AddManagers();

            // ========== ViewModel层 ==========
            services.AddViewModels();

            return services;
        }

        /// <summary>
        /// 注册数据库服务
        /// </summary>
        private static IServiceCollection AddDatabase(this IServiceCollection services)
        {
            // 使用与主程序相同的数据库路径（主程序目录/pyimages.db）
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");

            // 注册DbContext（Scoped生命周期，避免并发问题）
            services.AddScoped(provider => 
            {
                var context = new CanvasDbContext(dbPath);
                // 初始化数据库（确保表结构存在）
                context.InitializeDatabase();
                return context;
            });

            // 注册数据库管理器（Singleton，构造函数会自动初始化数据库）
            services.AddSingleton<DatabaseManager>(provider => new DatabaseManager(dbPath));

            return services;
        }

        /// <summary>
        /// 注册Repository层
        /// </summary>
        private static IServiceCollection AddRepositories(this IServiceCollection services)
        {
            // 注册Repository实现
            services.AddScoped<Repositories.Interfaces.IKeyframeRepository, Repositories.Implementations.KeyframeRepositoryImpl>();
            services.AddScoped<Repositories.Interfaces.ITimingRepository, Repositories.Implementations.TimingRepository>();
            services.AddScoped<Repositories.Interfaces.IOriginalModeRepository, Repositories.Implementations.OriginalModeRepositoryImpl>();
            services.AddScoped<Repositories.Interfaces.IMediaFileRepository, Repositories.Implementations.MediaFileRepositoryImpl>();
            services.AddScoped<Repositories.Interfaces.ICompositeScriptRepository, Repositories.Implementations.CompositeScriptRepository>();

            // 保留旧的Repository（向后兼容）
            services.AddScoped<KeyframeRepository>();

            return services;
        }

        /// <summary>
        /// 注册核心服务层
        /// </summary>
        private static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            // 播放状态机（Singleton）
            services.AddSingleton<PlaybackStateMachine>();

            // 关键帧模式服务（Scoped）- 注册为接口和具体类
            services.AddScoped<Services.Implementations.KeyframeRecordingService>();
            services.AddScoped<Services.Implementations.KeyframePlaybackService>();
            
            // 原图模式服务（Scoped）
            services.AddScoped<Services.Implementations.OriginalRecordingService>();
            services.AddScoped<Services.Implementations.OriginalPlaybackService>();
            
            // 合成播放服务（Scoped）
            services.AddScoped<Services.Implementations.CompositePlaybackService>();
            
            // 默认服务接口注册（使用关键帧模式作为默认）
            services.AddScoped<Services.Interfaces.IRecordingService>(sp => 
                sp.GetRequiredService<Services.Implementations.KeyframeRecordingService>());
            services.AddScoped<Services.Interfaces.IPlaybackService>(sp => 
                sp.GetRequiredService<Services.Implementations.KeyframePlaybackService>());
            
            // 倒计时服务（Singleton，全局唯一实例，确保事件订阅正常工作）
            services.AddSingleton<Services.Interfaces.ICountdownService, Services.Implementations.CountdownService>();

            // 服务工厂（Scoped）
            services.AddScoped<Services.PlaybackServiceFactory>();

            return services;
        }

        /// <summary>
        /// 注册Manager层
        /// </summary>
        private static IServiceCollection AddManagers(this IServiceCollection services)
        {
            // 当前已有的Manager（这些依赖MainWindow，需要特殊处理）
            // KeyframeManager, KeyframeNavigator等会在MainWindow创建时手动实例化

            return services;
        }

        /// <summary>
        /// 注册ViewModel层
        /// </summary>
        private static IServiceCollection AddViewModels(this IServiceCollection services)
        {
            // 注册ViewModels（Transient，每次请求创建新实例）
            services.AddTransient<ViewModels.PlaybackControlViewModel>();
            
            // 注册图片缓存（Singleton，全局共享）
            services.AddSingleton<Utils.ImageCache>();

            return services;
        }
    }
}

