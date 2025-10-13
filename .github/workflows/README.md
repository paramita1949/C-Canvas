# GitHub Actions 工作流说明

## 📦 自动构建和发布流程

### 触发条件

1. **推送到 main 分支** - 自动构建开发版本
2. **创建版本标签** (如 `v1.0.0`) - 自动构建并创建 GitHub Release
3. **Pull Request** - 验证构建
4. **手动触发** - 在 GitHub Actions 页面手动运行

### 发布新版本步骤

```bash
# 1. 更新版本号并提交
git add .
git commit -m "chore: 发布 v1.0.0"

# 2. 创建版本标签
git tag -a v1.0.0 -m "版本 1.0.0"

# 3. 推送到 GitHub
git push origin main
git push origin v1.0.0

# GitHub Actions 会自动：
# - 编译项目
# - 打包成 zip
# - 创建 GitHub Release
# - 上传安装包
```

### 版本号规范

- `v1.0.0` - 正式版本
- `v1.0.0-beta.1` - 测试版本
- `v1.0.0-rc.1` - 候选版本

### 构建产物

- **开发构建**: `Canvas-dev-yyyyMMdd-HHmmss-win-x64.zip`
- **正式版本**: `Canvas-1.0.0-win-x64.zip`

### 系统要求

- Windows 10 或更高版本
- .NET 8.0 Runtime

## 🔧 配置文件说明

### build-release.yml

主要工作流文件，执行以下任务：

1. ✅ 检出代码
2. ✅ 设置 .NET 环境
3. ✅ 还原 NuGet 包
4. ✅ 编译项目
5. ✅ 发布项目
6. ✅ 创建发布包
7. ✅ 上传构建产物
8. ✅ 创建 GitHub Release（仅标签触发）

### 环境变量

- `PROJECT_NAME`: 项目名称（ImageColorChanger）
- `OUTPUT_NAME`: 输出名称（Canvas）
- `DOTNET_VERSION`: .NET 版本（8.0.x）

## 📝 注意事项

1. 确保项目能在 `Release` 配置下成功编译
2. 标签格式必须是 `v*`（如 `v1.0.0`）
3. GitHub Release 会自动生成变更记录
4. 构建产物保留 30 天

## 🚀 下次发布检查清单

- [ ] 更新 `updata.txt` 中的版本说明
- [ ] 确保所有测试通过
- [ ] 更新文档（如有需要）
- [ ] 创建版本标签并推送
- [ ] 验证 GitHub Release 创建成功
- [ ] 测试下载的安装包

