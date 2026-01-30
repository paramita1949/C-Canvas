---
name: dependency-analyzer
description: 分析项目依赖关系，检测过时包和安全漏洞。使用 /deps 触发。
---

# Dependency Analyzer

分析项目依赖，检测过时版本和安全问题。

## Step 1: 读取依赖文件

```bash
# .NET 项目
dotnet list package

# 检查过时包
dotnet list package --outdated

# 检查安全漏洞
dotnet list package --vulnerable
```

## Step 2: 分析结果

| 状态 | 说明 |
|------|------|
| ✅ 最新 | 已是最新版本 |
| ⚠️ 过时 | 有新版本可用 |
| 🔴 漏洞 | 存在已知安全漏洞 |

## Step 3: 生成报告

```
## 📦 依赖分析报告

### 过时的包
| 包名 | 当前版本 | 最新版本 |
|------|----------|----------|
| Newtonsoft.Json | 12.0.3 | 13.0.3 |

### 安全漏洞
| 包名 | 漏洞等级 | CVE |
|------|----------|-----|
| System.Text.Json | 中 | CVE-2024-xxx |

### 建议操作
1. 运行 `dotnet add package Newtonsoft.Json` 更新
```

## Step 4: 可选操作

询问用户是否自动更新依赖。
