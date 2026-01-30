---
name: migration-helper
description: 帮助数据库迁移和 Entity Framework 操作。使用 /migrate 触发。
---

# Migration Helper

协助 Entity Framework 数据库迁移操作。

## 常用命令

### 创建迁移
```bash
dotnet ef migrations add <MigrationName>
```

### 应用迁移
```bash
dotnet ef database update
```

### 回滚迁移
```bash
dotnet ef database update <PreviousMigrationName>
```

### 删除最后迁移
```bash
dotnet ef migrations remove
```

### 生成 SQL 脚本
```bash
dotnet ef migrations script
```

## Step 1: 检查迁移状态

```bash
dotnet ef migrations list
dotnet ef database update --dry-run
```

## Step 2: 分析模型变更

比较当前模型与数据库架构差异。

## Step 3: 生成迁移

根据变更自动生成迁移名称建议：
- `AddUserTable`
- `AlterProjectAddDescription`
- `RemoveObsoleteColumn`

## 安全检查

⚠️ 检测危险操作：
- 删除表
- 删除列
- 数据类型更改
