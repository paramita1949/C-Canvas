---
name: snippet-library
description: 常用代码片段快速插入。使用 /snippet 触发。
---

# Snippet Library

快速插入常用代码片段。

## 使用方法

```
/snippet <name>
```

## 可用片段

### C# 通用

| 名称 | 描述 |
|------|------|
| `prop` | 自动属性 |
| `propfull` | 完整属性 |
| `ctor` | 构造函数 |
| `singleton` | 单例模式 |
| `try` | try-catch 块 |
| `using` | using 语句 |

### WPF

| 名称 | 描述 |
|------|------|
| `dp` | 依赖属性 |
| `cmd` | ICommand 实现 |
| `notify` | INotifyPropertyChanged |
| `converter` | 值转换器 |

### Entity Framework

| 名称 | 描述 |
|------|------|
| `dbcontext` | DbContext 类 |
| `entity` | 实体类 |
| `config` | 实体配置 |

## 示例

### singleton
```csharp
public sealed class MySingleton
{
    private static readonly Lazy<MySingleton> _instance = 
        new(() => new MySingleton());
    
    public static MySingleton Instance => _instance.Value;
    
    private MySingleton() { }
}
```

### cmd
```csharp
private ICommand _myCommand;
public ICommand MyCommand => _myCommand ??= 
    new RelayCommand(Execute, CanExecute);

private void Execute(object parameter) { }
private bool CanExecute(object parameter) => true;
```
