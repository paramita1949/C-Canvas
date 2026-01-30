---
name: regex-builder
description: 交互式正则表达式构建和测试。使用 /regex 触发。
---

# Regex Builder

交互式构建和测试正则表达式。

## 使用方法

```
/regex <描述需求>
```

例如：
```
/regex 匹配邮箱地址
/regex 提取 URL 中的域名
/regex 验证中国手机号
```

## Step 1: 理解需求

分析用户描述，确定匹配目标。

## Step 2: 构建正则

### 常用模式

| 需求 | 正则 |
|------|------|
| 邮箱 | `^[\w.-]+@[\w.-]+\.\w+$` |
| 手机号 | `^1[3-9]\d{9}$` |
| URL | `https?://[\w.-]+(?:/[\w.-]*)*` |
| IP地址 | `\d{1,3}(?:\.\d{1,3}){3}` |

## Step 3: 测试验证

```csharp
var regex = new Regex(@"pattern");
var input = "test string";

// 测试匹配
bool isMatch = regex.IsMatch(input);

// 提取匹配
var matches = regex.Matches(input);
```

## Step 4: 输出

```
## 正则表达式

**需求:** 匹配邮箱地址
**正则:** `^[\w.-]+@[\w.-]+\.\w+$`

### 测试结果
✅ test@example.com
✅ user.name@domain.org
❌ invalid-email
```
