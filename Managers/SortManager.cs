using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.International.Converters.PinYinConverter;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 文件排序管理器 - 处理文件名的智能排序
    /// </summary>
    public class SortManager
    {
        /// <summary>
        /// 获取文件的排序键
        /// 支持多种文件名格式：
        /// 1. "第001首 爱你" - 第X首格式
        /// 2. "001.圣哉三一1" - 前缀数字.中文后缀数字
        /// 3. "10想起你" - 开头数字+中文
        /// 4. "因为有你01" - 中文数字
        /// 排序优先级：前缀数字 > 中文拼音 > 后缀数字
        /// </summary>
        public (int prefixNumber, string pinyinPart, int suffixNumber) GetSortKey(string filename)
        {
            // 移除扩展名
            string name = Path.GetFileNameWithoutExtension(filename);

            // 初始化排序键组件
            int prefixNumber = 0;
            string textPart = name;
            int suffixNumber = 0;

            // 模式1: 匹配 "第001首 爱你" 或 "第0707" 格式（第X首格式，"首"字可选）
            var pattern1 = Regex.Match(name, @"^第(\d+)(?:首)?\s*(.*)$");
            if (pattern1.Success)
            {
                prefixNumber = int.Parse(pattern1.Groups[1].Value);
                string remainingText = pattern1.Groups[2].Value.Trim();

                if (string.IsNullOrEmpty(remainingText))
                {
                    textPart = "";
                    suffixNumber = 0;
                }
                else
                {
                    // 尝试从剩余文本中分离文本部分和后缀数字
                    var suffixMatch = Regex.Match(remainingText, @"(\d+)$");
                    if (suffixMatch.Success)
                    {
                        suffixNumber = int.Parse(suffixMatch.Groups[1].Value);
                        textPart = remainingText.Substring(0, suffixMatch.Index).Trim();
                    }
                    else
                    {
                        textPart = remainingText;
                        suffixNumber = 0;
                    }
                }
            }
            else
            {
                // 模式2: 匹配 "001.圣哉三一1" 格式（前缀数字.中文后缀数字）
                var pattern2 = Regex.Match(name, @"^(\d+)\.(.+?)(\d+)$");
                if (pattern2.Success)
                {
                    prefixNumber = int.Parse(pattern2.Groups[1].Value);
                    textPart = pattern2.Groups[2].Value;
                    suffixNumber = int.Parse(pattern2.Groups[3].Value);
                }
                else
                {
                    // 模式3: 匹配 "10想起你" 格式（开头数字+中文）
                    var pattern3 = Regex.Match(name, @"^(\d+)(.+)$");
                    if (pattern3.Success)
                    {
                        prefixNumber = int.Parse(pattern3.Groups[1].Value);
                        textPart = pattern3.Groups[2].Value;
                        suffixNumber = 0;
                    }
                    else
                    {
                        // 模式4: 匹配 "因为有你_01" 或 "因为有你01" 格式（中文_数字或中文数字）
                        var pattern4 = Regex.Match(name, @"^(.+?)_?(\d+)$");
                        if (pattern4.Success)
                        {
                            textPart = pattern4.Groups[1].Value.Replace("_", "");
                            suffixNumber = int.Parse(pattern4.Groups[2].Value);
                            prefixNumber = 0;
                        }
                        else
                        {
                            // 模式5: 纯文本，没有数字
                            textPart = name;
                            prefixNumber = 0;
                            suffixNumber = 0;
                        }
                    }
                }
            }

            // 获取中文的拼音首字母
            string pinyinPart = GetPinyin(textPart);

            // 返回排序键：(前缀数字, 中文拼音, 后缀数字)
            return (prefixNumber, pinyinPart.ToLower(), suffixNumber);
        }

        /// <summary>
        /// 获取中文字符串的拼音首字母
        /// </summary>
        private string GetPinyin(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var pinyin = new System.Text.StringBuilder();

            foreach (char c in text)
            {
                if (ChineseChar.IsValidChar(c))
                {
                    // 中文字符，获取拼音首字母
                    try
                    {
                        var chineseChar = new ChineseChar(c);
                        var pinyins = chineseChar.Pinyins;
                        if (pinyins != null && pinyins.Count > 0)
                        {
                            // 获取第一个拼音的首字母
                            var firstPinyin = pinyins[0];
                            if (!string.IsNullOrEmpty(firstPinyin))
                            {
                                pinyin.Append(firstPinyin[0]);
                            }
                        }
                    }
                    catch
                    {
                        // 如果转换失败，保留原字符
                        pinyin.Append(c);
                    }
                }
                else
                {
                    // 非中文字符，直接添加
                    pinyin.Append(c);
                }
            }

            return pinyin.ToString();
        }
    }
}

