## Global Coding Skills Policy (Mandatory)
For any programming/development request in any workspace, the agent MUST load and follow these core Superpowers skills on every turn before analysis, planning, coding, or clarifying questions:
- skill: `using-superpowers`
- file: `C:/Users/Administrator/.codex/superpowers/skills/using-superpowers/SKILL.md`
- skill: `systematic-debugging`
- file: `C:/Users/Administrator/.codex/superpowers/skills/systematic-debugging/SKILL.md`
- skill: `verification-before-completion`
- file: `C:/Users/Administrator/.codex/superpowers/skills/verification-before-completion/SKILL.md`

When a Superpowers plugin skill is available in the active Codex environment, prefer invoking/loading that plugin skill capability first; use the file path above as the explicit fallback/reference for environments that do not expose direct plugin skill invocation.

## Global Development Workflow Skills (Mandatory When Matched)
When a request matches the condition, the agent MUST additionally load and follow the skill:

### Planning / Implementation
- `brainstorming` before creative feature work, behavior changes, or new components.
- `writing-plans` for multi-step requirements before editing code.
- `planning-with-files-zh` when the task needs file-backed planning, persistent task state, or iterative execution against an on-disk plan. Use it alongside `writing-plans`, not as a replacement for the Superpowers planning workflow.
- `test-driven-development` before implementing a feature or bugfix.
- `executing-plans` when executing an existing written plan.

### Quality / Collaboration
- `requesting-code-review` before merge or after major implementation.
- `receiving-code-review` before applying review feedback.
- `dispatching-parallel-agents` when 2+ independent subtasks can run in parallel.
- `subagent-driven-development` when executing an implementation plan with independent tasks.
- `finishing-a-development-branch` after tests pass and branch is ready for integration.
- `self-improvement` (aka `self-im`) after failures, user corrections, or tool/API issues.
- `find-skills` when the user asks how to do something via skills, asks to find/install skills, or asks whether a skill exists for a capability.

### Tech/Domain-Specific Programming Skills
- `aspnet-core` for ASP.NET Core web app build/refactor/review/troubleshooting.
- `winui-app` for WinUI 3/Windows App SDK app work.
- `playwright` / `playwright-interactive` / `agent-browser` for browser automation, UI flow testing, and interactive debugging.
- `chatgpt-apps` + `openai-docs` for OpenAI API/Apps SDK implementation.
- `figma` / `figma-implement-design` for design-to-code tasks.
- `security-best-practices` / `security-threat-model` / `security-ownership-map` only when user explicitly requests security-focused work.
- `cli-anything` when building or validating CLI-Anything harnesses.
- `develop-web-game` for iterative web game development/testing.
- `jupyter-notebook` for notebook-based coding tasks.

### Release / Delivery (When Explicitly Requested)
- `vercel-deploy` / `netlify-deploy` / `cloudflare-deploy` / `render-deploy` for deployment tasks.
- `yeet` only when user explicitly asks for stage+commit+push+PR in one flow.

## Skill Selection Notes
- Source scanned: `C:/Users/Administrator/.codex` (including `skills` and `superpowers/skills`, with duplicates from `vendor_imports` ignored).
- The core skill set above is always-on for programming turns; scenario skills are mandatory once matched.

--- project-doc ---

## Repository Skill Entry (Mandatory)
For any user request in this repository, the agent MUST load and follow BOTH global and repository skills before analysis, planning, coding, or clarifying questions.

## CodeGraph Usage (Mandatory When Available)
For coding, debugging, refactoring, review, or impact-analysis requests in this repository, the agent MUST use CodeGraph before broad file scanning when the CodeGraph MCP tools are available.

Required pattern:
1. When CodeGraph MCP tools are exposed, run `codegraph_context` / `codegraph_search` / `codegraph_callers` / `codegraph_callees` / `codegraph_impact` first to identify relevant files, symbols, entry points, and impacted surfaces.
2. Then read only the directly relevant source files to verify details before editing.
3. If CodeGraph MCP tools are not exposed in the current session, use the CLI fallback from `d:/img/Canvas`. The CLI does not currently provide a `context` subcommand, so do not run `codegraph context`:
   - `codegraph status`
   - `codegraph query "<symbol-or-feature-or-task-keywords>"`
   - `codegraph callers "<symbol>"`
   - `codegraph callees "<symbol>"`
   - `codegraph impact "<symbol>"`
4. If the index is stale, run `codegraph sync` before relying on CodeGraph results.
5. Do not replace source reads with CodeGraph output for final implementation decisions; use CodeGraph to narrow the search and source files as final truth.

## Always-Load Skills Per Turn (Lean)
### Keep always-on (high value / low waste)
- skill: `project-context`
- file: `d:/img/Canvas/skills/project-context/SKILL.md`
- skill: `verification-before-completion`
- file: `C:/Users/Administrator/.codex/superpowers/skills/verification-before-completion/SKILL.md`

## Scenario-Required Skills (Mandatory When Matched)
### Repository-local
- When the request involves notice/marquee scrolling behavior, boundary wrapping, direction symmetry, or 16:9 vs 4:3 movement differences:
  - skill: `notice-scroll-development`
  - file: `d:/img/Canvas/skills/notice-scroll-development/SKILL.md`
