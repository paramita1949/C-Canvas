# Canvas Cast - UI重构完成说明

## ✅ 已完成内容

### 1. UI界面完全重新设计

已将原来的**图片变色工具UI**（左右布局）改为**Canvas Cast完整UI**（模仿Python版本）

#### 界面布局结构

```
┌─────────────────────────────────────────────────┐
│  顶部菜单栏：导入|投影|同步|返回|原图|缩放|变色    │
├─────────────────────────────────────────────────┤
│  关键帧控制：加帧|清帧|上帧|下帧|录制|播放|倒计时  │
├────────┬────────────────────────────────────────┤
│ 项目树 │          图片显示区域                   │
│        │      （黑色背景，支持缩放滚动）          │
│ 搜索框 │                                         │
│        │                                         │
│ 文件夹 │                                         │
│ 列表   │                                         │
├────────┴────────────────────────────────────────┤
│  媒体播放器控制栏（默认隐藏）                     │
└─────────────────────────────────────────────────┘
```

### 2. 已实现的功能模块

#### ✅ 基础功能（已实现）
- **图片加载与显示**
- **GPU加速颜色变换**（原有功能）
- **图片缩放** - 精细5%步进
- **平滑滚动** - Ctrl+滚轮缩放
- **图片拖动** - 支持拖动浏览大图
- **背景检测** - 自动检测黑底/白底

#### 🎨 UI组件（已创建）

**顶部菜单栏**：
- 导入按钮 ✅
- 投影按钮 ⏳ (UI完成，功能待实现)
- 同步按钮 ⏳
- 返回按钮 ✅
- 原图按钮 ⏳
- 缩放按钮 ✅
- 变色按钮 ✅ (左键切换，右键选颜色)
- 缩放百分比显示 ✅
- 屏幕选择器 ⏳

**关键帧控制栏**：
- 加帧/清帧/上帧/下帧 按钮 ⏳
- 播放次数按钮 ⏳
- 录制/播放/清时/脚本 按钮 ⏳
- 倒计时显示 ⏳
- 暂停/继续按钮 ⏳

**左侧面板**：
- 搜索框 ⏳
- 搜索范围选择 ⏳
- 项目树 (TreeView) ⏳

**右侧图片区域**：
- 缩放控制条 ✅
- 图片显示区 ✅
- ScrollViewer ✅

**底部媒体播放器**：
- 播放控制按钮 ⏳
- 进度条 ⏳
- 时间显示 ⏳
- 音量控制 ⏳
- 播放模式切换 ⏳

### 3. 代码结构优化

```csharp
MainWindow.xaml.cs
├── 字段 (Fields)
│   ├── 图像处理相关
│   ├── 缩放拖动相关
│   └── 项目数据
│
├── 初始化 (Initialization)
│   ├── GPU处理器初始化
│   └── UI初始化
│
├── 事件处理 (Event Handlers)
│   ├── 顶部菜单栏事件
│   ├── 关键帧控制栏事件
│   ├── 项目树事件
│   └── 媒体播放器事件
│
├── 核心功能 (Core Functions)
│   ├── 图像加载
│   ├── 背景检测
│   ├── 颜色效果
│   └── 图像显示
│
├── 缩放功能 (Zoom)
│   ├── 滚轮缩放
│   ├── 按钮缩放
│   └── 自动适应
│
└── 拖动功能 (Drag)
    ├── 鼠标按下
    ├── 鼠标移动
    └── 鼠标释放
```

---

## 📝 功能对照表

| 功能模块 | Python版本 | C#版本状态 | 说明 |
|---------|-----------|-----------|------|
| **图片管理** | ✅ | ⏳ 待实现 | UI已完成 |
| 导入文件夹 | ✅ | ⏳ | 按钮已创建 |
| 项目树浏览 | ✅ | ⏳ | TreeView已创建 |
| 搜索功能 | ✅ | ⏳ | 搜索框已创建 |
| 拖拽排序 | ✅ | ⏳ | 未实现 |
| **图片查看** | ✅ | ✅ 已完成 | |
| 图片加载 | ✅ | ✅ | |
| 缩放功能 | ✅ | ✅ | 5%精细步进 |
| 平滑滚动 | ✅ | ✅ | Ctrl+滚轮 |
| 颜色变换 | ✅ | ✅ | GPU加速 |
| **投影功能** | ✅ | ⏳ 待实现 | UI已完成 |
| 多屏投影 | ✅ | ⏳ | 屏幕选择器已创建 |
| 实时同步 | ✅ | ⏳ | |
| 分离式更新 | ✅ | ⏳ | |
| **关键帧系统** | ✅ | ⏳ 待实现 | UI已完成 |
| 关键帧录制 | ✅ | ⏳ | 按钮已创建 |
| 时间控制 | ✅ | ⏳ | |
| 自动播放 | ✅ | ⏳ | |
| 倒计时显示 | ✅ | ⏳ | UI已创建 |
| **媒体播放** | ✅ | ⏳ 待实现 | UI已完成 |
| 视频播放 | ✅ | ⏳ | 需集成LibVLC |
| 音频播放 | ✅ | ⏳ | 需集成LibVLC |
| 播放模式 | ✅ | ⏳ | 4种模式 |

