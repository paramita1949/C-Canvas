---
name: code-comment-generator
description: 为函数/类生成 XML 文档注释（适合 C#）。使用 /comment 触发。
---

# Code Comment Generator

为 C# 代码自动生成 XML 文档注释。

## Step 1: 识别目标

读取当前光标所在的方法或类。

## Step 2: 分析代码

| 元素 | 对应标签 |
|------|----------|
| 类描述 | `<summary>` |
| 方法描述 | `<summary>` |
| 参数 | `<param name="">` |
| 返回值 | `<returns>` |
| 异常 | `<exception cref="">` |
| 示例 | `<example>` |

## Step 3: 生成注释

```csharp
/// <summary>
/// 根据 ID 获取项目
/// </summary>
/// <param name="projectId">项目的唯一标识符</param>
/// <returns>找到的项目对象，如果不存在则返回 null</returns>
/// <exception cref="ArgumentException">当 projectId 小于 0 时抛出</exception>
public Project GetProjectById(int projectId)
```

## Step 4: 插入注释

将生成的注释插入到目标代码上方。
