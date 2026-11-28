# Canvas文档总索引

整理日期：2026-11-27。按当前主源码核对维护入口；主工程版本本次读取6.1.4.4，后续以工程文件为准。文档中的历史实测、设计方案和当前源码能力分开阅读，不能推断已安装或已发布版本同步。

## 一级目录

| 分类 | 内容与阅读顺序 |
|---|---|
| [使用说明](usage/README.md) | 日常操作、快捷键与可搜索HTML指南 |
| [源码架构](architecture/README.md) | 先看源码概览，再看画布比例 |
| [模块维护](maintenance/README.md) | 按合成、投影、字幕、在线诗歌本、结构谱、AI定位 |
| [开发规范](development/README.md) | 复用组件交互、样式与数据约束 |
| [发布与升级](release/README.md) | R2库存/校验与客户端升级维护；发布另由技能触发 |
| [独立研究与执行标准](research/README.md) | 上游谱码语法与离线分页/长页标准，不混作已集成功能 |
| [待实施/部分实施设计](design/README.md) | 保留仍有价值的设计，并标明后续已实现部分 |

## 快速入口

- 新同工：[快速上手](usage/README.md) → [服侍使用指南](usage/software-operations-and-shortcuts.html)。
- 开发定位：[源码概览](architecture/overview.md) → 对应模块维护；规则见 [AGENTS.md](../AGENTS.md) 和 [项目上下文技能](../skills/project-context/SKILL.md)。
- 结构谱：[分页/合页/长页统一规则](maintenance/structured-scores.md) → [诗歌本完整谱码参考](research/shigeben-structured-score-script-reference.md) → [时长设计](design/结构化歌谱_自动时长与合成循环_技术方案.md)；真实歌曲另看 [音频设计](design/歌曲播放与下载接口_Canvas接入技术方案.md)。
- 发布：[发布技能](../skills/canvas-release/SKILL.md) → [R2子步骤](release/r2-release-automation.md)。文档整理不触发升版/安装/提交/推送/发布。

## 文档维护规则

1. 每篇维护文档写明对象、当前源码入口和验证边界；版本从主工程读取，不固化成未来版本结论。
2. 新文档放入对应分类，同时更新分类README；新增分类再更新本页。保留分类与总索引返回链接。
3. 旧日期不直接等于无效；确认与当前源码冲突、被新标准替代或属于一次性执行记录时才归档。
4. 历史记录保持原文/哈希，不悄悄改写历史成功或失败；迁移后修正仍维护文档中的链接。
5. 只为文档变更核对内容、目录与链接，不运行无关业务构建，不复制工程做开发。

## 已淘汰内容与归档

首轮6份过期资料已移出主目录；全文复核另淘汰1份重复使用摘要，并用精简当前契约替换4份混合历史/旧架构长文。原文均可恢复，不再作为日常执行入口。完整原文、原因和SHA256见 [外部归档索引](D:/img/Canvas-archive/20261127-docs-restructure/ARCHIVE_INDEX.md)。

| 旧文档 | 处理/替代入口 |
|---|---|
| `NDI_UNIFIED_FLOW_2026-05-05.md` | [maintenance/projection-and-ndi.md](maintenance/projection-and-ndi.md)；旧图把业务枚举与发送源混为一层；用当前路由、枚举和字幕工作循环说明替代。 |
| `画布比例切换功能说明.md` | [architecture/canvas-aspect-ratio.md](architecture/canvas-aspect-ratio.md)；旧文写4:3为1600×1200及旧按钮/示例文件；实际分支为960×720。 |
| `update-endpoint-discovery-design.md` | [release/update-client-maintenance.md](release/update-client-maintenance.md)；旧四级发现/DNS/签名方案仍未整体实施；当前永久地址、网站策略下发和健康排序已不同。 |
| `live-caption-input-method-audit-and-v1-20261123.md` | [maintenance/live-caption.md](maintenance/live-caption.md)；一次性审计/回放记录不是持续维护入口；抽取当前源码职责，原结果完整归档。 |
| `shigeben-book1218-pagination-research.md` | [research/shigeben-book1218-rendering-standard.md](research/shigeben-book1218-rendering-standard.md)；整本未完成/482张阶段页/人工内存限制已被正式v4标准及全本结果替代。 |
| `shigeben-integration-technical-analysis.md` | [maintenance/shigeben-api-maintenance.md](maintenance/shigeben-api-maintenance.md)；初期6.1.3.9接口调查中的接入缺口/网络失败不是当前状态；维护手册已有后续纠正。 |

返回：[项目README](../README.md)

## 全文复核后的维护方式

2026-11-27复核覆盖docs全部24个文件正文及5份项目/技能入口与引用。旧架构或重复内容直接退出当前说明，不靠页首“历史”标签保留矛盾正文。未实施且仍适用的设计保留明确契约；客户历史日志、更新元数据和业务资源不属于技术资料淘汰范围。
