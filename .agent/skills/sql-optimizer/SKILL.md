---
name: sql-optimizer
description: SQL 查询优化建议。使用 /sql 触发。
---

# SQL Optimizer

分析 SQL 查询并提供优化建议。

## 使用方法

```
/sql <粘贴 SQL 查询>
```

## Step 1: 分析查询

解析 SQL 语句结构：
- SELECT 子句
- FROM / JOIN
- WHERE 条件
- ORDER BY / GROUP BY

## Step 2: 检测问题

| 问题 | 影响 | 优化建议 |
|------|------|----------|
| SELECT * | 性能差 | 指定需要的列 |
| 缺少索引 | 全表扫描 | 添加索引 |
| N+1 查询 | 多次往返 | 使用 JOIN |
| 子查询 | 性能差 | 改用 JOIN |
| LIKE '%x' | 无法用索引 | 改变匹配模式 |

## Step 3: 生成建议

```
## 🔍 SQL 优化报告

### 原始查询
```sql
SELECT * FROM Users WHERE Name LIKE '%test%'
```

### 发现问题
1. ⚠️ 使用 SELECT * 
2. ⚠️ LIKE 以 % 开头

### 优化后
```sql
SELECT Id, Name, Email 
FROM Users 
WHERE Name LIKE 'test%'
```

### 索引建议
```sql
CREATE INDEX IX_Users_Name ON Users(Name)
```
```
