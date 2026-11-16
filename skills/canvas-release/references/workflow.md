# 执行接口与证据

## 项目和身份

- TARGET：`D:/img/Canvas`；技术项目 `ImageColorChanger.csproj`、输出 `CanvasCast.dll`。
- 用 Git remote 实时确认仓库，目前 `paramita1949/C-Canvas`。不硬编码历史版本、build号、SHA 或日期。
- 新版本按当前四段版本递增末段（用户明确版本优先）。元数据同步 `Version/FileVersion/AssemblyVersion`、`CHANGELOG.md`、`updata/latest.txt`（纯版本）、`updata/read.txt`（首行 `V版本`，后跟本次日志）。日期来自本次 changelog。
- `updata/versions.txt` 是网盘实际可下载包索引，不是 Git push 时臆造的发布历史。commit 使用明确 allowlist，不 `git add .`，不动无关 dirty 文件。
- runtime 变更按仓库 gate 验证场景与 Release build；版本/文案-only 不跑无关应用套件。

## 客户日志写作规则

适用：后续 `CHANGELOG.md` 的版本说明、`updata/read.txt`、官网三页更新说明及 GitHub Release 的用户更新说明。工程记录留在提交、验证报告及事务证据中，不复制到客户日志。默认不改已发布的历史日志，除非用户另行要求。

1. **按客户价值筛选**：只写用户能感知的功能新增、使用方式调整、体验改善和问题修复。全量同步代码≠逐项公开工程细节；同一功能的多个修改合并为一条。
2. **简洁有重点**：通常3—5条，默认不超过6条；小更新1—2条即可，不凑条数。每条一句话，优先20—45字，确需说明时可稍长，不展开实现过程。重要变化在前，细小调整省略。
3. **分类清楚**：用【新增】、【优化】、【修复】开头；只有实际存在的类别才出现。描述结构为“功能名称＋用户得到的变化”，不强行填满三类。
4. **工程细节不进入客户日志**：不写文件名、函数、字段、WPF、解析解、阻尼参数、帧时间、缓存生命周期、异步回调、哈希、测试数、CI、加密和部署步骤。用户必须操作的文件名或兼容性条件除外。GitHub自动构建元数据可独立保留，不混进用户更新条目。
5. **准确而不夸大**：禁止无依据的“彻底解决”“全面提升”“零卡顿”及性能百分比；不能把内部重构包装成新增功能。没有明确用户影响的调整不写“优化若干细节”等空话。
6. **保留必要提醒**：默认设置变化、功能移除、操作入口变化、兼容性要求或需要客户操作时必须单独提醒，简要说明“影响谁、发生什么、要不要操作”；普通用户不必关心的数值省略。新用户默认变化要与已有用户设置是否保留区分，不承诺未经确认的设置保留。
7. **各渠道一致**：先形成同一份客户文案，再同步客户端、官网和 GitHub 用户说明；允许编号、Markdown及HTML形式不同，不允许功能含义不同。首页横幅只写最重要的一句，不塞完整日志。版本和日期独立显示。

以6.1.3.4为例，后续写法可参考（仅示例，不重新发布历史版本）：

- 【新增】经文柔滑滚动，提供三档速度，默认使用1档。
- 【优化】简化经文滚动菜单，选择档位即可切换滚动方式。
- 【优化】改善大图加载与幻灯片切换体验。
- 【优化】调整新建配置的圣经默认设置，已有设置保持不变。

示例若用于正式日志，需先确认对应事实；需要客户操作的提醒另起一行，不另造工程分类。

发布前只需做一次文案筛选：客户能看懂吗？能知道哪个功能变了以及有何影响吗？是否混入工程细节、重复或无依据承诺？无用户影响的条目删去，不因此删除源码改动或跳过发布门禁。

## GitHub 到保护包

用 `gh auth status`、`gh run list/view --json headSha,status,conclusion` 找**本次提交**，等待该 run success。通过 `gh api repos/{owner}/{repo}/releases/tags/{tag}` 核对 assets；`gh release download {tag} --pattern Canvas-Update.zip --dir {run}/CI-original` 下载，核验 GitHub digest、size。若 digest 未提供，记录缺项，不能宣称已匹配官方 digest。

从包 DLL 读取 File/AssemblyVersion 与 ProductVersion（含 Git SHA），不信桌面文件名。原始包归档后解到隔离 CI-input，保护输出独立目录。

```powershell
& 'C:/Program Files (x86)/Eziriz/.NET Reactor/dotNET_Reactor.Console.exe' `
  -project 'C:/Users/Administrator/Desktop/123.nrproj' `
  -file "$run/CI-input/CanvasCast.dll" `
  -targetfile "$run/protected/CanvasCast.dll" -nodialog -licensed
