# 资源打包工具

## 概述
这个工具会在每次编译时自动将项目资源（字体、图片）打包成 `Resources.pak` 文件。

## 工作原理

### 1. 打包工具 (`PackResources.cs`)
- 独立的命令行工具
- 将指定资源打包成 PAK 文件
- 支持 GZip 压缩
- 包含资源：
  - `Fonts/` 目录下所有文件
  - `weixin.png`
  - `pay.png`

### 2. 自动编译流程
在主项目 `ImageColorChanger.csproj` 中配置了编译后事件：
```xml
<Target Name="PackResources" AfterTargets="Build">
  <Exec Command="dotnet build BuildTools\PackResources.csproj -c Release -o BuildTools\bin" />
  <Exec Command="dotnet BuildTools\bin\PackResources.dll . &quot;$(OutDir)Resources.pak&quot;" />
</Target>
```

每次编译时自动执行：
1. 编译打包工具
2. 运行打包工具生成 `Resources.pak`
3. 输出到 `bin\Debug\net8.0-windows\Resources.pak`

### 3. 资源加载
主程序通过 `ResourceLoader` 类自动检测：
- 如果存在 `Resources.pak`，从 PAK 加载资源
- 如果不存在，从文件系统加载资源（开发模式）

## 输出
- **位置**: `bin\Debug\net8.0-windows\Resources.pak` 或 `bin\Release\net8.0-windows\Resources.pak`
- **大小**: 约 36 MB（包含所有字体和图片）
- **格式**: 自定义 PAK 格式 (CCANVAS 魔数，版本 1)

## 优势
1. **自动化**: 无需手动打包
2. **一致性**: 每次编译都会重新打包
3. **透明**: 开发时仍可直接使用文件，打包后自动切换
4. **高效**: 仅打包需要的资源，支持压缩

