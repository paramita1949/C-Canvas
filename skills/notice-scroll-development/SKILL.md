---
name: notice-scroll-development
description: Use when implementing or fixing notice/text marquee motion, boundary wrapping, or direction symmetry in this Canvas project, especially when behavior differs between LeftToRight and RightToLeft or after canvas ratio changes.
---

# Notice Scroll Development

## Overview
Use this skill to build and debug notice scrolling with stable boundary logic. The core rule is to keep all motion math in one coordinate system based on the actual render canvas.

## When to Use
- Notice component changes in `NoticeRuntimeService` or `MainWindow.TextEditor.Helpers`
- Any report that one direction looks right and the other looks wrong
- Any behavior change between 16:9 and 4:3 modes
- Any request about "wrap", "re-enter", "leave viewport", or "all text disappears then restart"

Do not use for unrelated UI styling or static text rendering.

## Required Rules
1. Define behavior first: `touch boundary wrap` vs `fully leave viewport then wrap`.
2. Use `EditorCanvas.ActualWidth` as primary viewport width source.
3. Keep `viewportWidth`, `contentWidth`, `laneStartX/laneEndX`, and start position in the same coordinate system.
4. Validate symmetry: test both `LeftToRight` and `RightToLeft` under the same width.
5. Apply TDD: update/add failing tests first, then implement.

## Workflow
1. Reproduce with explicit widths (`1920`, `1600`, `960`) and both directions.
2. Verify viewport source in `TryGetNoticeRenderOffset` before touching formulas.
3. Confirm expected wrap threshold from requirement language.
4. Add/adjust tests in `Canvas.TextEditor.Tests/Components/NoticeRuntimeServiceTests.cs`.
5. Implement minimal math change in `Services/TextEditor/Components/Notice/NoticeRuntimeService.cs`.
6. Run targeted tests, then full `NoticeRuntimeServiceTests`.

## Verification Checklist
- [ ] `LeftToRight`: wraps at the required threshold (touch edge or fully leave).
- [ ] `RightToLeft`: wraps at the required threshold.
- [ ] 16:9 and 4:3 both match expected behavior.
- [ ] Short and long text both behave correctly.
- [ ] `PingPong` behavior did not regress.

## Project References
- Primary summary: `NOTICE_COMPONENT_AGENT_SUMMARY.md`
- Runtime logic: `Services/TextEditor/Components/Notice/NoticeRuntimeService.cs`
- UI integration: `UI/MainWindow.TextEditor.Helpers.cs`
- Tests: `Canvas.TextEditor.Tests/Components/NoticeRuntimeServiceTests.cs`
