---
name: commit-message-generator
description: 根据 git diff 自动生成规范的 commit 消息，支持 Conventional Commits 格式。使用 /commit 触发。
---

# Commit Message Generator

根据当前的 git 更改自动生成规范的提交消息。

## 使用场景

- 当你完成代码更改，需要提交时
- 希望保持一致的提交消息风格
- 遵循 Conventional Commits 规范

## Step 1: 检查 Git 状态

```bash
git status --porcelain
```

**如果没有更改：** 报告 "没有需要提交的更改" 并停止。

## Step 2: 获取更改详情

```bash
# 获取更改统计
git --no-pager diff --stat HEAD 2>$null
if (-not $?) { git --no-pager diff --cached --stat }

# 获取更改的文件列表
git --no-pager diff --name-only HEAD 2>$null
if (-not $?) { git --no-pager diff --cached --name-only }

# 获取详细 diff（用于理解更改内容）
git --no-pager diff HEAD 2>$null
if (-not $?) { git --no-pager diff --cached }
```

## Step 3: 分析更改类型

根据更改内容确定提交类型：

| 类型 | 条件 |
|------|------|
| `feat` | 新增功能、新文件、新特性 |
| `fix` | 修复 bug、错误处理 |
| `docs` | 仅文档更改（.md, .txt, 注释） |
| `style` | 代码格式化、空格、分号（不影响逻辑） |
| `refactor` | 重构代码（不新增功能也不修复 bug） |
| `test` | 添加或修改测试 |
| `chore` | 构建过程、辅助工具、配置文件 |
| `perf` | 性能优化 |
| `ci` | CI/CD 配置更改 |
| `build` | 构建系统或外部依赖更改 |

## Step 4: 确定影响范围（Scope）

根据更改的文件路径确定 scope：

```
UI/          → ui
Database/    → db
Services/    → services
Models/      → models
Controllers/ → controllers
*.xaml       → xaml
*.config     → config
migrations/  → migrations
tests/       → tests
```

如果更改跨越多个目录，可以省略 scope 或使用通用名称。

## Step 5: 生成提交消息

### Conventional Commits 格式

```
<type>(<scope>): <subject>

<body>

<footer>
```

### 规则

1. **Subject（主题行）**
   - 使用祈使语气（"add" 而非 "added"）
   - 首字母小写
   - 不超过 50 个字符
   - 结尾不加句号

2. **Body（正文）** - 可选
   - 解释"为什么"而非"是什么"
   - 每行不超过 72 个字符
   - 用空行与主题行分隔

3. **Footer（页脚）** - 可选
   - 关联 Issue：`Closes #123`
   - Breaking Change：`BREAKING CHANGE: description`

## Step 6: 输出结果

向用户展示生成的提交消息：

```
## 建议的提交消息

**类型:** [type]
**范围:** [scope]
**更改摘要:** [X] 个文件，[+Y/-Z] 行

---

```
feat(ui): add dark mode toggle button

- Add toggle button to settings panel
- Implement theme switching logic
- Update color variables for dark mode
```

---

**是否使用此消息提交？** 输入 `yes` 确认，或提供修改建议。
```

## Step 7: 执行提交（可选）

如果用户确认：

```bash
git add -A
git commit -m "<generated message>"
```

## 示例输出

### 简单更改
```
fix(db): correct null reference in query
```

### 复杂更改
```
feat(ui): implement project creation wizard

- Add multi-step wizard dialog
- Implement template selection
- Add validation for project names
- Connect to database service

Closes #42
```

### Breaking Change
```
refactor(api)!: change authentication flow

BREAKING CHANGE: JWT tokens now expire after 1 hour instead of 24 hours.
Users will need to implement token refresh logic.
```
