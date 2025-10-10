# Canvas Cast - C# 重写技术方案

## 📋 目录
1. [项目概述](#项目概述)
2. [Python项目分析](#python项目分析)
3. [C#技术栈选型](#c技术栈选型)
4. [架构设计](#架构设计)
5. [核心模块设计](#核心模块设计)
6. [数据库设计](#数据库设计)
7. [性能优化方案](#性能优化方案)
8. [开发路线图](#开发路线图)
9. [风险评估](#风险评估)

---

## 项目概述

### Python版本核心功能
Canvas Cast V2.5 是一个功能完善的图像和媒体浏览/播放工具，主要功能包括：

1. **图片管理**
   - 文件夹导入与管理
   - 项目树浏览
   - 搜索与过滤
   - 拖拽排序

2. **图片查看**
   - 高性能图片渲染
   - 缩放与平滑滚动
   - 颜色变换效果
   - GPU加速处理

3. **媒体播放**
   - 视频/音频播放
   - 4种播放模式
   - 播放器控制

4. **投影功能**
   - 多屏投影
   - 实时同步
   - 分离式更新优化

5. **关键帧系统**
   - 关键帧录制
   - 时间控制
   - 自动播放
   - 倒计时显示

### C#重写目标
- ✅ 保留所有核心功能
- ✅ 提升性能（WPF硬件加速）
- ✅ 改善用户体验（Material Design）
- ✅ 更好的GPU加速（ComputeSharp/DirectX）
- ✅ 原生Windows体验

---

## Python项目分析

### 技术栈
```
GUI框架:     Tkinter
图像处理:    Pillow, numpy
GPU加速:     moderngl, PyOpenGL
媒体播放:    python-vlc
数据库:      SQLite (直接使用)
其他:        pynput, screeninfo, pypinyin
```

### 模块结构
```
Canvas/
├── core/                    # 核心功能
│   ├── config_manager.py    # 配置管理
│   └── image_processor.py   # 图像处理
├── database/                # 数据库
│   ├── database_manager.py
│   ├── schema_db.py
│   └── project_db.py
├── ui/                      # 用户界面
│   ├── ui_components.py
│   ├── context_menu_manager.py
│   ├── event_handler.py
│   └── media_player.py
├── projection/              # 投影
│   └── projection_manager.py
├── playback/               # 播放控制
│   ├── playback_controller.py
│   └── keytime.py
├── keyframes/              # 关键帧
│   ├── keyframe_manager.py
│   └── keyframe_navigation.py
├── managers/               # 管理器
│   ├── import_manager.py
│   ├── project_manager.py
│   ├── script_manager.py
│   ├── sort_manager.py
│   ├── media_manager.py
│   └── yuantu.py
├── gpu/                    # GPU加速
│   └── [8个GPU相关模块]
└── engines/                # 引擎
    └── smooth_scroll_engine.py
```

### 性能优化特性
1. **分离式投影更新** - 90%性能提升
2. **智能缩放算法选择**
3. **缓存共享机制**
4. **多层节流控制**
5. **GPU加速渲染**

---

## C#技术栈选型

### ✅ 已选定技术

| 功能领域 | 技术选择 | 优势 |
|---------|---------|------|
| **UI框架** | WPF (Windows Presentation Foundation) | 现代化、MVVM支持、硬件加速 |
| **UI库** | MaterialDesignInXAML | 美观、组件丰富、易用 |
| **图像处理** | SixLabors.ImageSharp | 高性能、跨平台、现代API |
| **GPU加速** | ComputeSharp | DirectX 12、易用、高性能 |
| **数据库** | EF Core + SQLite | ORM、类型安全、易维护 |

### 📦 推荐补充的NuGet包

```xml
<!-- 必需包 -->
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.7" />
<PackageReference Include="MaterialDesignThemes" Version="5.1.0" />
<PackageReference Include="ComputeSharp" Version="3.2.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.0" />

<!-- 媒体播放 -->
<PackageReference Include="LibVLCSharp" Version="3.8.5" />
<PackageReference Include="LibVLCSharp.WPF" Version="3.8.5" />
<PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.21" />

<!-- MVVM框架 -->
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />

<!-- 多屏幕管理 -->
<PackageReference Include="System.Windows.Forms" /> <!-- 已有 -->

<!-- JSON配置 -->
<PackageReference Include="System.Text.Json" Version="8.0.0" />

<!-- 性能优化 -->
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.0" />
<PackageReference Include="System.Collections.Immutable" Version="8.0.0" />

<!-- 日志 -->
<PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
```

---

## 架构设计

### 整体架构 - MVVM模式

```
┌─────────────────────────────────────────┐
│              View (XAML)                 │
│  - MainWindow.xaml                       │
│  - ProjectTreeView.xaml                  │
│  - MediaPlayerControl.xaml               │
│  - ProjectionWindow.xaml                 │
└──────────────┬──────────────────────────┘
               │ DataBinding
┌──────────────▼──────────────────────────┐
│           ViewModel                      │
│  - MainViewModel                         │
│  - ProjectViewModel                      │
│  - MediaPlayerViewModel                  │
│  - KeyframeViewModel                     │
└──────────────┬──────────────────────────┘
               │ 业务逻辑
┌──────────────▼──────────────────────────┐
│             Model                        │
│  - Project                               │
│  - Image                                 │
│  - Keyframe                              │
│  - MediaFile                             │
└──────────────┬──────────────────────────┘
               │
┌──────────────▼──────────────────────────┐
│           Services (服务层)               │
│  - ImageProcessingService                │
│  - DatabaseService                       │
│  - MediaPlayerService                    │
│  - ProjectionService                     │
│  - KeyframeService                       │
│  - ConfigurationService                  │
└─────────────────────────────────────────┘
```

### 文件夹结构

```
CCanvas/
├── App.xaml
├── App.xaml.cs
├── CCanvas.csproj
│
├── Views/                          # 视图
│   ├── MainWindow.xaml
│   ├── ProjectionWindow.xaml
│   ├── Controls/
│   │   ├── ProjectTreeControl.xaml
│   │   ├── ImageViewerControl.xaml
│   │   ├── MediaPlayerControl.xaml
│   │   └── KeyframeControlPanel.xaml
│   └── Dialogs/
│       ├── ColorPickerDialog.xaml
│       └── SettingsDialog.xaml
│
├── ViewModels/                     # 视图模型
│   ├── MainViewModel.cs
│   ├── ProjectViewModel.cs
│   ├── ImageViewerViewModel.cs
│   ├── MediaPlayerViewModel.cs
│   ├── KeyframeViewModel.cs
│   └── Base/
│       └── ViewModelBase.cs
│
├── Models/                         # 数据模型
│   ├── Project.cs
│   ├── Folder.cs
│   ├── ImageFile.cs
│   ├── MediaFile.cs
│   ├── Keyframe.cs
│   ├── PlaybackSettings.cs
│   └── Configuration.cs
│
├── Services/                       # 服务层
│   ├── Core/
│   │   ├── IImageProcessingService.cs
│   │   └── ImageProcessingService.cs
│   ├── Database/
│   │   ├── IDatabaseService.cs
│   │   ├── DatabaseService.cs
│   │   └── CCanvasDbContext.cs
│   ├── Media/
│   │   ├── IMediaPlayerService.cs
│   │   └── MediaPlayerService.cs
│   ├── Projection/
│   │   ├── IProjectionService.cs
│   │   └── ProjectionService.cs
│   ├── Keyframe/
│   │   ├── IKeyframeService.cs
│   │   └── KeyframeService.cs
│   └── Configuration/
│       ├── IConfigurationService.cs
│       └── ConfigurationService.cs
│
├── GPU/                            # GPU加速
│   ├── Shaders/
│   │   ├── ColorTransformShader.cs
│   │   └── ScalingShader.cs
│   └── GpuImageProcessor.cs
│
├── Helpers/                        # 辅助类
│   ├── ImageHelper.cs
│   ├── ColorHelper.cs
│   ├── ScreenHelper.cs
│   └── PathHelper.cs
│
├── Converters/                     # WPF转换器
│   ├── BoolToVisibilityConverter.cs
│   ├── ColorToBrushConverter.cs
│   └── ByteToImageConverter.cs
│
├── Resources/                      # 资源
│   ├── Styles/
│   │   └── CustomStyles.xaml
│   ├── Icons/
│   │   └── app.ico
│   └── Themes/
│       └── CustomTheme.xaml
│
└── Data/                          # 数据文件
    ├── Database/
    │   └── canvas.db
    └── Config/
        └── settings.json
```

---

## 核心模块设计

### 1. 图像处理服务 (ImageProcessingService)

```csharp
public interface IImageProcessingService
{
    /// <summary>
    /// 加载图片
    /// </summary>
    Task<Image<Rgba32>> LoadImageAsync(string path);
    
    /// <summary>
    /// 应用颜色变换 (GPU加速)
    /// </summary>
    Task<Image<Rgba32>> ApplyColorTransformAsync(
        Image<Rgba32> source, 
        Rgba32 targetColor, 
        bool isWhiteBackground);
    
    /// <summary>
    /// 智能缩放图片
    /// </summary>
    Task<Image<Rgba32>> ResizeImageAsync(
        Image<Rgba32> source, 
        int targetWidth, 
        int targetHeight, 
        ResampleAlgorithm algorithm);
    
    /// <summary>
    /// 检测背景类型
    /// </summary>
    BackgroundType DetectBackground(Image<Rgba32> image);
    
    /// <summary>
    /// 转换为WPF BitmapSource
    /// </summary>
    BitmapSource ToBitmapSource(Image<Rgba32> image);
}

public class ImageProcessingService : IImageProcessingService
{
    private readonly GpuImageProcessor _gpuProcessor;
    private readonly IMemoryCache _cache;
    
    public ImageProcessingService(
        GpuImageProcessor gpuProcessor,
        IMemoryCache cache)
    {
        _gpuProcessor = gpuProcessor;
        _cache = cache;
    }
    
    // 实现智能缓存、GPU加速等功能
}
```

### 2. 数据库服务 (DatabaseService)

```csharp
// Entity Framework Core DbContext
public class CCanvasDbContext : DbContext
{
    public DbSet<Project> Projects { get; set; }
    public DbSet<Folder> Folders { get; set; }
    public DbSet<ImageFile> Images { get; set; }
    public DbSet<MediaFile> MediaFiles { get; set; }
    public DbSet<Keyframe> Keyframes { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 配置表关系
        modelBuilder.Entity<Folder>()
            .HasMany(f => f.Images)
            .WithOne(i => i.Folder)
            .HasForeignKey(i => i.FolderId);
            
        modelBuilder.Entity<ImageFile>()
            .HasMany(i => i.Keyframes)
            .WithOne(k => k.Image)
            .HasForeignKey(k => k.ImageId);
    }
}

// 数据模型
public class ImageFile
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public FileType FileType { get; set; }
    public int? FolderId { get; set; }
    public int OrderIndex { get; set; }
    public bool IsOriginalMarked { get; set; }
    public OriginalMarkType? MarkType { get; set; }
    
    // 导航属性
    public Folder Folder { get; set; }
    public ICollection<Keyframe> Keyframes { get; set; }
}

public class Keyframe
{
    public int Id { get; set; }
    public int ImageId { get; set; }
    public int Position { get; set; }
    public int OrderIndex { get; set; }
    public int? Duration { get; set; }
    
    // 导航属性
    public ImageFile Image { get; set; }
}
```

### 3. 媒体播放服务 (MediaPlayerService)

```csharp
public interface IMediaPlayerService
{
    /// <summary>
    /// 初始化播放器
    /// </summary>
    void Initialize();
    
    /// <summary>
    /// 播放媒体文件
    /// </summary>
    Task PlayAsync(string filePath);
    
    /// <summary>
    /// 暂停/继续
    /// </summary>
    void TogglePlayPause();
    
    /// <summary>
    /// 停止播放
    /// </summary>
    void Stop();
    
    /// <summary>
    /// 设置播放模式
    /// </summary>
    void SetPlayMode(PlayMode mode);
    
    /// <summary>
    /// 播放状态
    /// </summary>
    IObservable<PlayerState> PlayerStateChanged { get; }
}

public class MediaPlayerService : IMediaPlayerService
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    
    public MediaPlayerService()
    {
        _libVLC = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVLC);
    }
    
    // LibVLCSharp实现
}
```

### 4. 投影服务 (ProjectionService)

```csharp
public interface IProjectionService
{
    /// <summary>
    /// 开启投影
    /// </summary>
    Task<bool> StartProjectionAsync(Screen targetScreen);
    
    /// <summary>
    /// 关闭投影
    /// </summary>
    void StopProjection();
    
    /// <summary>
    /// 更新投影内容
    /// </summary>
    void UpdateProjection(BitmapSource imageSource);
    
    /// <summary>
    /// 分离式更新投影 (优化性能)
    /// </summary>
    Task UpdateProjectionSeparatedAsync(BitmapSource imageSource);
}

public class ProjectionService : IProjectionService
{
    private Window _projectionWindow;
    private readonly IImageProcessingService _imageService;
    private Timer _delayedUpdateTimer;
    
    // 实现分离式更新策略
    public async Task UpdateProjectionSeparatedAsync(BitmapSource imageSource)
    {
        // 取消之前的延迟更新
        _delayedUpdateTimer?.Stop();
        
        // 延迟300ms更新投影（防抖）
        _delayedUpdateTimer = new Timer(300);
        _delayedUpdateTimer.Elapsed += async (s, e) =>
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                UpdateProjection(imageSource);
            });
        };
        _delayedUpdateTimer.Start();
    }
}
```

### 5. 关键帧服务 (KeyframeService)

```csharp
public interface IKeyframeService
{
    /// <summary>
    /// 添加关键帧
    /// </summary>
    Task<Keyframe> AddKeyframeAsync(int imageId, int position);
    
    /// <summary>
    /// 删除关键帧
    /// </summary>
    Task DeleteKeyframeAsync(int keyframeId);
    
    /// <summary>
    /// 获取图片的所有关键帧
    /// </summary>
    Task<List<Keyframe>> GetKeyframesAsync(int imageId);
    
    /// <summary>
    /// 录制时间
    /// </summary>
    Task RecordTimingAsync(int imageId, List<KeyframeTiming> timings);
    
    /// <summary>
    /// 自动播放
    /// </summary>
    Task AutoPlayAsync(int imageId, int loopCount, 
        IProgress<PlaybackProgress> progress);
}
```

### 6. GPU图像处理器 (GpuImageProcessor)

```csharp
public class GpuImageProcessor : IDisposable
{
    private readonly GraphicsDevice _device;
    
    public GpuImageProcessor()
    {
        _device = GraphicsDevice.GetDefault();
    }
    
    /// <summary>
    /// GPU加速颜色变换
    /// </summary>
    public Image<Rgba32> ProcessImage(
        Image<Rgba32> source, 
        Rgba32 targetColor, 
        bool isWhiteBackground)
    {
        // 使用ComputeSharp着色器
        var result = source.Clone();
        
        using var texture = _device.AllocateReadWriteTexture2D<Rgba32>(
            source.Width, source.Height);
            
        // 上传数据到GPU
        source.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                texture[y] = row.ToArray();
            }
        });
        
        // 执行着色器
        _device.For(source.Width, source.Height, 
            new ColorTransformShader(texture, targetColor, isWhiteBackground));
        
        // 下载结果
        // ...
        
        return result;
    }
    
    public void Dispose()
    {
        _device?.Dispose();
    }
}

// ComputeSharp着色器
[AutoConstructor]
public readonly partial struct ColorTransformShader : IComputeShader
{
    private readonly ReadWriteTexture2D<Rgba32> texture;
    private readonly Rgba32 targetColor;
    private readonly Bool isWhiteBackground;
    
    public void Execute()
    {
        Rgba32 pixel = texture[ThreadIds.XY];
        
        // 实现颜色变换逻辑
        // ...
        
        texture[ThreadIds.XY] = result;
    }
}
```

---

## 数据库设计

### EF Core数据模型

```csharp
// Projects表
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    
    public ICollection<Folder> Folders { get; set; }
    public ICollection<ImageFile> Images { get; set; }
}

// Folders表
public class Folder
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public int? ProjectId { get; set; }
    public bool IsManualSort { get; set; }
    public OriginalMarkType? OriginalMarkType { get; set; }
    public PlayMode? PlayMode { get; set; }
    
    public Project Project { get; set; }
    public ICollection<ImageFile> Images { get; set; }
}

// Images表
public class ImageFile
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public FileType FileType { get; set; }
    public int? FolderId { get; set; }
    public int OrderIndex { get; set; }
    public bool IsOriginalMarked { get; set; }
    public OriginalMarkType? MarkType { get; set; }
    
    public Folder Folder { get; set; }
    public ICollection<Keyframe> Keyframes { get; set; }
}

// Keyframes表
public class Keyframe
{
    public int Id { get; set; }
    public int ImageId { get; set; }
    public int Position { get; set; }
    public int OrderIndex { get; set; }
    public int? Duration { get; set; }
    
    public ImageFile Image { get; set; }
}

// 枚举类型
public enum FileType
{
    Image,
    Video,
    Audio
}

public enum OriginalMarkType
{
    Loop,      // 循环
    Sequence   // 顺序
}

public enum PlayMode
{
    Sequential,  // 顺序播放
    Random,      // 随机播放
    LoopOne,     // 单曲循环
    LoopAll      // 列表循环
}
```

---

## 性能优化方案

### 1. 图片缓存策略

```csharp
public class ImageCache
{
    private readonly IMemoryCache _cache;
    private readonly int _maxCacheSize = 50;
    
    public async Task<BitmapSource> GetOrLoadAsync(
        string path, 
        Func<Task<BitmapSource>> loader)
    {
        if (_cache.TryGetValue(path, out BitmapSource cached))
            return cached;
            
        var image = await loader();
        
        var options = new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = TimeSpan.FromMinutes(10)
        };
        
        _cache.Set(path, image, options);
        return image;
    }
}
```

### 2. 虚拟化项目树

```xml
<!-- 使用VirtualizingStackPanel优化大列表 -->
<TreeView VirtualizingPanel.IsVirtualizing="True"
          VirtualizingPanel.VirtualizationMode="Recycling">
    <TreeView.ItemsPanel>
        <ItemsPanelTemplate>
            <VirtualizingStackPanel/>
        </ItemsPanelTemplate>
    </TreeView.ItemsPanel>
</TreeView>
```

### 3. 异步图片加载

```csharp
public class AsyncImageLoader
{
    public static readonly DependencyProperty SourceProperty = 
        DependencyProperty.RegisterAttached(
            "Source", 
            typeof(string), 
            typeof(AsyncImageLoader),
            new PropertyMetadata(null, OnSourceChanged));
    
    private static async void OnSourceChanged(
        DependencyObject d, 
        DependencyPropertyChangedEventArgs e)
    {
        if (d is System.Windows.Controls.Image image && 
            e.NewValue is string path)
        {
            image.Source = null;
            
            var bitmap = await Task.Run(() => 
                LoadBitmapAsync(path));
                
            image.Source = bitmap;
        }
    }
}
```

### 4. GPU加速缩放

```csharp
// 使用WPF的RenderTargetBitmap实现硬件加速缩放
public BitmapSource ScaleImageHardwareAccelerated(
    BitmapSource source, 
    double scaleX, 
    double scaleY)
{
    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
        context.PushTransform(new ScaleTransform(scaleX, scaleY));
        context.DrawImage(source, new Rect(0, 0, 
            source.PixelWidth, source.PixelHeight));
    }
    
    var target = new RenderTargetBitmap(
        (int)(source.PixelWidth * scaleX),
        (int)(source.PixelHeight * scaleY),
        96, 96, PixelFormats.Pbgra32);
        
    target.Render(visual);
    target.Freeze();
    
    return target;
}
```

### 5. 分离式投影更新（已验证）

```csharp
// 参考Python版本的90%性能提升策略
public class SeparatedProjectionUpdater
{
    private DispatcherTimer _delayTimer;
    private const int DelayMs = 300;
    
    public void ScheduleUpdate(Action updateAction)
    {
        _delayTimer?.Stop();
        _delayTimer = new DispatcherTimer 
        { 
            Interval = TimeSpan.FromMilliseconds(DelayMs) 
        };
        
        _delayTimer.Tick += (s, e) =>
        {
            updateAction();
            _delayTimer.Stop();
        };
        
        _delayTimer.Start();
    }
}
```

---

## 开发路线图

### 阶段1: 基础架构 (2-3周)

**目标**: 搭建MVVM架构，实现基础功能

- [x] WPF项目搭建 ✅
- [x] Material Design UI ✅
- [x] GPU加速验证 ✅
- [ ] MVVM架构实现
  - [ ] ViewModelBase基类
  - [ ] RelayCommand实现
  - [ ] INotifyPropertyChanged
- [ ] 依赖注入配置
  - [ ] Microsoft.Extensions.DependencyInjection
  - [ ] 服务注册
- [ ] EF Core数据库
  - [ ] DbContext设计
  - [ ] 迁移配置
  - [ ] 仓储模式

**交付物**:
- 完整的MVVM框架
- 数据库访问层
- 基础UI布局

### 阶段2: 核心功能 (4-5周)

**目标**: 实现图片管理和查看

- [ ] 图片管理
  - [ ] 文件夹导入
  - [ ] 项目树显示
  - [ ] 搜索功能
  - [ ] 拖拽排序
- [ ] 图片查看
  - [ ] 图片加载与显示
  - [ ] 缩放功能
  - [ ] 平滑滚动
  - [ ] 颜色变换
- [ ] 配置管理
  - [ ] JSON配置文件
  - [ ] 用户设置持久化

**交付物**:
- 完整的图片管理功能
- 高性能图片查看器
- 配置系统

### 阶段3: 媒体播放 (2-3周)

**目标**: 实现视频/音频播放

- [ ] LibVLCSharp集成
  - [ ] 播放器初始化
  - [ ] 视频渲染
  - [ ] 音频播放
- [ ] 播放控制
  - [ ] 播放/暂停/停止
  - [ ] 进度条
  - [ ] 音量控制
  - [ ] 4种播放模式
- [ ] UI集成
  - [ ] 播放器控制栏
  - [ ] 快捷键支持

**交付物**:
- 完整的媒体播放功能
- 播放器UI控件

### 阶段4: 投影功能 (2周)

**目标**: 实现多屏投影

- [ ] 多屏检测
  - [ ] 屏幕列表获取
  - [ ] 分辨率检测
- [ ] 投影窗口
  - [ ] 全屏窗口创建
  - [ ] 内容同步
  - [ ] 分离式更新
- [ ] 性能优化
  - [ ] 智能缩放算法
  - [ ] 延迟更新策略

**交付物**:
- 多屏投影功能
- 90%性能提升的分离式更新

### 阶段5: 关键帧系统 (3-4周)

**目标**: 实现关键帧录制和播放

- [ ] 关键帧管理
  - [ ] 添加/删除关键帧
  - [ ] 关键帧导航
  - [ ] 时间录制
- [ ] 自动播放
  - [ ] 播放控制
  - [ ] 循环设置
  - [ ] 倒计时显示
- [ ] 原图模式
  - [ ] 原图标记
  - [ ] 相似图查找

**交付物**:
- 完整的关键帧系统
- 自动播放功能

### 阶段6: GPU优化 (2-3周)

**目标**: 深度GPU加速优化

- [ ] ComputeSharp着色器
  - [ ] 颜色变换着色器
  - [ ] 缩放着色器
- [ ] 纹理管理
  - [ ] 纹理池
  - [ ] 共享机制
- [ ] 性能监控
  - [ ] FPS监控
  - [ ] 性能分析

**交付物**:
- GPU加速的图像处理
- 性能监控工具

### 阶段7: 打磨优化 (2-3周)

**目标**: 完善细节，性能优化

- [ ] UI优化
  - [ ] 动画效果
  - [ ] 响应式布局
  - [ ] 主题切换
- [ ] 性能优化
  - [ ] 内存优化
  - [ ] 加载速度优化
  - [ ] 缓存策略
- [ ] 错误处理
  - [ ] 日志系统
  - [ ] 异常处理
  - [ ] 用户提示

**交付物**:
- 完善的用户体验
- 稳定的性能表现

### 阶段8: 测试发布 (1-2周)

**目标**: 测试和发布

- [ ] 功能测试
- [ ] 性能测试
- [ ] 兼容性测试
- [ ] 打包发布
  - [ ] ClickOnce部署
  - [ ] MSIX打包

**交付物**:
- 可发布的应用程序
- 用户文档

---

## 风险评估

### 技术风险

| 风险项 | 影响 | 概率 | 缓解措施 |
|--------|------|------|---------|
| **LibVLCSharp兼容性** | 高 | 中 | 充分测试，准备备选方案(Windows Media Foundation) |
| **GPU加速效果** | 中 | 低 | ComputeSharp已验证，有保底CPU方案 |
| **大文件性能** | 中 | 中 | 实现渐进式加载、虚拟化 |
| **多屏投影稳定性** | 中 | 中 | 参考Python版本的成熟方案 |

### 开发风险

| 风险项 | 影响 | 概率 | 缓解措施 |
|--------|------|------|---------|
| **MVVM学习曲线** | 中 | 中 | 使用MVVM工具包，参考最佳实践 |
| **EF Core复杂度** | 低 | 低 | SQLite简单场景，EF Core文档完善 |
| **WPF性能调优** | 中 | 中 | 利用WPF硬件加速优势 |

### 推荐开发优先级

1. **高优先级** (必须实现)
   - 图片管理与查看
   - 基础UI框架
   - 数据库访问

2. **中优先级** (核心功能)
   - 媒体播放
   - 投影功能
   - 关键帧系统

3. **低优先级** (优化项)
   - 高级GPU优化
   - 动画效果
   - 主题切换

---

## 关键技术决策

### 为什么选择WPF而不是WinUI 3?

**WPF优势**:
- ✅ 成熟稳定，生态完善
- ✅ Material Design支持好
- ✅ GPU加速成熟
- ✅ 开发效率高

**WinUI 3劣势**:
- ❌ 生态不够成熟
- ❌ 第三方库少
- ❌ 学习资料少

### 为什么使用EF Core而不是直接SQL?

**EF Core优势**:
- ✅ 类型安全
- ✅ 易于维护
- ✅ 迁移方便
- ✅ LINQ查询

**Python版直接SQL劣势**:
- ❌ 容易出错
- ❌ 维护困难
- ❌ 字符串拼接风险

### 为什么用ComputeSharp而不是Shader Effects?

**ComputeSharp优势**:
- ✅ C#编写着色器
- ✅ 类型安全
- ✅ DirectX 12
- ✅ 高性能

**已验证**: 你的GPU加速测试已成功！

---

## 总结

### C#重写的优势

1. **性能**
   - WPF硬件加速
   - ComputeSharp GPU加速
   - 原生Windows API

2. **开发效率**
   - MVVM模式
   - 强类型
   - Visual Studio工具链

3. **用户体验**
   - Material Design
   - 流畅动画
   - 原生Windows体验

4. **可维护性**
   - 清晰架构
   - 依赖注入
   - 单元测试

### 预期效果

- **性能提升**: 30-50%（WPF硬件加速）
- **开发周期**: 20-25周（全职）
- **代码质量**: 更高的可维护性
- **用户体验**: 显著提升

---

**建议**: 
1. 先完成阶段1-2，验证架构可行性
2. 实现MVP（最小可行产品）快速迭代
3. 参考Python版本的成熟方案
4. 充分利用C#和WPF的优势

**下一步**:
开始实现MVVM框架和基础架构？我可以帮你生成具体代码！

