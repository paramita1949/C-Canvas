---
name: changelog-generator
description: 基于 git 历史自动生成或更新 CHANGELOG.md 文件。使用 /changelog 触发。
---

# Changelog Generator

根据 Git 提交历史自动生成 CHANGELOG.md 文件。

## 使用场景

- 发布新版本前生成更新日志
- 定期更新项目变更记录
- 遵循 Keep a Changelog 格式

## Step 1: 检查 Git 仓库

```bash
git rev-parse --git-dir 2>$null
```

**如果不是 Git 仓库：** 报告错误并停止。

## Step 2: 获取版本信息

```bash
# 获取所有标签（按版本排序）
git tag --sort=-v:refname

# 获取最新标签
git describe --tags --abbrev=0 2>$null

# 如果没有标签，使用第一个提交
git rev-list --max-parents=0 HEAD
```

## Step 3: 收集提交信息

```bash
# 获取自上次标签以来的提交
git log <last-tag>..HEAD --pretty=format:"%H|%s|%an|%ad" --date=short

# 或获取所有提交（如果没有标签）
git log --pretty=format:"%H|%s|%an|%ad" --date=short
```

## Step 4: 分类提交

根据 Conventional Commits 类型分类：

| 分类 | 包含的类型 | 图标 |
|------|-----------|------|
| ✨ Features | `feat` | 新功能 |
| 🐛 Bug Fixes | `fix` | 问题修复 |
| 📚 Documentation | `docs` | 文档更新 |
| 💄 Styles | `style` | 样式调整 |
| ♻️ Refactoring | `refactor` | 代码重构 |
| ⚡ Performance | `perf` | 性能优化 |
| ✅ Tests | `test` | 测试相关 |
| 🔧 Chores | `chore`, `build`, `ci` | 其他更改 |
| 💥 Breaking Changes | 带 `!` 或 `BREAKING CHANGE` | 破坏性变更 |

## Step 5: 生成 Changelog 格式

### Keep a Changelog 格式

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### ✨ Added
- New feature description (#issue)

### 🔄 Changed
- Changed feature description

### 🗑️ Deprecated
- Deprecated feature description

### 🗑️ Removed
- Removed feature description

### 🐛 Fixed
- Bug fix description (#issue)

### 🔒 Security
- Security fix description

## [1.0.0] - 2025-01-30

### ✨ Added
- Initial release features

[Unreleased]: https://github.com/user/repo/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/user/repo/releases/tag/v1.0.0
```

## Step 6: 检查现有 Changelog

```bash
# 检查是否存在 CHANGELOG.md
if (Test-Path "CHANGELOG.md") { 
    echo "EXISTS" 
} else { 
    echo "NOT_EXISTS" 
}
```

- **如果存在：** 读取现有内容，仅更新 `[Unreleased]` 部分
- **如果不存在：** 创建新文件，包含完整历史

## Step 7: 输出结果

```
## Changelog 更新

**时间范围:** [last-tag] → HEAD
**提交数量:** [N] 个提交
**分类统计:**
- ✨ Features: [X]
- 🐛 Bug Fixes: [Y]
- 📚 Documentation: [Z]

---

[生成的 Changelog 内容]

---

**已写入:** CHANGELOG.md
```

## 高级选项

### 按版本生成

用户可以指定版本范围：

```
/changelog v1.0.0..v2.0.0
```

### 指定输出格式

- `--format=markdown` (默认)
- `--format=json`
- `--format=html`

### 包含作者信息

```
/changelog --with-authors
```

输出：
```markdown
- Add dark mode support (#123) - @username
```

## 示例输出

```markdown
## [Unreleased]

### ✨ Added
- Add project creation wizard (#42)
- Implement dark mode toggle
- Add keyboard shortcuts for common actions

### 🐛 Fixed
- Fix null reference in database query (#38)
- Correct alignment issues in settings panel

### 📚 Documentation
- Update README with installation instructions
- Add API documentation

### ♻️ Refactored
- Reorganize project structure
- Extract common utilities to shared module
```
