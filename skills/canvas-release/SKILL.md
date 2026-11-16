---
name: canvas-release
description: Use when the user requests a Canvas version release, says 更新版本 or 更新版本日志推送 in D:/img/Canvas, or resumes a pending Canvas release. Not for conceptual questions or unrelated projects.
---

# Canvas 全流程发布

在本仓库，“更新版本”默认按下列完整流程执行，不再让用户逐步下载、加密、上传或清理。**新的明确范围限制优先**：如“仅改版本不推送”“只上传网盘”“仅日志”，只执行该范围，不能触发其他发布动作。创建/修改本 Skill 不等于发布新版本。

## 客户更新日志（强制）

后续日志面向使用软件的客户，只写用户能感知的新增、优化和修复，不逐项公开工程细节。先按 [workflow.md 的客户日志规则](references/workflow.md#客户日志写作规则) 筛选、合并，再同步客户端、官网和 GitHub 的用户更新说明。发布流程、校验、归档及清理门禁不变；全部已批准功能改动仍需同步，不能因为日志省略内部细节而漏发代码。

## 固定顺序

执行前读取 [references/workflow.md](references/workflow.md)，确认当前版本、Git SHA、最近 checkpoint 和实际远端状态；旧报告不是当前版本默认值。

1. 主程序本次改动验证 → 版本与日志 → scoped commit / GitHub push。
2. 等待**同一 SHA**的 CI 成功 → 自动下载 `Canvas-Update.zip` → 核对资产摘要、大小及 DLL 版本/提交身份。
3. 用桌面**原始 `123.nrproj`**加密 `CanvasCast.dll` → 保护配置不变 → 版本/资源/UI smoke 验证 → 仅替换 DLL 重新压 ZIP。
4. 网盘 dry-run → 上传保护包 → 回下载全包哈希与元数据验证 → 自动最多保留**最新3包总数，包含当前版** → actual inventory 与 `versions.txt` 对齐。
5. 更新 `D:/img/cloudflare/cf-admin` 官网三页日志/版本/日期 → 测试 → deploy → 在线内容、哈希与视觉验证。
6. 记录所有验收事件和包归档 → **最后**运行 [scripts/cleanup_desktop.py](scripts/cleanup_desktop.py)，仅删除桌面 `Canvas-Update.zip`。

不要删 `123.nrproj`、其他桌面文件、事务证据或归档包。任何步骤失败/缺少验收，保留桌面 ZIP；禁止 `finally` 无条件清理。GitHub 原始 CI 包与网盘保护包是两个身份，不应拿原始包直接上传。

## 续行与完成门禁

- 有未完成事务：复用原版本、SHA、CI run、包哈希、四角色与已成功事件，只执行第一项未完成动作。不要重新升版本/重发 release。
- control-only “继续”：第一工具调用读取最小 checkpoint 后直接执行已绑定的 pending runner，不能先扫描正文再给计划。`controller_command` 只在与 pending gate 匹配时复用，不能误用旧 UI 测试命令作发布命令。
- checkpoint 保存 `ACTIVE_OBJECT / LAST_CONFIRMED_RESULT / NEXT_EXECUTABLE_ACTION / INPUT_PATHS / ACCEPTANCE_EVENT / status`；命令数组与原始 stdout/stderr/exit code 写到对应 gate JSON。
- **CI 成功不等于完整发布成功，deploy 成功不等于官网验收成功。** 清理 manifest 的 pass 只能来自本事务实际命令与内容核验，不能手填 pass 来绕过执行。
- 完成后重复“继续”只核验 cleanup receipt 和 absence，不重建桌面包、不发布下一版。
- 如果缺少认证、CI失败或身份不匹配，停止有副作用的后续步骤，保留包并记录具体失败。可安全重试临时网络错误，不无限重发上传/部署/删除。

## 结果与安装

报告实际版本/SHA/CI、保护包摘要、网盘3包库存、网站验收及桌面清理结果；安装客户端全启动/实际升级若未测，明确未验证。沿用当前事务四角色，补丁/rollback 仅证明本地副本恢复，不是生产回滚。

权威副本：`D:/img/Canvas/skills/canvas-release`；可发现安装：`C:/Users/Administrator/.codex/skills/canvas-release`。维护时同步并验证文件哈希，不依赖 memories 内的旧发布指南。
