using System;
using System.Collections.Generic;
using System.Diagnostics;
using SkiaSharp;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 圣经经文 SkiaSharp 渲染器
    /// </summary>
    public class BibleTextRenderer
    {
        private BibleTextInsertConfig _config;
        
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="config">圣经插入配置</param>
        public BibleTextRenderer(BibleTextInsertConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            
            #if DEBUG
            Debug.WriteLine($"✅ [BibleTextRenderer] 初始化完成");
            Debug.WriteLine($"   样式布局: {_config.Style}");
            Debug.WriteLine($"   统一字体: {_config.FontFamily}");
            Debug.WriteLine($"   标题: {_config.TitleStyle.FontSize}pt, 粗体={_config.TitleStyle.IsBold}");
            Debug.WriteLine($"   经文: {_config.VerseStyle.FontSize}pt, 粗体={_config.VerseStyle.IsBold}");
            #endif
        }
        
        /// <summary>
        /// 渲染经文到 SKBitmap
        /// </summary>
        /// <param name="reference">经文引用（如：[创世记1章1节]）</param>
        /// <param name="verseContent">经文内容</param>
        /// <param name="width">画布宽度</param>
        /// <param name="height">画布高度</param>
        /// <returns>渲染后的位图</returns>
        public SKBitmap RenderToBitmap(string reference, string verseContent, int width, int height)
        {
            var bitmap = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                
                float currentY = 20f;
                
                switch (_config.Style)
                {
                    case BibleTextInsertStyle.TitleOnTop:
                        currentY = DrawTitle(canvas, reference, 20f, currentY, width);
                        currentY += 20f; // 间距
                        DrawVerse(canvas, verseContent, 20f, currentY, width);
                        break;
                        
                    case BibleTextInsertStyle.TitleAtBottom:
                        currentY = DrawVerse(canvas, verseContent, 20f, currentY, width);
                        currentY += 20f;
                        DrawTitle(canvas, reference, 20f, currentY, width);
                        break;
                        
                    case BibleTextInsertStyle.InlineAtEnd:
                        DrawVerseWithInlineTitle(canvas, verseContent, reference, 
                                                  20f, currentY, width);
                        break;
                }
            }
            
            return bitmap;
        }
        
        /// <summary>
        /// 渲染标题
        /// </summary>
        /// <param name="canvas">画布</param>
        /// <param name="text">标题文本</param>
        /// <param name="x">X坐标</param>
        /// <param name="y">Y坐标</param>
        /// <param name="maxWidth">最大宽度</param>
        /// <returns>渲染后的Y坐标</returns>
        private float DrawTitle(SKCanvas canvas, string text, float x, float y, float maxWidth)
        {
            using (var paint = new SKPaint())
            {
                paint.Color = _config.TitleStyle.GetSKColor();
                paint.TextSize = _config.TitleStyle.FontSize;
                paint.IsAntialias = true;
                
                // ✅ 使用SkiaFontService加载字体（支持自定义字体文件）
                paint.Typeface = Core.SkiaFontService.Instance.GetTypeface(
                    _config.FontFamily,
                    _config.TitleStyle.IsBold,
                    false
                );
                
                // 🔧 如果需要加粗，启用伪加粗（对于不支持加粗的自定义字体）
                if (_config.TitleStyle.IsBold)
                {
                    paint.FakeBoldText = true;
                }
                
                canvas.DrawText(text, x, y + paint.TextSize, paint);
                
                return y + paint.TextSize;
            }
        }
        
        /// <summary>
        /// 渲染经文（支持多行）
        /// </summary>
        /// <param name="canvas">画布</param>
        /// <param name="text">经文文本</param>
        /// <param name="x">X坐标</param>
        /// <param name="y">Y坐标</param>
        /// <param name="maxWidth">最大宽度</param>
        /// <returns>渲染后的Y坐标</returns>
        private float DrawVerse(SKCanvas canvas, string text, float x, float y, float maxWidth)
        {
            using (var paint = new SKPaint())
            {
                paint.Color = _config.VerseStyle.GetSKColor();
                paint.TextSize = _config.VerseStyle.FontSize;
                paint.IsAntialias = true;
                
                // ✅ 使用SkiaFontService加载字体（支持自定义字体文件）
                paint.Typeface = Core.SkiaFontService.Instance.GetTypeface(
                    _config.FontFamily,
                    _config.VerseStyle.IsBold,
                    false
                );
                
                // 🔧 如果需要加粗，启用伪加粗（对于不支持加粗的自定义字体）
                if (_config.VerseStyle.IsBold)
                {
                    paint.FakeBoldText = true;
                }
                
                // 分行处理（每行是一节）
                string[] lines = text.Split('\n');
                float lineHeight = paint.TextSize + _config.VerseStyle.VerseSpacing; // 字体大小 + 节距
                float currentY = y;
                
                foreach (string line in lines)
                {
                    // 自动换行处理
                    var wrappedLines = WrapText(line, maxWidth - 40f, paint);
                    foreach (var wrappedLine in wrappedLines)
                    {
                        canvas.DrawText(wrappedLine, x, currentY + paint.TextSize, paint);
                        currentY += lineHeight;
                    }
                }
                
                return currentY;
            }
        }
        
        /// <summary>
        /// 渲染经文并在末尾添加标题
        /// </summary>
        /// <param name="canvas">画布</param>
        /// <param name="verseText">经文文本</param>
        /// <param name="reference">经文引用</param>
        /// <param name="x">X坐标</param>
        /// <param name="y">Y坐标</param>
        /// <param name="maxWidth">最大宽度</param>
        private void DrawVerseWithInlineTitle(SKCanvas canvas, string verseText, 
                                               string reference, float x, float y, float maxWidth)
        {
            using (var versePaint = new SKPaint())
            using (var titlePaint = new SKPaint())
            {
                // 设置经文画笔
                versePaint.Color = _config.VerseStyle.GetSKColor();
                versePaint.TextSize = _config.VerseStyle.FontSize;
                versePaint.IsAntialias = true;
                
                // ✅ 使用SkiaFontService加载字体（支持自定义字体文件）
                versePaint.Typeface = Core.SkiaFontService.Instance.GetTypeface(
                    _config.FontFamily,
                    _config.VerseStyle.IsBold,
                    false
                );
                
                // 🔧 如果需要加粗，启用伪加粗（对于不支持加粗的自定义字体）
                if (_config.VerseStyle.IsBold)
                {
                    versePaint.FakeBoldText = true;
                }
                
                // 设置标题画笔
                titlePaint.Color = _config.TitleStyle.GetSKColor();
                titlePaint.TextSize = _config.TitleStyle.FontSize;
                titlePaint.IsAntialias = true;
                
                // ✅ 使用SkiaFontService加载字体（支持自定义字体文件）
                titlePaint.Typeface = Core.SkiaFontService.Instance.GetTypeface(
                    _config.FontFamily,
                    _config.TitleStyle.IsBold,
                    false
                );
                
                // 🔧 如果需要加粗，启用伪加粗（对于不支持加粗的自定义字体）
                if (_config.TitleStyle.IsBold)
                {
                    titlePaint.FakeBoldText = true;
                }
                
                // 计算最后一行的位置（每行是一节）
                string[] lines = verseText.Split('\n');
                float lineHeight = versePaint.TextSize + _config.VerseStyle.VerseSpacing; // 字体大小 + 节距
                float currentY = y;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i < lines.Length - 1)
                    {
                        // 非最后一行，正常绘制
                        canvas.DrawText(lines[i], x, currentY + versePaint.TextSize, versePaint);
                        currentY += lineHeight;
                    }
                    else
                    {
                        // 最后一行，经文 + 标题
                        float verseWidth = versePaint.MeasureText(lines[i]);
                        canvas.DrawText(lines[i], x, currentY + versePaint.TextSize, versePaint);
                        
                        // 标题紧跟在经文后面
                        canvas.DrawText(reference, x + verseWidth + 5f, 
                                       currentY + titlePaint.TextSize, titlePaint);
                    }
                }
            }
        }
        
        /// <summary>
        /// 文本自动换行
        /// </summary>
        /// <param name="text">原始文本</param>
        /// <param name="maxWidth">最大宽度</param>
        /// <param name="paint">画笔</param>
        /// <returns>换行后的文本列表</returns>
        private List<string> WrapText(string text, float maxWidth, SKPaint paint)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
                return lines;
            
            var words = text.ToCharArray();
            var currentLine = "";
            
            foreach (char ch in words)
            {
                string testLine = currentLine + ch;
                float testWidth = paint.MeasureText(testLine);
                
                if (testWidth > maxWidth && !string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                    currentLine = ch.ToString();
                }
                else
                {
                    currentLine = testLine;
                }
            }
            
            if (!string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
            }
            
            return lines;
        }
        
        /// <summary>
        /// 生成纯文本（用于插入到TextBox）
        /// </summary>
        /// <param name="reference">经文引用</param>
        /// <param name="verseContent">经文内容</param>
        /// <returns>格式化后的纯文本</returns>
        public string GeneratePlainText(string reference, string verseContent)
        {
            switch (_config.Style)
            {
                case BibleTextInsertStyle.TitleOnTop:
                    return $"{reference}\n\n{verseContent}";
                    
                case BibleTextInsertStyle.TitleAtBottom:
                    return $"{verseContent}\n\n{reference}";
                    
                case BibleTextInsertStyle.InlineAtEnd:
                    return $"{verseContent}{reference}";
                    
                default:
                    return verseContent;
            }
        }
    }
}

