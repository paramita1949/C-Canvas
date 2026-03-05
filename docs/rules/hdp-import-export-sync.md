# .hdp 导入导出字段同步规则（子代理）

适用范围：`Managers/SlideExportManager.cs`、`Managers/SlideImportManager.cs` 及 `.hdp` DTO。

## 触发条件
- 只要以下任一模型新增/修改字段，即触发本规则：
- `Database/Models/TextProject.cs`
- `Database/Models/Slide.cs`
- `Database/Models/TextElement.cs`
- `Database/Models/RichTextSpan.cs`

## 强制要求
1. `SlideExportManager` 导出映射必须补齐新字段。
2. `.hdp` DTO（`TextProjectData/SlideData/TextElementData/RichTextSpanData`）必须补齐新字段。
3. `SlideImportManager` 导入映射必须补齐新字段。
4. 旧 `.hdp` 兼容必须明确：
- 可空字段允许缺失。
- 非可空字段必须给兼容默认值。
5. 必须新增或更新自动化测试，至少校验：
- DTO 中存在该字段。
- 导入时缺失字段不会异常且回退到默认值。

## 禁止项
- 只改模型，不改 `.hdp` 导入导出。
- 只改导出，不改导入。
- 没有测试就声称“已支持新字段”。

## 子代理执行模板
将以下任务直接交给子代理执行：

```
任务：同步 .hdp 导入导出字段

目标：
1) 扫描 TextProject/Slide/TextElement/RichTextSpan 模型最近新增字段。
2) 对比 SlideExportManager 的 DTO 和导出映射。
3) 对比 SlideImportManager 的导入映射。
4) 补齐缺失字段并保证旧 .hdp 兼容默认值。
5) 新增或更新测试并运行定向测试。

验收标准：
- 导入导出字段一致。
- 旧文件兼容通过。
- 测试通过并附结果摘要。
```

