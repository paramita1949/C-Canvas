---
name: readme-generator
description: 根据项目结构自动生成专业的 README.md 文件。使用 /readme 触发。
---

# README Generator

分析项目结构并生成专业的 README.md 文件。

## Step 1: 分析项目结构

```bash
Get-ChildItem -Recurse -Depth 3 | Where-Object { $_.FullName -notmatch '(node_modules|bin|obj|\.git)' }
```

## Step 2: 生成 README 模板

```markdown
# 📦 项目名称

简短的项目描述。

## ✨ 特性

- 🚀 特性一
- 🎨 特性二

## 🛠️ 技术栈

- **框架:** WPF / .NET 8
- **数据库:** SQLite

## 🚀 快速开始

```bash
git clone https://github.com/username/project.git
cd project
dotnet restore
dotnet run
```

## 📁 项目结构

```
Project/
├── UI/           # 用户界面
├── Database/     # 数据库
├── Models/       # 数据模型
└── Services/     # 业务逻辑
```

## 📄 许可证

MIT License
```

## Step 3: 输出

生成 README.md 文件并询问用户是否需要自定义。