- When the request is a bug / failure / unexpected runtime behavior:
  - skill: `systematic-debugging`
  - file: `C:/Users/Administrator/.codex/superpowers/skills/systematic-debugging/SKILL.md`
- When the request is about finding/installing skills:
  - skill: `find-skills`
  - file: `C:/Users/Administrator/.codex/skills/find-skills/SKILL.md`
- After user correction, repeated failure, or tool/API issue:
  - skill: `self-improvement`
  - file: `C:/Users/Administrator/.codex/skills/self-improvement/SKILL.md`

### UI / Layout Change Gate
For any request that adds or changes buttons, toolbars, panels, dialogs, spacing, typography, colors, or control layout:
1. Inspect the existing UI region first, including neighboring controls, styles, resource keys, spacing, density, icon/text conventions, and primary vs secondary action hierarchy.
2. State the existing UI pattern before editing code.
3. Place new controls inside the existing pattern unless the user explicitly asks for a redesign.
4. Prefer reusing existing styles/resources over inline one-off styling.
5. Verify visually after implementation with a screenshot or equivalent UI inspection. The new element must look like it belongs to the same interface, not like a functional control pasted onto the surface.
6. If the requested control does not fit the current layout, propose a layout adjustment before coding instead of forcing it into the nearest container.

### Global workflow
- Product-first chain (run before implementation for NEW feature/flow/UI architecture work only):
  - `product-manager-toolkit`
  - `prd-development`
  - `user-story`
  - `roadmap-planning`
  - execution order: `product-manager-toolkit` -> `user-story` -> `prd-development` -> `roadmap-planning`
  - requirement: do not start coding until user outcome, interaction flow, and naming semantics are明确
- Creative feature/behavior work: `brainstorming` (only when scope is open-ended)
- Multi-step requirement: `writing-plans` (only when work is truly multi-step)
- Feature/bugfix implementation: `test-driven-development` (for non-trivial logic changes)
- Executing an existing plan: `executing-plans`
- Ready for merge/review: `requesting-code-review`, `finishing-a-development-branch`
- Applying review comments: `receiving-code-review`
- 2+ independent subtasks: `dispatching-parallel-agents` / `subagent-driven-development`

## Purpose
Use this repository entry to ensure every turn in `d:/img/Canvas` can invoke both global skill capabilities and repository-specific skill context.
## System Change Gate (Mandatory)
For any non-trivial feature/bugfix/UI behavior change (not pure text copy edits), the agent MUST complete this full-chain workflow before claiming completion.

### Lightweight Change Exception (Mandatory)
For small, explicitly scoped changes such as GitHub Actions/YML filename edits, copy-only text edits, documentation wording, version metadata, or simple config value changes:
1. Do **not** run unrelated app/test suites unless the user explicitly asks for build verification.
2. Do **not** update `docs/engineering-change-gate.md`, `task_plan.md`, `progress.md`, or `findings.md` unless the user asks for persistent task records or the change is a non-trivial feature/bugfix/UI behavior change.
3. Verify only the directly changed surface with targeted checks, for example:
   - `rg` for old/new text in the edited files.
   - `git diff --check` when whitespace or YAML formatting could matter.
   - A YAML parser/linter only if already available and directly relevant.
4. In the final response, state that broad tests were intentionally skipped because the change is configuration/text-only.
5. If the user says “just modify YML/config/text”, treat that as an explicit request for this lightweight path.

### 1) Impact Surface First
Before code edits, enumerate the impacted surface in `docs/engineering-change-gate.md`:
- modules/files touched
- state owners and data flow boundaries
- UI entry points and event triggers
- persistence / fallback / default-value sources

### 2) Invariants Before Implementation
Define explicit invariants before edits:
- internal canonical value(s) and single source of truth
- display mapping rule(s) and rendering boundaries
- forbidden couplings (e.g., never infer internal state from UI text)

### 3) Layered Change Rule
Implement by layer, not by patch-point:
- state/domain layer
- mapping/presentation layer
- interaction/UI layer
Do not ship if a behavior is fixed in only one layer while related layers are unreviewed.

### 4) Scenario Regression Gate
Before completion, run scenario regression (not build-only):
- empty/default state
- selected/active state
- search/filter state (if present)
- delete/fallback/recovery state
- one negative path (invalid input or missing data)

### 5) Verification Artifact Required
For non-trivial feature/bugfix/UI behavior changes, the turn must include:
- updated `docs/engineering-change-gate.md` checklist section for the task
- verification command output summary from task-relevant checks, such as targeted unit tests, `dotnet build`, UI inspection, or scenario-specific scripts

If any checklist item is not validated, explicitly mark it as pending and do not claim full completion.

## CF-ADMIN Sync Policy (Mandatory)
For any request involving `cf-admin` content/features/config:

1. Treat `D:\img\cloudflare\cf-admin` as the source of truth for edits.
2. Apply all `cf-admin` code/content changes in `D:\img\cloudflare\cf-admin` first.
3. After source edits, mirror-sync to `D:\img\Canvas\cf-admin` in the same turn.
4. Exclude source `.git` metadata when syncing; only project files are mirrored.
5. Verify sync success with file-level comparison (for example hash check or mirror tool report) before claiming completion.

Completion rule:
- Do not mark a `cf-admin` task complete unless both directories are confirmed consistent:
  - `D:\img\cloudflare\cf-admin`
  - `D:\img\Canvas\cf-admin`