```

前后记录 `123.nrproj` SHA256，**不改保护配置**。保护 DLL 不等于 DLL 换名；要求输出非空、hash改变、版本与 commit保留。原 ZIP 重新打包仅替换准确 entry `CanvasCast.dll`；entry集合/顺序与所有其他entry字节不变，CRC通过。分别记录 original ZIP、protected DLL、protected ZIP 三种哈希。

保护后 runtime smoke：真实入口 `ImageColorChanger.App.Main` 能加载资源/UpdateWindow。已有可复用 smoke 模板在 `D:/img/Canvas/release-txn/update-download-ui-20261112/release-61210/smoke.py` 与 `smoke/Program.cs`。读取后在当前 run 适配路径，不直接重跑历史发布脚本。该模板仅拦截首个 Priority.Send 业务启动回调；不得构造 App 后无限制 pump 触发 DB/主窗口；外部 library-host 不足以证明真实入口资源失效。明确这不证明安装客户端完整启动/升级。

## R2 上传与保留

上传配置 `D:/img/cloudflare/Cloudflare-R2/scripts/version-upload.config.json`（只读使用，不输出密码/token）。桌面包如已存在且身份不同，先归档，不覆盖不明文件。把当前保护包放至配置要求的包路径；核对 config 指向此版本包/metadata。

```powershell
& $pwsh -NoProfile -File 'D:/img/cloudflare/Cloudflare-R2/scripts/upload-version-package.ps1' `
  -Version $version -ConfigPath 'D:/img/cloudflare/Cloudflare-R2/scripts/version-upload.config.json' -DryRun
# dry-run exit0且目标正确后，相同命令去掉-DryRun
```

`$pwsh` 使用可用 PowerShell 7（本机可用路径 `C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe`）。设置 `$ProgressPreference='SilentlyContinue'`。

上传器自动调用 sibling `prune-update-packages.ps1`。必须先完整下载新包验证 SHA256，再 fresh inventory 和 latest复核，保留最新三个严格 `vX.Y.Z.W/Canvas-Update.zip`，旧目录文件先于folder marker删除。保留根目录无关文件与官网历史。并发 latest改变则不得删除。

fresh GET验证：`https://pan.yongmu.com.cn/raw/` 的 latest.txt、versions.txt、v版本/files.txt、v版本/read.txt 和全 ZIP，对应本地哈希相等；`/api/children/` 与各版本 children的实际包集合等于索引且≤3。若不足3，保留全部。永久客户端地址 `https://pan.019890311.xyz/raw/latest.txt` 也要 HTTP200且版本一致。

## 官网

唯一源 `D:/img/cloudflare/cf-admin`，不用 Canvas 镜像。修改 `public/index.html`、`public/bible-projection.html`、`public/changelog.html`：本次全部日志、日期、首页 softwareVersion/datePublished/footer、single current card/history排序。

在源仓库运行 `npm test`、scoped `git diff --check`、`npx --no-install wrangler whoami`，然后：
```text
npx --no-install wrangler pages deploy public --project-name auth-cf --branch master --commit-dirty=true
```
未请求官网 Git push则不额外提交官网仓库。在线三个 `.html` 页面 HTTP200、版本/日期/全部notes、历史/current唯一性与源文件SHA256相同，截图确认页面。仅deployment URL不算通过。

## 最后清理

先保留保护 ZIP 到当前 run，生成 `release-complete.json`。清理 helper检查证据文件digest与命令exit0，但**不替代 executor 核验命令结果和每项语义**。不要把一次无关exit0作为全部gates证据。

manifest 必须包含：
- `version` 四段版本，`commit` 40位SHA；
- `package_path` 当前run归档保护ZIP绝对路径，`package_sha256`；
- `project_path` 原桌面 `123.nrproj` 绝对路径，`project_sha256`；
- `gates` 以下全部key：`source_verification, github_push, ci_asset, reactor_protection, protected_runtime, dll_only_zip, r2_upload, r2_verify, retention3, site_tests, site_deploy, site_live`；
- 每gate `{ "status": "pass", "evidence_path": "ABSOLUTE_GATE_JSON", "evidence_sha256": "SHA256" }`。gate JSON须有本次实际 `command` 数组、`stdout`、`stderr`、`exit_status:0`；内容核验可用单独验证命令。

```text
python -X utf8 D:/img/Canvas/skills/canvas-release/scripts/cleanup_desktop.py --manifest ABSOLUTE_RELEASE_COMPLETE_JSON --receipt ABSOLUTE_DESKTOP_CLEANUP_JSON
```

默认Desktop为当前用户Desktop。`--desktop` 仅供fixture测试或用户明确指定的桌面目录。只unlink确切 `Canvas-Update.zip`，不递归扫描/删除。receipt写明版本/commit/包hash/manifest hash与deleted状态。missing包且无有效receipt是未证明，不冒充成功；同identity有效receipt则 `already_cleaned`。

最终 checkpoint `status=complete`、`NEXT_EXECUTABLE_ACTION=null`，仅当所有gate及cleanup有观察到的结果。失败时checkpoint绑定首个未完成命令。