---

## 🎯 下一步开发优先级

### 优先级1：核心功能（必须）

#### 1.1 数据库集成（1-2周）
```csharp
// 选择方案：直接使用SQLite（和Python版本一致）
Install-Package Microsoft.Data.Sqlite

// 创建服务
public class DatabaseService
{
    private SqliteConnection _connection;
    
    public List<ImageFile> GetImages(int? folderId)
    {
        // 直接SQL查询
    }
}
```

**任务清单**：
- [ ] 安装 `Microsoft.Data.Sqlite` NuGet包
- [ ] 创建 `DatabaseService` 类
- [ ] 实现表结构（images, folders, keyframes）
- [ ] 实现基础CRUD操作
- [ ] 数据库初始化

#### 1.2 项目树功能（1周）
```csharp
// 实现项目树数据绑定
public class ProjectTreeViewModel
{
    public ObservableCollection<TreeNode> Items { get; set; }
    
    public void LoadFromDatabase()
    {
        // 从数据库加载项目树
    }
}
```

**任务清单**：
- [ ] 实现 `ProjectTreeItem` 数据模型
- [ ] 实现文件夹导入功能
- [ ] 实现项目树加载
- [ ] 实现双击加载图片
- [ ] 实现右键菜单

#### 1.3 图片导入管理（1周）
**任务清单**：
- [ ] 实现导入单个图片
- [ ] 实现导入文件夹（递归）
- [ ] 实现文件类型检测（图片/视频/音频）
- [ ] 实现数据库保存
- [ ] 实现项目树刷新

### 优先级2：高级功能（重要）

#### 2.1 投影功能（1-2周）
**任务清单**：
- [ ] 实现多屏检测
- [ ] 创建投影窗口
- [ ] 实现内容同步
- [ ] 实现分离式更新（90%性能提升）
- [ ] 实现快捷键控制（ESC关闭）

#### 2.2 媒体播放（2周）
```csharp
// 安装LibVLC
Install-Package LibVLCSharp
Install-Package LibVLCSharp.WPF
Install-Package VideoLAN.LibVLC.Windows
```

**任务清单**：
- [ ] 集成LibVLCSharp
- [ ] 实现视频播放
- [ ] 实现音频播放
- [ ] 实现播放控制（播放/暂停/停止）
- [ ] 实现4种播放模式
- [ ] 实现进度条和音量控制

### 优先级3：完善功能（可选）

#### 3.1 关键帧系统（2-3周）
**任务清单**：
- [ ] 实现关键帧添加/删除
- [ ] 实现关键帧导航
- [ ] 实现时间录制
- [ ] 实现自动播放
- [ ] 实现倒计时显示
- [ ] 实现播放控制

#### 3.2 原图模式（1周）
**任务清单**：
- [ ] 实现原图标记
- [ ] 实现原图模式切换
- [ ] 实现相似图查找

---

## 🔧 技术实现建议

### 1. MVVM架构改造（可选但推荐）

当前是代码后置模式，建议逐步改造为MVVM：

```csharp
// 1. 安装MVVM工具包
Install-Package CommunityToolkit.Mvvm

// 2. 创建ViewModel基类
public class ViewModelBase : ObservableObject { }

// 3. 创建MainViewModel
public class MainViewModel : ViewModelBase
{
    private string _currentImagePath;
    public string CurrentImagePath
    {
        get => _currentImagePath;
        set => SetProperty(ref _currentImagePath, value);
    }
    
    public RelayCommand LoadImageCommand { get; }
}
```

### 2. 依赖注入（推荐）

```csharp
// App.xaml.cs
public partial class App : Application
{
    private ServiceProvider _serviceProvider;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        
        // 注册服务
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IImageService, ImageService>();
        services.AddSingleton<IMediaService, MediaService>();
        
        // 注册ViewModels
        services.AddTransient<MainViewModel>();
        
        _serviceProvider = services.BuildServiceProvider();
        
        var mainWindow = new MainWindow
        {
            DataContext = _serviceProvider.GetService<MainViewModel>()
        };
        mainWindow.Show();
    }
}
```

