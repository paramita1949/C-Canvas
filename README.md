# CCanvas - Canvas Cast 图片管理与投影系统

## 📝 项目简介

CCanvas 是一个功能强大的图片管理与投影系统，专为教学、演示和图片管理场景设计。

## ✨ 核心功能

### 1. 图片管理
- 📁 项目和文件夹层级管理
- 🔍 全局搜索功能
- 🏷️ 手动排序支持
- 📊 数据库持久化存储

### 2. 原图模式
- 🖼️ 智能原图标记系统
- 📐 多种显示模式（拉伸/适中）
- 🔄 自动切换原图/处理图
- 🎯 相似图片查找

### 3. 投影功能
- 🖥️ 双屏投影支持
- ⌨️ 键盘快捷键控制
- 🎬 流畅的图片切换
- 📏 智能缩放和滚动

### 4. 图片处理
- 🎨 黄字效果（智能背景检测）
- 🔄 图片缩放和旋转
- 💾 处理后图片保存
- 🎯 GPU 加速支持

## 🛠️ 技术栈

- **框架**: .NET 8.0 + WPF
- **数据库**: SQLite + Entity Framework Core
- **图片处理**: SixLabors.ImageSharp
- **GPU 加速**: ComputeSharp
- **UI 组件**: Material Design In XAML

## 📦 项目结构

```
CCanvas/
├── Core/                   # 核心功能
│   ├── Constants.cs       # 常量定义
│   ├── GPUProcessor.cs    # GPU 处理
│   └── ImageProcessor.cs  # 图片处理
├── Database/              # 数据库层
│   ├── CanvasDbContext.cs
│   ├── DatabaseManager.cs
│   └── Models/           # 数据模型
├── Managers/              # 业务管理器
│   ├── ImportManager.cs
│   ├── OriginalManager.cs
│   ├── ProjectionManager.cs
│   ├── SearchManager.cs
│   └── SortManager.cs
├── UI/                    # 用户界面
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
├── docs/                  # 项目文档
└── Canvas/                # Python 版本（旧版）
```

## 🚀 快速开始

### 编译项目

```bash
# Release 版本
dotnet build --configuration Release

# 或使用批处理文件
build.bat
```

### 运行程序

```bash
# 直接运行
dotnet run

# 或使用批处理文件
run.bat

# 或运行编译后的可执行文件
.\bin\Release\net8.0-windows\ImageColorChanger.exe
```

## 📚 文档

- [C# 重构完成总结](docs/C%23重构完成总结.md)
- [数据库结构说明](docs/数据库结构说明.md)
- [原图模式投影功能实现总结](docs/原图模式投影功能实现总结.md)
- [UI 重构说明](docs/UI重构说明.md)
- [图片加载性能优化分析](docs/图片加载性能优化分析.md)
- [性能优化完成总结](docs/性能优化完成总结.md)

## ⌨️ 快捷键

### 主窗口
- `Ctrl + O`: 打开图片
- `Ctrl + S`: 保存处理后的图片
- `F11`: 进入/退出投影模式
- `Left/Right`: 上一张/下一张图片
- `Ctrl + Mouse Wheel`: 缩放图片

### 投影窗口
- `Left/Right`: 上一张/下一张图片
- `Up/Down`: 向上/向下滚动
- `Home/End`: 滚动到顶部/底部
- `Space`: 切换暂停/播放
- `Esc`: 退出投影模式

## 🔧 配置要求

- **操作系统**: Windows 10/11
- **.NET**: .NET 8.0 Runtime
- **内存**: 建议 4GB 以上
- **显卡**: 支持 DirectX 11（用于 GPU 加速）

## 📋 版本历史

### v1.0-stable (当前版本)
- ✅ 完整的图片管理功能
- ✅ 原图模式和投影功能
- ✅ 数据库持久化
- ✅ 图片处理和保存
- ✅ 稳定的性能表现

## 🐛 已知问题

- 性能优化方案暂时回滚，等待进一步测试
- Canvas 子模块（Python 版本）需要单独管理

## 🔮 未来计划

- [ ] 性能优化（异步加载、直接像素传输）
- [ ] 预加载机制
- [ ] GPU 加速图片缩放
- [ ] 批量处理功能
- [ ] 更多图片效果

## 📄 许可证

本项目为私有项目。

## 👥 贡献

项目由团队内部维护。

## 📞 联系方式

如有问题请联系项目维护者。

---

**最后更新**: 2025-10-10

