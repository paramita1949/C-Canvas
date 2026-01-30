---
name: refactor-assistant
description: 识别代码坏味道并建议重构方案。使用 /refactor 触发。
---

# Refactor Assistant

分析代码质量，识别坏味道，并提供重构建议。

## 使用场景

- 代码审查时发现问题
- 想要提高代码质量
- 学习重构技术

## 使用方法

```
/refactor                    # 分析当前打开的文件
/refactor <filename>         # 分析指定文件
/refactor <ClassName>        # 分析指定类
```

## Step 1: 读取目标代码

使用 `view_file` 或 `view_code_item` 工具获取代码内容。

## Step 2: 代码坏味道检测

### 检测清单

| 坏味道 | 严重程度 | 检测方法 |
|--------|----------|----------|
| 过长函数 | 🔴 高 | 方法 > 30 行 |
| 过大类 | 🔴 高 | 类 > 300 行 |
| 重复代码 | 🔴 高 | 相似代码块 |
| 过长参数列表 | 🟡 中 | 参数 > 4 个 |
| 数据泥团 | 🟡 中 | 多处相同参数组合 |
| 基本类型偏执 | 🟡 中 | 过度使用 string/int |
| Switch 语句 | 🟡 中 | 大型 switch/case |
| 过深嵌套 | 🟡 中 | 嵌套 > 3 层 |
| 注释过多 | 🟢 低 | 代码需要大量注释解释 |
| 死代码 | 🟢 低 | 未使用的代码 |
| 魔法数字 | 🟢 低 | 硬编码的数字 |
| 不一致命名 | 🟢 低 | 命名不符合规范 |

## Step 3: 分析结果报告

```
## 🔍 代码分析报告

**文件:** [filename]
**类数:** [N]
**方法数:** [M]
**总行数:** [L]

---

### 📊 代码健康度: [分数]/100

| 指标 | 值 | 状态 |
|------|-----|------|
| 平均方法长度 | X 行 | ✅/⚠️/❌ |
| 最大嵌套深度 | X 层 | ✅/⚠️/❌ |
| 圈复杂度 | X | ✅/⚠️/❌ |
| 重复代码率 | X% | ✅/⚠️/❌ |

---

### 🚨 发现的问题

#### 问题 1: [坏味道名称] 🔴
**位置:** 第 X-Y 行
**描述:** [问题描述]
**影响:** [对代码质量的影响]

---

### 🔧 重构建议
```

## Step 4: 提供重构方案

### 常见重构技术

#### 1. 提取方法 (Extract Method)
**适用于:** 过长函数、重复代码

```csharp
// Before
public void ProcessOrder(Order order)
{
    // 验证订单 - 20 行
    // 计算总价 - 15 行
    // 应用折扣 - 10 行
    // 保存订单 - 10 行
}

// After
public void ProcessOrder(Order order)
{
    ValidateOrder(order);
    var total = CalculateTotal(order);
    var finalPrice = ApplyDiscounts(total);
    SaveOrder(order, finalPrice);
}
```

#### 2. 提取类 (Extract Class)
**适用于:** 过大类、职责不单一

```csharp
// Before: Order 类包含太多职责
class Order
{
    // 订单属性
    // 价格计算逻辑
    // 折扣逻辑
    // 发货逻辑
}

// After: 分离职责
class Order { /* 订单核心属性 */ }
class PriceCalculator { /* 价格计算 */ }
class DiscountService { /* 折扣逻辑 */ }
class ShippingService { /* 发货逻辑 */ }
```

#### 3. 引入参数对象 (Introduce Parameter Object)
**适用于:** 过长参数列表

```csharp
// Before
void CreateUser(string name, string email, string phone, 
                string address, string city, string country)

// After
void CreateUser(UserInfo userInfo)

record UserInfo(string Name, string Email, string Phone, 
                Address Address);
```

#### 4. 用多态替代条件 (Replace Conditional with Polymorphism)
**适用于:** 大型 switch 语句

```csharp
// Before
decimal CalculateShipping(Order order)
{
    switch (order.ShippingType)
    {
        case "standard": return 5.99m;
        case "express": return 15.99m;
        case "overnight": return 25.99m;
    }
}

// After
interface IShippingStrategy
{
    decimal Calculate(Order order);
}

class StandardShipping : IShippingStrategy { ... }
class ExpressShipping : IShippingStrategy { ... }
```

#### 5. 卫语句 (Guard Clauses)
**适用于:** 过深嵌套

```csharp
// Before
void ProcessPayment(Payment payment)
{
    if (payment != null)
    {
        if (payment.IsValid)
        {
            if (payment.Amount > 0)
            {
                // 处理支付
            }
        }
    }
}

// After
void ProcessPayment(Payment payment)
{
    if (payment == null) return;
    if (!payment.IsValid) return;
    if (payment.Amount <= 0) return;
    
    // 处理支付
}
```

#### 6. 提取常量 (Extract Constant)
**适用于:** 魔法数字

```csharp
// Before
if (retryCount > 3) { ... }
Thread.Sleep(5000);

// After
private const int MaxRetryCount = 3;
private const int RetryDelayMs = 5000;

if (retryCount > MaxRetryCount) { ... }
Thread.Sleep(RetryDelayMs);
```

## Step 5: 输出完整报告

```
## 🔧 重构建议报告

### 问题 1: 过长函数 `ProcessOrder`

**当前状态:**
- 行数: 85 行
- 圈复杂度: 12
- 问题: 难以理解和维护

**建议: 提取方法**

将以下逻辑提取为独立方法:
1. `ValidateOrder()` - 第 10-25 行
2. `CalculateTotal()` - 第 26-45 行
3. `ApplyDiscounts()` - 第 46-60 行
4. `SaveOrder()` - 第 61-85 行

**重构后代码:**

```csharp
public async Task ProcessOrder(Order order)
{
    ValidateOrder(order);
    var total = CalculateTotal(order);
    var finalPrice = ApplyDiscounts(order, total);
    await SaveOrderAsync(order, finalPrice);
}
```

**收益:**
- ✅ 代码可读性提高
- ✅ 单元测试更容易
- ✅ 各部分可独立复用

---

### 📋 重构优先级

| 优先级 | 问题 | 预计时间 |
|--------|------|----------|
| 1 | 提取 ProcessOrder 方法 | 30 分钟 |
| 2 | 消除重复的验证逻辑 | 20 分钟 |
| 3 | 用策略模式替代 switch | 45 分钟 |

**总计:** 约 1.5 小时

需要我帮你执行某个重构吗？
```

## 自动重构（可选）

如果用户确认，可以自动执行简单重构：

1. **提取常量** - 自动将魔法数字替换为命名常量
2. **格式化代码** - 统一代码风格
3. **移除死代码** - 删除未使用的变量和方法
4. **简化条件** - 合并可合并的条件语句
