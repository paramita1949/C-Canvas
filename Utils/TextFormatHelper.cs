using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 文本格式化辅助类
    /// 处理圣经文本中的特殊标记(如下划线等)
    /// </summary>
    public static class TextFormatHelper
    {
        /// <summary>
        /// 将包含HTML标记的文本转换为TextBlock的Inlines
        /// 支持的标记: <u>下划线</u>
        /// 如果无法正确显示下划线,会自动移除标记显示纯文本
        /// </summary>
        /// <param name="textBlock">目标TextBlock</param>
        /// <param name="text">原始文本</param>
        public static void SetFormattedText(TextBlock textBlock, string text)
        {
            if (textBlock == null)
                return;

            if (string.IsNullOrEmpty(text))
            {
                textBlock.Text = string.Empty;
                return;
            }

            // 查找所有<u>标记
            var pattern = @"<u>(.*?)</u>";
            var matches = Regex.Matches(text, pattern);

            if (matches.Count == 0)
            {
                // 没有特殊标记,直接设置文本
                textBlock.Text = text;
                return;
            }

            // 有特殊标记,移除所有<u></u>标记显示纯文本
            var cleanText = StripHtmlTags(text);
            textBlock.Text = cleanText;
        }

        /// <summary>
        /// 移除文本中的所有HTML标记,返回纯文本
        /// </summary>
        public static string StripHtmlTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // 移除所有<u>标记
            return Regex.Replace(text, @"<u>(.*?)</u>", "$1");
        }
    }
}

