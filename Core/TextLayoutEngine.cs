using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 文本布局引擎（自动换行、行距计算）
    /// </summary>
    public class TextLayoutEngine
    {
        private readonly SkiaFontService _fontService;

        public TextLayoutEngine(SkiaFontService fontService)
        {
            _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        }

        /// <summary>
        /// 计算文本布局（返回每行的位置和大小）
        /// </summary>
        public TextLayout CalculateLayout(string text, TextStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new TextLayout
                {
                    Lines = new List<TextLine>(),
                    TotalSize = new SKSize(0, 0)
                };
            }
            
            // 创建Font用于测量
            using var font = CreateFont(style);
            
            // 自动换行
            var wrappedLines = WrapText(text, font, maxWidth);

            // 计算每行的位置
            var layout = new TextLayout();
            // ✅ 修复行间距计算：与 WPF BlockLineHeight 行为完全一致
            //
            // WPF BlockLineHeight 行为：
            //   LineHeight = FontSize × LineSpacing (例如 86 × 2.1 = 180.6)
            //   行框高度固定为 LineHeight
            //   文字在行框内垂直居中
            //   第一行顶部 = 0
            //   第一行 baseline ≈ (LineHeight - FontSize) / 2 + FontSize
            //                    = (180.6 - 86) / 2 + 86 = 47.3 + 86 = 133.3
            //
            // SkiaSharp 对应实现：
            //   使用 SkiaSharp 的 FontMetrics 获取精确的 baseline 偏移
            float lineHeight = style.FontSize * style.LineSpacing;

            // 计算第一行的 baseline 位置（模拟 WPF BlockLineHeight 的垂直居中）
            // 上边距 = (LineHeight - FontSize) / 2
            float topPadding = (lineHeight - style.FontSize) / 2;
            float currentY = topPadding + style.FontSize;  // baseline = 上边距 + FontSize

            float maxLineWidth = 0;

            foreach (var lineText in wrappedLines)
            {
                float lineWidth = font.MeasureText(lineText);
                maxLineWidth = Math.Max(maxLineWidth, lineWidth);

                layout.Lines.Add(new TextLine
                {
                    Text = lineText,
                    Position = new SKPoint(0, currentY),
                    Size = new SKSize(lineWidth, style.FontSize)
                });

#if DEBUG
                if (layout.Lines.Count == 1) // 只输出第一行的调试信息
                {
                    System.Diagnostics.Debug.WriteLine($"📐 [行间距计算] 字体大小={style.FontSize}, 行间距倍数={style.LineSpacing}, 计算行高={lineHeight}, 第一行baseline={currentY}");
                }
#endif

                currentY += lineHeight;
            }
            
            layout.TotalSize = new SKSize(
                maxLineWidth,
                wrappedLines.Count > 0 ? currentY - style.FontSize * (style.LineSpacing - 1) : 0
            );
            
            return layout;
        }
        
        /// <summary>
        /// 测量文本实际占用的尺寸
        /// </summary>
        public SKSize MeasureText(string text, TextStyle style, float maxWidth)
        {
            var layout = CalculateLayout(text, style, maxWidth);
            return layout.TotalSize;
        }
        
        /// <summary>
        /// 自动换行（考虑中英文、标点符号）
        /// </summary>
        public List<string> WrapText(string text, SKFont font, float maxWidth)
        {
            var lines = new List<string>();
            
            if (string.IsNullOrEmpty(text))
                return lines;
            
            // 按换行符分割段落
            var paragraphs = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            
            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrEmpty(paragraph))
                {
                    lines.Add(string.Empty); // 保留空行
                    continue;
                }
                
                // 对每个段落进行换行处理
                var paragraphLines = WrapParagraph(paragraph, font, maxWidth);
                lines.AddRange(paragraphLines);
            }
            
            return lines;
        }
        
        /// <summary>
        /// 对单个段落进行换行处理
        /// </summary>
        private List<string> WrapParagraph(string paragraph, SKFont font, float maxWidth)
        {
            var lines = new List<string>();
            var currentLine = string.Empty;
            var words = new List<string>();
            var currentWord = string.Empty;
            
            // 将段落分解为"单词"（中文字符单独成词，英文单词保持完整）
            for (int i = 0; i < paragraph.Length; i++)
            {
                char c = paragraph[i];
                
                // 判断是否是中文字符或标点
                if (IsCJKCharacter(c) || IsCJKPunctuation(c))
                {
                    // 中文字符：先保存当前英文单词，然后中文字符单独成词
                    if (!string.IsNullOrEmpty(currentWord))
                    {
                        words.Add(currentWord);
                        currentWord = string.Empty;
                    }
                    words.Add(c.ToString());
                }
                else if (char.IsWhiteSpace(c))
                {
                    // 空格：保存当前单词
                    if (!string.IsNullOrEmpty(currentWord))
                    {
                        words.Add(currentWord);
                        currentWord = string.Empty;
                    }
                    words.Add(c.ToString()); // 保留空格
                }
                else
                {
                    // 英文字符：累积到当前单词
                    currentWord += c;
                }
            }
            
            // 保存最后一个单词
            if (!string.IsNullOrEmpty(currentWord))
            {
                words.Add(currentWord);
            }
            
            // 按单词组装行
            foreach (var word in words)
            {
                string testLine = currentLine + word;
                float testWidth = font.MeasureText(testLine);
                
                if (testWidth <= maxWidth)
                {
                    // 当前行可以容纳
                    currentLine = testLine;
                }
                else
                {
                    // 当前行放不下
                    if (!string.IsNullOrEmpty(currentLine))
                    {
                        // 保存当前行
                        lines.Add(currentLine.TrimEnd());
                        currentLine = string.Empty;
                    }
                    
                    // 检查单个单词是否超过最大宽度
                    if (font.MeasureText(word) > maxWidth)
                    {
                        // 单词太长，强制拆分
                        var splitWords = ForceSplitWord(word, font, maxWidth);
                        foreach (var splitWord in splitWords)
                        {
                            if (string.IsNullOrEmpty(currentLine))
                            {
                                currentLine = splitWord;
                            }
                            else
                            {
                                lines.Add(currentLine);
                                currentLine = splitWord;
                            }
                        }
                    }
                    else
                    {
                        // 从新行开始
                        currentLine = word;
                    }
                }
            }
            
            // 保存最后一行
            if (!string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine.TrimEnd());
            }
            
            // 如果没有产生任何行，返回空行
            if (lines.Count == 0)
            {
                lines.Add(string.Empty);
            }
            
            return lines;
        }
        
        /// <summary>
        /// 强制拆分过长的单词
        /// </summary>
        private List<string> ForceSplitWord(string word, SKFont font, float maxWidth)
        {
            var parts = new List<string>();
            var currentPart = string.Empty;
            
            foreach (char c in word)
            {
                string testPart = currentPart + c;
                if (font.MeasureText(testPart) <= maxWidth)
                {
                    currentPart = testPart;
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentPart))
                    {
                        parts.Add(currentPart);
                    }
                    currentPart = c.ToString();
                }
            }
            
            if (!string.IsNullOrEmpty(currentPart))
            {
                parts.Add(currentPart);
            }
            
            return parts;
        }
        
        /// <summary>
        /// 判断是否是中日韩（CJK）字符
        /// </summary>
        private bool IsCJKCharacter(char c)
        {
            int code = c;
            return (code >= 0x4E00 && code <= 0x9FFF) ||   // CJK统一汉字
                   (code >= 0x3400 && code <= 0x4DBF) ||   // CJK扩展A
                   (code >= 0x20000 && code <= 0x2A6DF) || // CJK扩展B
                   (code >= 0xF900 && code <= 0xFAFF) ||   // CJK兼容汉字
                   (code >= 0x3040 && code <= 0x309F) ||   // 日文平假名
                   (code >= 0x30A0 && code <= 0x30FF) ||   // 日文片假名
                   (code >= 0xAC00 && code <= 0xD7AF);     // 韩文音节
        }
        
        /// <summary>
        /// 判断是否是中文标点符号
        /// </summary>
        private bool IsCJKPunctuation(char c)
        {
            int code = c;
            return (code >= 0x3000 && code <= 0x303F) ||   // CJK符号和标点
                   (code >= 0xFF00 && code <= 0xFFEF);     // 全角ASCII、半角标点
        }
        
        /// <summary>
        /// 创建Font对象（用于测量）
        /// </summary>
        private SKFont CreateFont(TextStyle style)
        {
            // ✅ 使用SkiaFontService加载字体（支持自定义字体文件）
            var typeface = _fontService.GetTypeface(style.FontFamily, style.IsBold, style.IsItalic);
            
            var font = new SKFont
            {
                Typeface = typeface,
                Size = style.FontSize,
                Subpixel = true
            };
            
            // 🔧 如果需要加粗，启用伪加粗（对于不支持加粗的自定义字体）
            if (style.IsBold)
            {
                font.Embolden = true;
            }
            
            return font;
        }
        
        /// <summary>
        /// 获取字体样式
        /// </summary>
        private SKFontStyle GetFontStyle(bool isBold, bool isItalic)
        {
            if (isBold && isItalic)
                return SKFontStyle.BoldItalic;
            else if (isBold)
                return SKFontStyle.Bold;
            else if (isItalic)
                return SKFontStyle.Italic;
            else
                return SKFontStyle.Normal;
        }
    }
}

