---
name: pr-description-writer
description: 自动生成 Pull Request 描述和摘要。使用 /pr 触发。
---

# PR Description Writer

根据分支差异自动生成专业的 Pull Request 描述。

## 使用场景

- 创建 Pull Request 前生成描述
- 确保 PR 描述完整且专业
- 节省编写 PR 描述的时间

## Step 1: 获取分支信息

```bash
# 当前分支名
git branch --show-current

# 目标分支（通常是 main 或 develop）
# 用户可以指定，默认为 main
$targetBranch = "main"
```

## Step 2: 收集更改信息

```bash
# 获取提交列表
git log $targetBranch..HEAD --pretty=format:"%s" --reverse

# 获取文件更改统计
git diff --stat $targetBranch..HEAD

# 获取更改的文件列表
git diff --name-only $targetBranch..HEAD

# 获取详细 diff
git diff $targetBranch..HEAD
```

## Step 3: 分析更改类型

确定 PR 的主要类型：

| 类型 | 标签 | 条件 |
|------|------|------|
| Feature | `enhancement` | 新增功能 |
| Bug Fix | `bug` | 修复问题 |
| Documentation | `documentation` | 仅文档更改 |
| Refactoring | `refactor` | 代码重构 |
| Testing | `testing` | 测试相关 |
| Dependencies | `dependencies` | 依赖更新 |
| Breaking Change | `breaking` | 包含破坏性变更 |

## Step 4: 生成 PR 模板

### 标准 PR 模板

```markdown
## 📋 描述

[简要描述此 PR 的目的和更改内容]

## 🔄 更改类型

- [ ] ✨ 新功能 (New feature)
- [ ] 🐛 问题修复 (Bug fix)
- [ ] 📚 文档更新 (Documentation)
- [ ] ♻️ 代码重构 (Refactoring)
- [ ] ⚡ 性能优化 (Performance)
- [ ] ✅ 测试 (Tests)
- [ ] 🔧 配置/构建 (Chores)
- [ ] 💥 破坏性变更 (Breaking change)

## 📝 更改详情

### 新增
- [列出新增的功能/文件]

### 修改
- [列出修改的内容]

### 删除
- [列出删除的内容]

## 🧪 测试

- [ ] 已添加/更新相关测试
- [ ] 所有测试通过
- [ ] 已在本地测试功能

### 测试步骤
1. [描述如何测试此更改]
2. [预期结果]

## 📸 截图（如适用）

[如果有 UI 更改，添加前后对比截图]

## 🔗 相关 Issue

Closes #[issue number]

## ✅ 检查清单

- [ ] 代码符合项目规范
- [ ] 已自我审查代码
- [ ] 已添加必要的注释
- [ ] 文档已更新（如需要）
- [ ] 没有引入新的警告
- [ ] 已考虑向后兼容性

## 💡 其他说明

[任何额外的上下文或注意事项]
```

## Step 5: 智能填充内容

根据分析结果自动填充模板：

1. **描述**：根据提交消息和 diff 生成摘要
2. **更改类型**：自动勾选对应类型
3. **更改详情**：从文件列表和 diff 提取
4. **相关 Issue**：从提交消息中提取 `#number`

## Step 6: 输出结果

```
## Pull Request 描述

**分支:** [current-branch] → [target-branch]
**提交:** [N] 个提交
**文件:** [M] 个文件更改

---

[生成的 PR 描述]

---

**操作选项:**
1. 复制到剪贴板
2. 直接创建 PR（需要 GitHub CLI）
3. 保存到文件
```

## 高级功能

### 使用 GitHub CLI 创建 PR

```bash
# 如果安装了 gh CLI
gh pr create --title "<title>" --body "<body>"
```

### 指定目标分支

```
/pr --base develop
```

### 包含 diff 摘要

```
/pr --include-diff
```

## 示例输出

```markdown
## 📋 描述

本 PR 实现了项目创建向导功能，允许用户通过多步骤向导创建新项目。包含模板选择、项目配置和验证逻辑。

## 🔄 更改类型

- [x] ✨ 新功能 (New feature)

## 📝 更改详情

### 新增
- `UI/Dialogs/ProjectWizard.xaml` - 向导对话框 UI
- `UI/Dialogs/ProjectWizard.xaml.cs` - 向导逻辑
- `Services/ProjectTemplateService.cs` - 模板服务
- 3 个新的单元测试

### 修改
- `MainWindow.xaml.cs` - 添加向导触发逻辑
- `Database/CanvasDbContext.cs` - 添加项目模板表

## 🧪 测试

- [x] 已添加/更新相关测试
- [x] 所有测试通过

### 测试步骤
1. 点击 "新建项目" 按钮
2. 选择模板并填写项目名称
3. 确认项目创建成功

## 🔗 相关 Issue

Closes #42

## ✅ 检查清单

- [x] 代码符合项目规范
- [x] 已自我审查代码
- [x] 已添加必要的注释
```
