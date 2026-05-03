using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Repositories;
using ImageColorChanger.Managers;
using ImageColorChanger.Managers.Keyframes;
using ImageColorChanger.Services;
using ImageColorChanger.Services.Interfaces;
using ImageColorChanger.Services.Lyrics.Output;
using ImageColorChanger.Services.Ndi;
using ImageColorChanger.Services.Projection.Output;
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
            
            // 新版仓储（P0-4B）
            services.AddScoped<IFolderRepository, FolderRepository>();
            services.AddScoped<IMediaRepository, MediaRepository>();
            services.AddScoped<ISettingsRepository, SettingsRepository>();
            services.AddScoped<IOriginalMarkRepository, OriginalMarkRepository>();
            services.AddScoped<IKeyframeRepository, ImageColorChanger.Database.Repositories.KeyframeRepository>();
            services.AddScoped<IDatabaseMaintenanceRepository, DatabaseMaintenanceRepository>();

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
            services.AddScoped<Managers.Keyframes.IKeyframeStore, Managers.Keyframes.KeyframeStoreAdapter>();
            services.AddScoped<Repositories.TextEditor.ITextProjectRepository, Repositories.TextEditor.EfTextProjectRepository>();
            services.AddScoped<Repositories.TextEditor.ISlideRepository, Repositories.TextEditor.EfSlideRepository>();
            services.AddScoped<Repositories.TextEditor.ITextElementRepository, Repositories.TextEditor.EfTextElementRepository>();
            services.AddScoped<Repositories.TextEditor.IRichTextSpanRepository, Repositories.TextEditor.EfRichTextSpanRepository>();

            return services;
        }

        /// <summary>
        /// 注册核心服务层
        /// </summary>
        private static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            // 配置管理器（Singleton）- 全局唯一配置
            services.AddSingleton<ConfigManager>();
            services.AddSingleton<ILyricsNdiConfigProvider>(sp => sp.GetRequiredService<ConfigManager>());
            services.AddSingleton<IProjectionNdiConfigProvider>(sp => sp.GetRequiredService<ConfigManager>());
            services.AddSingleton<ILyricsNdiSender, NoopLyricsNdiSender>();
            services.AddSingleton<IProjectionNdiSender, NativeProjectionNdiSender>();
            services.AddSingleton<IProjectionNdiModeResolver, ProjectionNdiModeResolver>();
            services.AddSingleton<LyricsNdiOutputManager>();
            services.AddSingleton<ProjectionNdiOutputManager>();
            services.AddSingleton<INdiRouter, NdiRouter>();
            services.AddSingleton<IVideoBackgroundManager, VideoBackgroundManager>();
            services.AddSingleton<PakManager>();
            services.AddSingleton<GPUContext>();
            services.AddSingleton<SkiaFontService>();

            // 文本编辑域服务
            services.AddSingleton<Services.TextEditor.ITextBoxEditSessionService, Services.TextEditor.TextBoxEditSessionService>();
            services.AddSingleton<Services.TextEditor.ITextLayoutService, Services.TextEditor.TextLayoutService>();
            services.AddSingleton<Services.TextEditor.IRichTextSerializer, Services.TextEditor.RichTextSerializer>();
            services.AddScoped<Services.TextEditor.ITextElementPersistenceService, Services.TextEditor.TextElementPersistenceService>();
            services.AddScoped<Services.TextEditor.Application.ITextProjectService, Services.TextEditor.Application.TextProjectService>();
            services.AddScoped<Services.TextEditor.Application.ITextEditorSaveOrchestrator, Services.TextEditor.Application.TextEditorSaveOrchestrator>();
            services.AddSingleton<Services.TextEditor.Rendering.ITextEditorProjectionComposer, Services.TextEditor.Rendering.TextEditorProjectionComposer>();
            services.AddSingleton<Services.TextEditor.Rendering.ITextEditorProjectionRenderStateService, Services.TextEditor.Rendering.TextEditorProjectionRenderStateService>();
            services.AddSingleton<Services.TextEditor.Rendering.ITextEditorRenderSafetyService, Services.TextEditor.Rendering.TextEditorRenderSafetyService>();
            services.AddSingleton<Services.TextEditor.Rendering.ITextEditorThumbnailService, Services.TextEditor.Rendering.TextEditorThumbnailService>();

            // 认证服务（仅暴露接口，避免上层依赖具体实现）
            services.AddSingleton<IAuthService>(_ => AuthService.Instance);

            // 认证门面（窗口层使用，避免直接依赖 AuthService.Instance）
            services.AddSingleton<IAuthFacade>(sp => new AuthServiceFacade(sp.GetRequiredService<IAuthService>()));
            services.AddSingleton<IUiSettingsStore, UiSettingsStore>();
            
            // 内存缓存（Singleton）- 圣经服务需要
            services.AddMemoryCache();
            
            //  新增：SkiaSharp文本渲染服务（Singleton）
            services.AddSingleton<SkiaTextRenderer>();
            services.AddSingleton<TextLayoutEngine>();
            
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
            
            // 圣经服务（Singleton，需要 ConfigManager）
            services.AddSingleton<Services.Interfaces.IBibleService>(sp => 
            {
                var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                var configManager = sp.GetRequiredService<ConfigManager>();
                return new Services.Implementations.BibleService(cache, configManager);
            });
            
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
            services.AddSingleton<SortManager>();
            services.AddSingleton<SearchManager>();
            services.AddSingleton<ImportManager>();
            services.AddTransient<SlideExportManager>(sp =>
                new SlideExportManager(sp.GetRequiredService<DatabaseManager>().GetDbContext()));
            services.AddTransient<SlideImportManager>(sp =>
                new SlideImportManager(sp.GetRequiredService<DatabaseManager>().GetDbContext()));

            return services;
        }

        /// <summary>
        /// 注册ViewModel层
        /// </summary>
        private static IServiceCollection AddViewModels(this IServiceCollection services)
        {
            // 注册ViewModels（Transient，每次请求创建新实例）
            services.AddTransient<ViewModels.PlaybackControlViewModel>();
            services.AddTransient<UI.Composition.ScriptEditWindowFactory>();
            
            // 注册图片缓存（Singleton，全局共享）
            services.AddSingleton<Utils.ImageCache>();

            return services;
        }
    }
}


