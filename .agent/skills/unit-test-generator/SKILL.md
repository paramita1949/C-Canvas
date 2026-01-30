---
name: unit-test-generator
description: 为指定的函数或类自动生成单元测试。使用 /test 触发，后跟类名或方法名。
---

# Unit Test Generator

为 C# 代码自动生成全面的单元测试。

## 使用场景

- 为新编写的代码添加测试
- 提高项目测试覆盖率
- 学习单元测试最佳实践

## 使用方法

```
/test <ClassName>           # 为整个类生成测试
/test <ClassName.MethodName> # 为特定方法生成测试
/test                       # 为当前打开的文件生成测试
```

## Step 1: 识别目标代码

```bash
# 查找目标类/方法
# 使用 grep_search 或 view_file 工具定位代码
```

## Step 2: 分析代码结构

收集以下信息：

| 信息 | 用途 |
|------|------|
| 类名 | 测试类命名 |
| 方法签名 | 测试方法设计 |
| 参数类型 | 测试数据准备 |
| 返回类型 | 断言设计 |
| 依赖项 | Mock 对象准备 |
| 异常情况 | 异常测试 |

## Step 3: 确定测试框架

检查项目使用的测试框架：

```bash
# 检查 .csproj 文件中的测试框架引用
# 常见框架：xUnit, NUnit, MSTest
```

### 框架对照表

| 框架 | 测试标记 | 断言类 |
|------|----------|--------|
| xUnit | `[Fact]`, `[Theory]` | `Assert.` |
| NUnit | `[Test]`, `[TestCase]` | `Assert.` |
| MSTest | `[TestMethod]` | `Assert.` |

**默认使用 xUnit**（如果未检测到）

## Step 4: 生成测试代码

### 测试命名规范

```
方法名_场景_预期结果
```

示例：
- `Add_TwoPositiveNumbers_ReturnsSum`
- `GetUser_InvalidId_ThrowsNotFoundException`
- `Save_ValidEntity_ReturnsTrue`

### 测试结构（AAA 模式）

```csharp
[Fact]
public void MethodName_Scenario_ExpectedResult()
{
    // Arrange - 准备测试数据
    var sut = new SystemUnderTest();
    var input = "test input";
    
    // Act - 执行被测方法
    var result = sut.MethodUnderTest(input);
    
    // Assert - 验证结果
    Assert.Equal(expected, result);
}
```

## Step 5: 生成测试类模板

```csharp
using Xunit;
using Moq;
using FluentAssertions;

namespace Canvas.Tests
{
    public class [ClassName]Tests
    {
        private readonly Mock<IDependency> _mockDependency;
        private readonly [ClassName] _sut;

        public [ClassName]Tests()
        {
            _mockDependency = new Mock<IDependency>();
            _sut = new [ClassName](_mockDependency.Object);
        }

        #region [MethodName] Tests

        [Fact]
        public void [MethodName]_ValidInput_ReturnsExpectedResult()
        {
            // Arrange
            var input = /* test data */;
            var expected = /* expected result */;

            // Act
            var result = _sut.[MethodName](input);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void [MethodName]_NullInput_ThrowsArgumentNullException()
        {
            // Arrange
            string input = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _sut.[MethodName](input));
        }

        [Theory]
        [InlineData("input1", "expected1")]
        [InlineData("input2", "expected2")]
        public void [MethodName]_VariousInputs_ReturnsCorrectResults(string input, string expected)
        {
            // Act
            var result = _sut.[MethodName](input);

            // Assert
            result.Should().Be(expected);
        }

        #endregion
    }
}
```

## Step 6: 测试类型覆盖

为每个方法生成以下类型的测试：

### 1. 正常路径测试 ✅
```csharp
[Fact]
public void Method_ValidInput_Succeeds()
```

### 2. 边界值测试 📏
```csharp
[Theory]
[InlineData(0)]
[InlineData(int.MaxValue)]
[InlineData(int.MinValue)]
public void Method_BoundaryValues_HandlesCorrectly(int value)
```

### 3. 空值/空集合测试 🚫
```csharp
[Fact]
public void Method_NullInput_ThrowsException()

[Fact]
public void Method_EmptyCollection_ReturnsEmpty()
```

### 4. 异常测试 💥
```csharp
[Fact]
public void Method_InvalidState_ThrowsInvalidOperationException()
```

### 5. 异步测试 ⏳
```csharp
[Fact]
public async Task MethodAsync_ValidInput_CompletesSuccessfully()
{
    // Act
    var result = await _sut.MethodAsync(input);
    
    // Assert
    result.Should().NotBeNull();
}
```

## Step 7: 输出结果

```
## 🧪 单元测试生成完成

**目标:** [ClassName].[MethodName]
**测试框架:** xUnit
**生成测试:** [N] 个

### 测试覆盖

| 测试类型 | 数量 |
|----------|------|
| ✅ 正常路径 | X |
| 📏 边界值 | X |
| 🚫 空值处理 | X |
| 💥 异常情况 | X |
| ⏳ 异步操作 | X |

### 生成的测试文件

**路径:** `Tests/[ClassName]Tests.cs`

---

[生成的测试代码]

---

**下一步:**
1. 运行 `dotnet test` 验证测试
2. 根据需要调整测试数据
3. 添加更多边界条件测试
```

## Mock 对象指南

### 使用 Moq 创建 Mock

```csharp
// 创建 Mock
var mockService = new Mock<IProjectService>();

// 设置方法返回值
mockService.Setup(x => x.GetById(It.IsAny<int>()))
           .Returns(new Project { Id = 1, Name = "Test" });

// 设置异步方法
mockService.Setup(x => x.GetByIdAsync(It.IsAny<int>()))
           .ReturnsAsync(new Project { Id = 1 });

// 验证方法被调用
mockService.Verify(x => x.Save(It.IsAny<Project>()), Times.Once);
```

### DbContext Mock

```csharp
// 使用 InMemory 数据库
var options = new DbContextOptionsBuilder<CanvasDbContext>()
    .UseInMemoryDatabase(databaseName: "TestDb")
    .Options;

using var context = new CanvasDbContext(options);
context.Projects.Add(new Project { Id = 1, Name = "Test" });
context.SaveChanges();
```

## 示例输出

### 输入
```
/test CanvasDbContext.GetProjectById
```

### 输出
```csharp
public class CanvasDbContextTests
{
    private readonly CanvasDbContext _context;

    public CanvasDbContextTests()
    {
        var options = new DbContextOptionsBuilder<CanvasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new CanvasDbContext(options);
    }

    [Fact]
    public async Task GetProjectById_ExistingId_ReturnsProject()
    {
        // Arrange
        var project = new Project { Id = 1, Name = "Test Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        // Act
        var result = await _context.GetProjectById(1);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test Project");
    }

    [Fact]
    public async Task GetProjectById_NonExistingId_ReturnsNull()
    {
        // Act
        var result = await _context.GetProjectById(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectById_NegativeId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _context.GetProjectById(-1));
    }
}
```
