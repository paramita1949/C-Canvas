# R2 自动更新服务器配置说明

## 目录结构

```
https://canvas.019890311.xyz/
├── latest.txt              # 最新版本号文件
└── v5.3.5/                 # 版本目录
    ├── files.txt           # 文件列表（推荐）
    ├── CanvasCast.exe      # 主程序
    ├── CanvasCast.dll      # 核心DLL
    └── Resources.pak       # 资源包
```

## 1. latest.txt 配置

文件内容：当前最新版本号（纯文本）

**示例：**
```
5.3.5
```

**注意：**
- 只包含版本号，不要带 `V` 前缀
- 不要有空行或其他内容
- 格式：`Major.Minor.Build`（如 `5.3.5`）

## 2. files.txt 配置

文件位置：`https://canvas.019890311.xyz/v{版本号}/files.txt`

**示例内容：**
```
CanvasCast.exe
CanvasCast.dll
Resources.pak
```

**规则：**
- 每行一个文件名
- 自动去除行首尾空格
- 空行会被忽略
- 支持子目录（如 `subfolder/file.dll`）

## 3. 如果不配置 files.txt

程序会自动尝试发现以下文件（按优先级）：

1. **压缩包：**
   - `update.zip`
   - `update.7z`
   - `update.rar`
   - `Canvas-Update.zip`
   - `Canvas-Full.zip`

2. **主程序文件：**
   - `CanvasCast.dll`
   - `CanvasCast.exe`

3. **资源文件：**
   - `Resources.pak`

4. **配置文件：**
   - `appsettings.json`
   - `config.json`

## 4. 发布新版本步骤

### 方式一：使用完整更新包（推荐）

1. 在 R2 创建新版本目录，如 `v5.3.6/`
2. 上传 `Canvas-Update.zip` 到该目录
3. 创建 `files.txt` 内容：
   ```
   Canvas-Update.zip
   ```
4. 更新 `latest.txt` 为 `5.3.6`

**优点：** 程序会自动解压 ZIP，支持批量更新所有文件

### 方式二：直接上传文件

1. 在 R2 创建新版本目录，如 `v5.3.6/`
2. 上传以下文件：
   - `CanvasCast.exe`
   - `CanvasCast.dll`
   - `Resources.pak`
3. 创建 `files.txt` 内容：
   ```
   CanvasCast.exe
   CanvasCast.dll
   Resources.pak
   ```
4. 更新 `latest.txt` 为 `5.3.6`

### 方式三：不创建 files.txt（简化部署）

1. 在 R2 创建新版本目录，如 `v5.3.6/`
2. 上传 `Canvas-Update.zip`（程序会自动发现）
3. 更新 `latest.txt` 为 `5.3.6`

## 5. 更新流程说明

### 用户端更新流程：

1. 程序启动时检查更新
2. 从 `latest.txt` 读取最新版本号
3. 与本地版本（从 `AssemblyVersion` 读取）比较
4. 如果有新版本：
   - 从 `files.txt` 读取文件列表
   - 如果 `files.txt` 不存在，自动发现文件
   - 下载所有文件到临时目录
   - 如果是 ZIP 文件，自动解压
5. 用户点击更新：
   - 创建 BAT 更新脚本
   - 关闭程序
   - BAT 脚本等待进程完全退出（最多30秒）
   - 备份旧文件（.bak）
   - 复制新文件（包括 EXE、DLL、PAK）
   - 如果失败，恢复备份
   - 重启程序

### 更新脚本特性：

✅ 等待进程完全退出（通过进程ID检测）
✅ 支持 EXE 文件更新
✅ 自动备份和回滚
✅ 静默执行（无窗口）
✅ 自动清理临时文件

## 6. 常见问题

### Q: EXE 文件无法更新？
A: 已修复！现在更新脚本会等待进程完全退出后再更新 EXE。

### Q: 为什么推荐使用 ZIP 包？
A: ZIP 包可以包含任意文件和子目录结构，更灵活。程序会自动解压。

### Q: 如何只更新 DLL 不更新 EXE？
A: 在 `files.txt` 中只列出需要更新的文件：
```
CanvasCast.dll
Resources.pak
```

### Q: 支持增量更新吗？
A: 是的。`files.txt` 中只列出需要更新的文件即可。

## 7. GitHub Actions 自动发布

GitHub Actions 会自动：
- 从 `.csproj` 读取版本号
- 编译项目
- 创建两个发布包：
  - `Canvas-Full.zip` - 完整包（所有文件）
  - `Canvas-Update.zip` - 更新包（EXE + DLL + PAK）
- 创建 GitHub Release

**需要手动：**
- 将 `Canvas-Update.zip` 上传到 R2 的对应版本目录
- 更新 R2 的 `latest.txt`
- 可选：创建 `files.txt`

## 8. 版本号管理

**单一配置源：** `ImageColorChanger.csproj`

```xml
<Version>5.3.5</Version>               <!-- GitHub Actions 读取 -->
<FileVersion>5.3.5</FileVersion>       <!-- Windows 文件属性 -->
<AssemblyVersion>5.3.5</AssemblyVersion> <!-- 程序更新检测 -->
```

**发布新版本时：** 只需修改 `.csproj` 中的三个版本号即可！

