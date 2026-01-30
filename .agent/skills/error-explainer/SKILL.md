---
name: error-explainer
description: 解析错误堆栈并提供详细的修复建议。使用 /error 触发，后跟错误信息。
---

# Error Explainer

解析错误消息和堆栈跟踪，提供清晰的解释和修复建议。

## 使用场景

- 遇到不理解的错误信息
- 需要快速定位问题根源
- 寻找最佳修复方案

## 使用方法

```
/error <粘贴错误信息>
```

或直接描述遇到的错误。

## Step 1: 识别错误类型

### 常见错误分类

| 类别 | 示例 | 优先级 |
|------|------|--------|
| 编译错误 | CS1002, CS0103 | 🔴 高 |
| 运行时异常 | NullReferenceException | 🔴 高 |
| 逻辑错误 | 结果不符合预期 | 🟡 中 |
| 配置错误 | 连接字符串无效 | 🟡 中 |
| 依赖错误 | 包版本冲突 | 🟠 中高 |
| 权限错误 | Access Denied | 🟡 中 |

## Step 2: 解析错误信息

### C# 编译错误格式
```
错误代码: CS[XXXX]
文件位置: file.cs(line, column)
错误描述: [message]
```

### 异常堆栈格式
```
Exception Type: System.XXXException
Message: [error message]
Stack Trace:
   at Namespace.Class.Method() in file.cs:line X
   at ...
```

## Step 3: 定位问题源头

1. **找到最内层调用**（堆栈顶部）
2. **识别你的代码**（排除框架代码）
3. **查看相关文件**（如果在项目中）

```bash
# 如果错误指向项目文件，查看该文件
# 使用 view_file 工具查看错误行附近的代码
```

## Step 4: 生成解释报告

```
## 🔴 错误分析

### 错误类型
**[错误代码/异常类型]**

### 简要说明
[用通俗语言解释这个错误的含义]

### 错误位置
- **文件:** [filename]
- **行号:** [line number]
- **方法:** [method name]

### 根本原因
[解释为什么会发生这个错误]

### 🔧 修复建议

#### 方案 1（推荐）
```csharp
// 修复代码示例
```

#### 方案 2（备选）
```csharp
// 替代方案
```

### 🛡️ 预防措施
- [如何避免将来再次发生]
- [最佳实践建议]

### 📚 参考资料
- [相关文档链接]
```

## 常见 C# 错误速查

### NullReferenceException
```
原因: 尝试访问值为 null 的对象成员
修复:
1. 添加 null 检查: if (obj != null)
2. 使用 null 条件运算符: obj?.Property
3. 使用 null 合并运算符: obj ?? defaultValue
```

### InvalidOperationException
```
原因: 对象当前状态不支持此操作
常见场景:
- 在 foreach 中修改集合
- DbContext 已被释放
- 异步操作未正确等待
```

### ArgumentNullException
```
原因: 传递了 null 给不接受 null 的参数
修复: 调用前验证参数不为 null
```

### IndexOutOfRangeException
```
原因: 数组/列表索引超出范围
修复: 检查索引是否在 0 到 Length-1 之间
```

### InvalidCastException
```
原因: 类型转换失败
修复: 
1. 使用 as 运算符: obj as TargetType
2. 使用 is 运算符检查: if (obj is TargetType)
```

### CS0103: 名称不存在
```
原因: 变量/类型未定义或未引用
修复:
1. 检查拼写
2. 添加 using 语句
3. 添加项目引用
```

### CS1061: 不包含定义
```
原因: 类型中不存在该成员
修复:
1. 检查成员名拼写
2. 确认使用正确的类型
3. 检查是否需要类型转换
```

## Step 5: 交互式帮助

如果需要更多上下文：

```
需要更多信息来定位问题：
1. 请提供完整的堆栈跟踪
2. 请提供出错代码附近的上下文
3. 这个错误是什么时候开始出现的？

请选择或提供更多信息。
```

## 示例

### 输入
```
System.NullReferenceException: Object reference not set to an instance of an object.
   at Canvas.UI.MainWindow.LoadProject(Int32 projectId) in D:\img\Canvas\UI\MainWindow.xaml.cs:line 156
```

### 输出
```
## 🔴 错误分析

### 错误类型
**NullReferenceException** (空引用异常)

### 简要说明
你正在尝试使用一个值为 null 的对象。就像试图打开一扇不存在的门。

### 错误位置
- **文件:** MainWindow.xaml.cs
- **行号:** 156
- **方法:** LoadProject(Int32 projectId)

### 根本原因
在 `LoadProject` 方法中，某个对象在使用前没有被正确初始化或赋值。

### 🔧 修复建议

#### 方案 1（推荐）- 添加 null 检查
```csharp
public void LoadProject(int projectId)
{
    var project = _dbContext.Projects.Find(projectId);
    if (project == null)
    {
        MessageBox.Show("项目不存在");
        return;
    }
    // 继续使用 project...
}
```

#### 方案 2 - 使用 null 条件运算符
```csharp
var projectName = project?.Name ?? "未命名项目";
```

### 🛡️ 预防措施
- 启用 C# 可空引用类型: `<Nullable>enable</Nullable>`
- 对所有外部输入进行验证
- 使用防御性编程模式
```
