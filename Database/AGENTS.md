# DATABASE KNOWLEDGE BASE

继承根目录 `AGENTS.md`；本文件仅补充 `Database/` 局部规则。

## OVERVIEW
`Database/` 维护桌面端 SQLite 持久化：`CanvasDbContext` 负责主业务数据，`BibleDbContext` 负责圣经数据。

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| 主上下文与索引 | `Database/CanvasDbContext.cs` | 表定义、关系、索引、PRAGMA |
| 圣经上下文 | `Database/BibleDbContext.cs` | 圣经数据只读查询入口 |
| 兼容升级逻辑 | `Database/CanvasDbContext.cs`, `Database/DatabaseManager.cs` | `EnsureXxxColumnExists`/`ALTER TABLE` |
| 实体模型 | `Database/Models/*.cs` | 主业务实体 |
| 圣经模型 | `Database/Models/Bible/*.cs` | 书卷/经文/检索模型 |
| 传输对象与枚举 | `Database/Models/DTOs/*.cs`, `Database/Models/Enums/*.cs` | 非持久化载体 + 状态枚举 |

## CONVENTIONS
- 本仓库不走 EF Migration；新增字段走“启动时兼容升级”模式。
- 初始化流程基于 `EnsureCreated` + 手工 schema 检查，不直接依赖历史迁移快照。
- 与旧数据兼容优先，新增列默认值要保证老库可直接启动。
- SQLite 性能参数（WAL/NORMAL 等）属于生产假设，变更需评估启动与写入行为。

## ANTI-PATTERNS
- 禁止直接引入 `dotnet ef migrations` 作为默认升级路径。
- 禁止在未做兼容判断时直接 `ALTER TABLE` 假设列不存在。
- 禁止把 UI 展示 DTO 当实体写回数据库。
