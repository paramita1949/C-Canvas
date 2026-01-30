---
name: architecture-reviewer
description: 检查代码架构是否符合最佳实践。使用 /arch 触发。
---

# Architecture Reviewer

检查项目架构，确保符合设计原则。

## Step 1: 分析项目结构

```bash
Get-ChildItem -Directory -Recurse -Depth 2
```

## Step 2: 检查设计原则

### SOLID 原则
- **S** 单一职责：每个类只有一个变化原因
- **O** 开闭原则：对扩展开放，对修改关闭
- **L** 里氏替换：子类可替换父类
- **I** 接口隔离：不强迫依赖不需要的接口
- **D** 依赖倒置：依赖抽象而非具体实现

### 分层架构检查
```
UI层 → 只能调用 Service 层
Service层 → 只能调用 Repository 层
Repository层 → 只能调用 Database 层
```

## Step 3: 生成报告

```
## 🏗️ 架构审查报告

### ✅ 符合的原则
- 分层清晰
- 依赖注入

### ⚠️ 建议改进
- UI 层存在直接数据库访问
- 缺少接口抽象
```