### 3. 异步操作（必须）

所有IO操作都应该异步：

```csharp
public async Task<Image<Rgba32>> LoadImageAsync(string path)
{
    return await Task.Run(() => Image.Load<Rgba32>(path));
}

private async void BtnImport_Click(object sender, RoutedEventArgs e)
{
    var path = SelectFile();
    if (path != null)
    {
        var image = await LoadImageAsync(path);
        DisplayImage(image);
    }
}
```

---

## 📦 需要的NuGet包

### 必需包（立即安装）
```powershell
# SQLite数据库
Install-Package Microsoft.Data.Sqlite

# 异步操作
Install-Package System.Threading.Tasks.Extensions

# 集合操作
Install-Package System.Linq.Async
```

### 可选包（按需安装）
```powershell
# MVVM工具包
Install-Package CommunityToolkit.Mvvm

# 依赖注入
Install-Package Microsoft.Extensions.DependencyInjection

# 缓存
Install-Package Microsoft.Extensions.Caching.Memory

# 日志
Install-Package Serilog.Sinks.File

# LibVLC（媒体播放）
Install-Package LibVLCSharp
Install-Package LibVLCSharp.WPF
Install-Package VideoLAN.LibVLC.Windows
```

---

## 🎨 UI改进建议

### 1. 主题切换
可以添加深色/浅色主题切换：

```xml
<!-- App.xaml -->
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <materialDesign:BundledTheme 
                BaseTheme="Light" 
                PrimaryColor="Blue" 
                SecondaryColor="Lime" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

### 2. 动画效果
可以添加页面切换动画：

```xml
<Grid.Resources>
    <Storyboard x:Key="FadeIn">
        <DoubleAnimation 
            Storyboard.TargetProperty="Opacity"
            From="0" To="1" Duration="0:0:0.3"/>
    </Storyboard>
</Grid.Resources>
```

### 3. 响应式布局
支持不同屏幕尺寸：

```xml
<VisualStateManager.VisualStateGroups>
    <VisualStateGroup x:Name="AdaptiveStates">
        <VisualState x:Name="NarrowState">
            <VisualState.StateTriggers>
                <AdaptiveTrigger MinWindowWidth="0"/>
            </VisualState.StateTriggers>
            <VisualState.Setters>
                <!-- 窄屏布局调整 -->
            </VisualState.Setters>
        </VisualState>
    </VisualStateGroup>
</VisualStateManager.VisualStateGroups>
```

---

## 🐛 已知问题

1. **TreeView绑定**：需要实现完整的数据绑定逻辑
2. **右键菜单**：需要创建ContextMenu
3. **状态栏**：当前使用Title显示，建议添加StatusBar
4. **快捷键**：需要实现全局快捷键支持

---

## 📚 参考资源

### Python版本核心代码
- `main.py` - 主窗口和UI布局
- `ui/ui_components.py` - UI组件创建
- `core/image_processor.py` - 图像处理逻辑
- `database/database_manager.py` - 数据库操作

### WPF学习资源
- [Material Design In XAML](http://materialdesigninxaml.net/)
- [WPF Tutorial](https://www.wpf-tutorial.com/)
- [MVVM Pattern](https://docs.microsoft.com/en-us/dotnet/architecture/maui/mvvm)

---

## ✅ 验收标准

### 阶段1完成标准（2-3周后）
- [ ] 可以导入文件夹
- [ ] 项目树正确显示
- [ ] 双击加载图片
- [ ] 颜色变换功能正常
- [ ] 缩放滚动流畅
- [ ] 数据持久化到数据库

### 阶段2完成标准（5-6周后）
- [ ] 投影功能正常
- [ ] 媒体播放正常
- [ ] 基础关键帧功能

### 最终完成标准（20-25周后）
- [ ] 所有Python版本功能都已实现
- [ ] 性能优于Python版本
- [ ] UI更现代化
- [ ] 无明显Bug

---

**创建时间**: 2025-10-10  
**版本**: V1.0  
**作者**: Canvas Cast开发团队

---

## 🚀 快速开始

### 立即可以测试的功能
1. 运行程序
2. 点击"导入"按钮
3. 选择一张图片
4. 测试缩放功能（Ctrl+滚轮）
5. 点击"变色"按钮测试颜色效果
6. 右键"变色"按钮选择不同颜色

### 下一步要做的
1. 安装 `Microsoft.Data.Sqlite` NuGet包
2. 创建 `DatabaseService.cs` 文件
3. 实现基础的数据库操作
4. 实现文件夹导入功能

**祝开发顺利！** 🎉

