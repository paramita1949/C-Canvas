using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Interfaces;
using TinyPinyin;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 圣经拼音快速定位服务
    /// </summary>
    public class BiblePinyinService
    {
        private readonly IBibleService _bibleService;

        public BiblePinyinService(IBibleService bibleService)
        {
            _bibleService = bibleService;
        }
        /// <summary>
        /// 拼音缩写 → 书卷ID 映射字典
        /// </summary>
        private static readonly Dictionary<string, int> PinyinMap = new()
        {
            // 旧约 (1-39)
            {"csj", 1}, {"ceji", 2}, {"lwj", 3}, {"msj", 4}, {"smj", 5},
            {"ysyj", 6}, {"ssj", 7}, {"ldj", 8}, {"smejs", 9}, {"smejx", 10},
            {"lwjs", 11}, {"lwjx", 12}, {"ldzs", 13}, {"ldzx", 14}, {"yslj", 15},
            {"nxmj", 16}, {"ystj", 17}, {"ybj", 18}, {"sp", 19}, {"zy", 20},
            {"cds", 21}, {"yg", 22}, {"ysys", 23}, {"ylms", 24}, {"ylmag", 25},
            {"yxjs", 26}, {"dyls", 27}, {"hxas", 28}, {"yes", 29}, {"ams", 30},
            {"ebdys", 31}, {"yns", 32}, {"mjs", 33}, {"nhs", 34}, {"hbgs", 35},
            {"xfys", 36}, {"hgs", 37}, {"sjlys", 38}, {"mljs", 39},
            
            // 新约 (40-66)
            {"mtfy", 40}, {"mkfy", 41}, {"ljfy", 42}, {"yhfy", 43}, {"stxz", 44},
            {"lms", 45}, {"gldqs", 46}, {"gldhs", 47}, {"jlts", 48}, {"yfss", 49},
            {"flbs", 50}, {"glxs", 51}, {"tslnjqs", 52}, {"tslnjhs", 53},
            {"tmtqs", 54}, {"tmths", 55}, {"tds", 56}, {"flms", 57}, {"xbls", 58},
            {"ygs", 59}, {"bdqs", 60}, {"bdhs", 61}, {"yhys", 62}, {"yhes", 63},
            {"yhss", 64}, {"yds", 65}, {"qsl", 66}
        };

        /// <summary>
        /// 常见中文简称（如“林前”）对应的首字母别名映射（如“lq”）。
        /// 支持重叠：同一个别名可对应多卷书（例如“tq”可能匹配“帖前/提前”）。
        /// 规则：按 shortName 逐字取拼音首字母（如“徒”-> t，“林前”-> lq）。
        /// </summary>
        private static readonly Dictionary<string, List<int>> ShortAliasMap = BuildShortAliasMap();

        /// <summary>
        /// 根据拼音前缀查找匹配的书卷
        /// </summary>
        public List<BibleBookMatch> FindBooksByPinyin(string pinyin)
        {
            if (string.IsNullOrWhiteSpace(pinyin))
                return new List<BibleBookMatch>();

            pinyin = pinyin.ToLower().Trim();
            var bestMatchesByBook = new Dictionary<int, BibleBookMatch>();

            foreach (var kvp in PinyinMap)
            {
                if (kvp.Key.StartsWith(pinyin, StringComparison.Ordinal))
                {
                    TryUpsertMatch(bestMatchesByBook, kvp.Value, kvp.Key, kvp.Key == pinyin ? 100 : 60);
                }
            }

            foreach (var kvp in ShortAliasMap)
            {
                if (!kvp.Key.StartsWith(pinyin, StringComparison.Ordinal))
                {
                    continue;
                }

                int score = kvp.Key == pinyin ? 95 : 55;
                foreach (var bookId in kvp.Value)
                {
                    TryUpsertMatch(bestMatchesByBook, bookId, kvp.Key, score);
                }
            }

            return bestMatchesByBook.Values
                .OrderByDescending(m => m.MatchScore)
                .ThenBy(m => m.Pinyin?.Length ?? int.MaxValue)
                .ThenBy(m => m.BookId)
                .ToList();
        }

        /// <summary>
        /// 解析完整的拼音定位字符串
        /// </summary>
        public async Task<ParseResult> ParseAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new ParseResult { Success = false };

            var trimmed = input.Trim();
            
            // 智能分割：处理"创世记1"和"创世记 1"两种格式
            var parts = new List<string>();
            
            // 第一步：提取书卷名
            string bookName = null;
            int bookId = -1;
            
            // 尝试匹配所有66卷书
            foreach (var book in BibleBookConfig.Books)
            {
                if (trimmed.StartsWith(book.Name))
                {
                    bookName = book.Name;
                    bookId = book.BookId;
                    trimmed = trimmed.Substring(book.Name.Length).Trim();
                    break;
                }
            }
            
            // 如果没匹配到书卷名，尝试拼音
            if (bookId == -1)
            {
                var firstWord = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(firstWord))
                {
                    var matchedBookIds = FindBookIdsByExactAlias(firstWord.ToLower());
                    if (matchedBookIds.Count == 1)
                    {
                        bookId = matchedBookIds[0];
                        var book = BibleBookConfig.GetBook(bookId);
                        if (book != null)
                        {
                            bookName = book.Name;
                            trimmed = trimmed.Substring(firstWord.Length).Trim();
                        }
                    }
                }
            }
            
            if (bookId == -1 || string.IsNullOrEmpty(bookName))
                return new ParseResult { Success = false };
            
            var bookInfo = BibleBookConfig.GetBook(bookId);
            if (bookInfo == null)
                return new ParseResult { Success = false };

            // 只有书卷
            if (string.IsNullOrWhiteSpace(trimmed))
                return new ParseResult 
                { 
                    Success = true, 
                    BookId = bookId, 
                    BookName = bookInfo.Name, 
                    Type = LocationType.Book 
                };

            // 解析剩余部分（章号和节号）
            var remainingParts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            // 解析章号
            if (remainingParts.Length == 0 || !int.TryParse(remainingParts[0], out int chapter))
                return new ParseResult { Success = false };

            // 超出章数，使用最后一章
            if (chapter > bookInfo.ChapterCount)
                chapter = bookInfo.ChapterCount;
            if (chapter < 1)
                chapter = 1;

            // 只有章号
            if (remainingParts.Length == 1)
                return new ParseResult 
                { 
                    Success = true, 
                    BookId = bookId, 
                    BookName = bookInfo.Name, 
                    Chapter = chapter, 
                    Type = LocationType.Chapter 
                };

            // 解析节号范围
            // 支持两种格式：
            // 1. "创世记 2 1-4" (连字符分隔)
            // 2. "创世记 2 1 4" (空格分隔)
            if (remainingParts.Length < 2)
                return new ParseResult { Success = false };
            
            int startVerse, endVerse;
            
            if (remainingParts[1].Contains('-'))
            {
                // 格式1: 连字符分隔
                var verseParts = remainingParts[1].Split('-');
                if (verseParts.Length != 2 || 
                    !int.TryParse(verseParts[0], out startVerse) || 
                    !int.TryParse(verseParts[1], out endVerse))
                    return new ParseResult { Success = false };
            }
            else if (remainingParts.Length >= 3)
            {
                // 格式2: 空格分隔 "2 1 4" → 章=2, 起始节=1, 结束节=4
                if (!int.TryParse(remainingParts[1], out startVerse) || 
                    !int.TryParse(remainingParts[2], out endVerse))
                    return new ParseResult { Success = false };
            }
            else if (remainingParts.Length == 2)
            {
                // 格式3: 单节号 "2 1" → 章=2, 起始节=1, 结束节=1
                if (!int.TryParse(remainingParts[1], out startVerse))
                    return new ParseResult { Success = false };
                endVerse = startVerse; // 单节号时，起始节和结束节相同
            }
            else
            {
                return new ParseResult { Success = false };
            }

            // 获取该章最大节数
            int maxVerse = await _bibleService.GetVerseCountAsync(bookId, chapter);
            if (maxVerse <= 0) maxVerse = 176; // 如果查询失败，使用最大值
            
            if (startVerse > maxVerse) startVerse = maxVerse;
            if (endVerse > maxVerse) endVerse = maxVerse;
            if (startVerse < 1) startVerse = 1;
            if (endVerse < startVerse) 
            {
                // 自动交换起始和结束节
                (startVerse, endVerse) = (endVerse, startVerse);
            }

            return new ParseResult
            {
                Success = true,
                BookId = bookId,
                BookName = bookInfo.Name,
                Chapter = chapter,
                StartVerse = startVerse,
                EndVerse = endVerse,
                Type = LocationType.VerseRange
            };
        }

        /// <summary>
        /// 格式化显示输入内容
        /// </summary>
        public async Task<string> FormatDisplayAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var trimmed = input.Trim();
            
            // 提取书卷名
            string bookName = null;
            string remaining = trimmed;
            
            foreach (var book in BibleBookConfig.Books)
            {
                if (trimmed.StartsWith(book.Name))
                {
                    bookName = book.Name;
                    remaining = trimmed.Substring(book.Name.Length).Trim();
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(bookName))
                return input;
            
            // 情况1: 只有书卷名
            if (string.IsNullOrEmpty(remaining))
                return bookName;
            
            // 解析剩余部分（可能是 "4" 或 " 4" 或 "4 1" 等）
            var parts = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length == 0)
                return bookName;
            
            // 情况2: 书卷 + 章号
            if (parts.Length == 1 && int.TryParse(parts[0], out int chapter))
            {
                // 检查原始输入是否有空格结尾
                if (input.EndsWith(" "))
                    return $"{bookName}{chapter}:";
                else
                    return $"{bookName} {chapter}";
            }
            
            // 情况3: 书卷 + 章号 + 起始节
            if (parts.Length == 2 && int.TryParse(parts[0], out chapter) && int.TryParse(parts[1], out int startVerse))
            {
                // 第2个空格后自动加连字符
                if (input.EndsWith(" "))
                    return $"{bookName}{chapter}:{startVerse}-";
                else
                    return $"{bookName}{chapter}:{startVerse}";
            }
            
            // 情况4: 书卷 + 章号 + 起始节 + 结束节
            if (parts.Length == 3 && int.TryParse(parts[0], out chapter) && 
                int.TryParse(parts[1], out startVerse) && int.TryParse(parts[2], out int endVerse))
            {
                return $"{bookName}{chapter}:{startVerse}-{endVerse}";
            }
            
            // 其他格式：尝试标准解析
            var result = await ParseAsync(trimmed);
            if (result.Success)
            {
                if (result.Type == LocationType.Chapter)
                    return $"{result.BookName} {result.Chapter}";
                if (result.Type == LocationType.VerseRange)
                    return $"{result.BookName}{result.Chapter}:{result.StartVerse}-{result.EndVerse}";
            }
            
            return input;
        }

        /// <summary>
        /// 自动替换拼音为书卷名
        /// </summary>
        public string ReplaceWithBookName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return input;

            // 检查第一部分是否是拼音
            var bookIds = FindBookIdsByExactAlias(parts[0].ToLower());
            if (bookIds.Count == 1)
            {
                int bookId = bookIds[0];
                var book = BibleBookConfig.GetBook(bookId);
                if (book != null)
                {
                    parts[0] = book.Name;
                    return string.Join(" ", parts);
                }
            }

            return input;
        }

        private static List<int> FindBookIdsByExactAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return new List<int>();
            }

            alias = alias.ToLower().Trim();
            var ids = new HashSet<int>();
            if (PinyinMap.TryGetValue(alias, out int directBookId))
            {
                ids.Add(directBookId);
            }

            if (ShortAliasMap.TryGetValue(alias, out var aliasBookIds))
            {
                foreach (var id in aliasBookIds)
                {
                    ids.Add(id);
                }
            }

            return ids.OrderBy(id => id).ToList();
        }

        private static Dictionary<string, List<int>> BuildShortAliasMap()
        {
            var result = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var book in BibleBookConfig.Books)
            {
                var aliases = BuildShortNameAliases(book?.ShortName);
                if (aliases.Count == 0)
                {
                    continue;
                }

                foreach (var alias in aliases)
                {
                    if (!result.TryGetValue(alias, out var ids))
                    {
                        ids = new List<int>();
                        result[alias] = ids;
                    }

                    if (!ids.Contains(book.BookId))
                    {
                        ids.Add(book.BookId);
                    }
                }
            }

            return result;
        }

        private static HashSet<string> BuildShortNameAliases(string shortName)
        {
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(shortName))
            {
                return aliases;
            }

            shortName = shortName.Trim();
            var initialChars = new List<char>(shortName.Length);
            var fullSyllables = new List<string>(shortName.Length);
            for (int i = 0; i < shortName.Length; i++)
            {
                char c = shortName[i];
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (c <= 127)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        char ascii = char.ToLowerInvariant(c);
                        initialChars.Add(ascii);
                        fullSyllables.Add(ascii.ToString());
                    }
                    else
                    {
                        return new HashSet<string>(StringComparer.Ordinal);
                    }

                    continue;
                }

                if (!PinyinHelper.IsChinese(c))
                {
                    return new HashSet<string>(StringComparer.Ordinal);
                }

                string pinyin = NormalizePinyinSyllable(PinyinHelper.GetPinyin(c.ToString()));
                if (string.IsNullOrWhiteSpace(pinyin))
                {
                    return new HashSet<string>(StringComparer.Ordinal);
                }

                char initial = char.ToLowerInvariant(pinyin[0]);
                if (!char.IsLetter(initial))
                {
                    return new HashSet<string>(StringComparer.Ordinal);
                }

                initialChars.Add(initial);
                fullSyllables.Add(pinyin);
            }

            if (initialChars.Count == 0)
            {
                return aliases;
            }

            string initialAlias = new string(initialChars.ToArray());
            aliases.Add(initialAlias);

            string fullAlias = string.Concat(fullSyllables);
            if (!string.IsNullOrWhiteSpace(fullAlias))
            {
                aliases.Add(fullAlias);
            }

            return aliases;
        }

        private static string NormalizePinyinSyllable(string pinyin)
        {
            if (string.IsNullOrWhiteSpace(pinyin))
            {
                return string.Empty;
            }

            string normalized = pinyin
                .Trim()
                .ToLowerInvariant()
                .Replace("u:", "v")
                .Replace('ü', 'v');

            string decomposed = normalized.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (char ch in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                char lower = char.ToLowerInvariant(ch);
                if (lower >= 'a' && lower <= 'z')
                {
                    builder.Append(lower);
                }
                else if (lower == 'v')
                {
                    builder.Append('v');
                }
            }

            return builder.ToString();
        }

        private static void TryUpsertMatch(
            Dictionary<int, BibleBookMatch> bestMatchesByBook,
            int bookId,
            string matchedKey,
            int score)
        {
            var book = BibleBookConfig.GetBook(bookId);
            if (book == null)
            {
                return;
            }

            if (!bestMatchesByBook.TryGetValue(bookId, out var existing))
            {
                bestMatchesByBook[bookId] = new BibleBookMatch
                {
                    BookId = bookId,
                    BookName = book.Name,
                    Pinyin = matchedKey,
                    MatchScore = score
                };
                return;
            }

            bool replace = score > existing.MatchScore ||
                           (score == existing.MatchScore &&
                            (matchedKey?.Length ?? int.MaxValue) < (existing.Pinyin?.Length ?? int.MaxValue));
            if (replace)
            {
                existing.Pinyin = matchedKey;
                existing.MatchScore = score;
            }
        }
    }

    /// <summary>
    /// 书卷匹配结果
    /// </summary>
    public class BibleBookMatch
    {
        public int BookId { get; set; }
        public string BookName { get; set; }
        public string Pinyin { get; set; }
        public int MatchScore { get; set; }
    }

    /// <summary>
    /// 定位解析结果
    /// </summary>
    public class ParseResult
    {
        public bool Success { get; set; }
        public int? BookId { get; set; }
        public string BookName { get; set; }
        public int? Chapter { get; set; }
        public int? StartVerse { get; set; }
        public int? EndVerse { get; set; }
        public LocationType Type { get; set; }
    }

    /// <summary>
    /// 定位类型
    /// </summary>
    public enum LocationType
    {
        Book,        // 仅定位到书卷
        Chapter,     // 定位到章
        VerseRange   // 定位到节范围
    }
}

