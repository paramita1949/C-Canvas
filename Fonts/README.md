# 自定义字体库说明

## 📁 文件夹结构

```
Fonts/
├── Chinese/          ← 放置中文字体文件 (.ttf)
├── English/          ← 放置英文字体文件 (.ttf)
├── Number/           ← 放置数字字体文件 (.ttf)
├── fonts.json        ← 字体配置文件
└── README.md         ← 本说明文件
```

## 🎯 如何添加自定义字体

### 步骤 1：准备字体文件
- 下载 TTF 或 OTF 格式的字体文件
- 推荐使用 TTF 格式（更好的兼容性）

### 步骤 2：放置字体文件
- 中文字体 → 放入 `Chinese/` 文件夹
- 英文字体 → 放入 `English/` 文件夹
- 数字字体 → 放入 `Number/` 文件夹

### 步骤 3：编辑 fonts.json
在对应分类的 `fonts` 数组中添加配置：

```json
{
  "name": "思源黑体",
  "file": "Chinese/SourceHanSansCN.ttf",
  "family": "Source Han Sans CN",
  "weight": "Regular",
  "preview": "思源黑体 ABCabc 123",
  "isFavorite": false
}
```

**字段说明**：
- `name`: 在下拉框中显示的名称
- `file`: 字体文件路径（相对于 Fonts 文件夹）
  - 使用 `"system"` 表示系统字体
  - 使用相对路径表示自定义字体，如 `"Chinese/MyFont.ttf"`
- `family`: 字体族名称（FontFamily）
- `weight`: 字重（Regular, Bold, Light 等）
- `preview`: 预览文本
- `isFavorite`: 是否为收藏字体（true/false）

### 步骤 4：重启程序
保存 `fonts.json` 后重启程序，新字体将自动加载。

## 📥 推荐字体资源

### 免费商用中文字体
1. **思源黑体** - https://github.com/adobe-fonts/source-han-sans
2. **思源宋体** - https://github.com/adobe-fonts/source-han-serif
3. **阿里巴巴普惠体** - https://www.alibabafonts.com/
4. **站酷高端黑** - https://www.zcool.com.cn/special/zcoolfonts/
5. **霞鹜文楷** - https://github.com/lxgw/LxgwWenKai

### 免费商用英文字体
1. **Roboto** - https://fonts.google.com/specimen/Roboto
2. **Open Sans** - https://fonts.google.com/specimen/Open+Sans
3. **Montserrat** - https://fonts.google.com/specimen/Montserrat
4. **Lato** - https://fonts.google.com/specimen/Lato
5. **Inter** - https://rsms.me/inter/

### 数字字体
1. **DIN Pro** - 搜索 "DIN Pro free"
2. **Bebas Neue** - https://fonts.google.com/specimen/Bebas+Neue
3. **Oswald** - https://fonts.google.com/specimen/Oswald

## ⚠️ 注意事项

1. **字体版权**：请确保使用的字体允许商业使用
2. **文件大小**：单个字体文件通常 2-10MB，注意总大小
3. **字体命名**：`family` 字段必须与字体文件中的 FontFamily 名称一致
4. **文件路径**：使用正斜杠 `/` 或反斜杠 `\` 都可以

## 🔧 故障排查

### 字体不显示？
1. 检查字体文件是否存在
2. 检查 `family` 名称是否正确
3. 检查 `fonts.json` 格式是否正确（使用 JSON 验证工具）
4. 查看程序日志中的错误信息

### 如何查看字体的 FontFamily 名称？
- Windows: 右键字体文件 → 属性 → 详细信息 → 标题
- 或使用字体查看工具

## 📝 示例配置

### 添加思源黑体
```json
{
  "name": "思源黑体",
  "file": "Chinese/SourceHanSansCN-Regular.ttf",
  "family": "Source Han Sans CN",
  "weight": "Regular",
  "preview": "思源黑体 ABCabc 123",
  "isFavorite": true
}
```

### 添加 Roboto
```json
{
  "name": "Roboto",
  "file": "English/Roboto-Regular.ttf",
  "family": "Roboto",
  "weight": "Regular",
  "preview": "Roboto ABCabc 123",
  "isFavorite": true
}
```

---

*最后更新：2025-10-15*

