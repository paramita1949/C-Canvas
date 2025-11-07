using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models.Bible;
using ImageColorChanger.Services.Interfaces;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services.Implementations
{
    /// <summary>
    /// 圣经数据服务实现
    /// </summary>
    public class BibleService : IBibleService
    {
        private readonly IMemoryCache _cache;
        private readonly ConfigManager _configManager;
        private string _currentDatabasePath;

        public BibleService(IMemoryCache cache, ConfigManager configManager)
        {
            _cache = cache;
            _configManager = configManager;
            
            // 根据配置获取数据库路径
            _currentDatabasePath = GetDatabasePath();

            #if DEBUG
            Debug.WriteLine($"[圣经服务] 数据库路径: {_currentDatabasePath}");
            Debug.WriteLine($"[圣经服务] 数据库存在: {File.Exists(_currentDatabasePath)}");
            #endif
        }

        /// <summary>
        /// 根据配置获取数据库路径
        /// </summary>
        private string GetDatabasePath()
        {
            // 从配置中获取数据库文件名
            var dbFileName = _configManager.BibleDatabaseFileName ?? "bible.db";
            
            var path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "data", "assets", dbFileName);

            #if DEBUG
            Debug.WriteLine($"[圣经服务] 选择数据库: {dbFileName}");
            #endif

            return path;
        }

        /// <summary>
        /// 更新数据库路径（译本切换时调用）
        /// </summary>
        public void UpdateDatabasePath()
        {
            var newPath = GetDatabasePath();
            if (newPath != _currentDatabasePath)
            {
                _currentDatabasePath = newPath;
                
                // 清除所有缓存，因为数据来源已改变
                if (_cache is MemoryCache memCache)
                {
                    memCache.Compact(1.0);
                }

                #if DEBUG
                Debug.WriteLine($"[圣经服务] 切换数据库: {_currentDatabasePath}");
                #endif
            }
        }

        /// <summary>
        /// 创建数据库上下文
        /// </summary>
        private BibleDbContext CreateDbContext()
        {
            return new BibleDbContext(_currentDatabasePath);
        }

        /// <summary>
        /// 获取单节经文（带缓存）
        /// </summary>
        public async Task<BibleVerse> GetVerseAsync(int book, int chapter, int verse)
        {
            var cacheKey = $"verse_{book}_{chapter}_{verse}";

            if (_cache.TryGetValue(cacheKey, out BibleVerse cachedVerse))
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经服务] 缓存命中: {cacheKey}");
                //#endif
                return cachedVerse;
            }

            #if DEBUG
            var sw = Stopwatch.StartNew();
            #endif

            try
            {
                using var context = CreateDbContext();
                var result = await context.Bible
                    .FirstOrDefaultAsync(v =>
                        v.Book == book &&
                        v.Chapter == chapter &&
                        v.Verse == verse);

                #if DEBUG
                sw.Stop();
                //Debug.WriteLine($"[圣经服务] 查询单节耗时: {sw.ElapsedMilliseconds}ms");
                #endif

                if (result != null)
                {
                    // 缓存10分钟
                    _cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
                }

                return result;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经服务] 查询失败: {ex.Message}");
                throw;
            }
#else
            catch (Exception)
            {
                throw;
            }
#endif
        }

        /// <summary>
        /// 获取整章经文（带缓存）
        /// </summary>
        public async Task<List<BibleVerse>> GetChapterVersesAsync(int book, int chapter)
        {
            var cacheKey = $"chapter_{book}_{chapter}";

            if (_cache.TryGetValue(cacheKey, out List<BibleVerse> cachedVerses))
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经服务] 缓存命中: {cacheKey}");
                //#endif
                return cachedVerses;
            }

            #if DEBUG
            var sw = Stopwatch.StartNew();
            #endif

            try
            {
                using var context = CreateDbContext();
                var verses = await context.Bible
                    .Where(v => v.Book == book && v.Chapter == chapter)
                    .OrderBy(v => v.Verse)
                    .ToListAsync();

                // 🔧 处理只有"-"符号的节，合并到前一节
                var processedVerses = ProcessDashOnlyVerses(verses);

                //#if DEBUG
                //sw.Stop();
                //Debug.WriteLine($"[圣经服务] 查询整章: {sw.ElapsedMilliseconds}ms, 结果数: {verses.Count}, 处理后: {processedVerses.Count}");
                //#endif

                // 缓存30分钟
                _cache.Set(cacheKey, processedVerses, TimeSpan.FromMinutes(30));

                return processedVerses;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经服务] 查询失败: {ex.Message}");
                throw;
            }
#else
            catch (Exception)
            {
                throw;
            }
#endif
        }

        /// <summary>
        /// 处理只有"-"符号的节，合并到前一节
        /// 例如：约书亚记3章10节有经文，11节只有"-"，则合并显示为"10、11 经文内容..."
        /// 同时构建节号映射表，用于查询用户选择的节号对应的实际经文
        /// </summary>
        private (List<BibleVerse> verses, Dictionary<int, BibleVerse> verseMap) ProcessDashOnlyVersesWithMap(List<BibleVerse> verses)
        {
            if (verses == null || verses.Count == 0)
                return (verses, new Dictionary<int, BibleVerse>());

            var result = new List<BibleVerse>();
            var verseMap = new Dictionary<int, BibleVerse>(); // 节号 -> 实际经文的映射
            var dashVerseNumbers = new List<int>(); // 记录需要合并的"-"节号
            
            for (int i = 0; i < verses.Count; i++)
            {
                var currentVerse = verses[i];
                var scripture = currentVerse.Scripture?.Trim() ?? "";

                // 检查当前节是否只有"-"符号
                if (scripture == "-")
                {
                    // 记录这个节号，等待合并到前一节
                    dashVerseNumbers.Add(currentVerse.Verse);
                    
                    #if DEBUG
                    Debug.WriteLine($"[圣经服务] 发现'-'节: 第{currentVerse.Verse}节");
                    #endif
                }
                else
                {
                    // 正常经文
                    // 如果前面有"-"节，需要合并显示
                    if (dashVerseNumbers.Count > 0 && result.Count > 0)
                    {
                        // 将之前累积的"-"节号合并到上一节
                        var previousVerse = result[result.Count - 1];
                        
                        // 构建合并的节号显示，如"10、11、12"
                        var mergedVerseNumbers = previousVerse.DisplayVerseNumber ?? previousVerse.Verse.ToString();
                        foreach (var dashVerse in dashVerseNumbers)
                        {
                            mergedVerseNumbers += $"、{dashVerse}";
                            // 🔧 建立映射："-"节号 -> 前一节的经文
                            verseMap[dashVerse] = previousVerse;
                        }
                        
                        previousVerse.DisplayVerseNumber = mergedVerseNumbers;
                        
                        #if DEBUG
                        Debug.WriteLine($"[圣经服务] 合并节号: {mergedVerseNumbers} => {previousVerse.Scripture?.Substring(0, Math.Min(20, previousVerse.Scripture.Length))}...");
                        #endif
                        
                        dashVerseNumbers.Clear();
                    }
                    else if (dashVerseNumbers.Count > 0)
                    {
                        // 前面有"-"节，但没有前一节（即"-"在最开头）
                        // 这种情况下，将"-"节号归到当前节
                        var mergedVerseNumbers = currentVerse.Verse.ToString();
                        foreach (var dashVerse in dashVerseNumbers)
                        {
                            mergedVerseNumbers = $"{dashVerse}、" + mergedVerseNumbers;
                            // 🔧 建立映射："-"节号 -> 当前节的经文
                            verseMap[dashVerse] = currentVerse;
                        }
                        
                        currentVerse.DisplayVerseNumber = mergedVerseNumbers;
                        
                        #if DEBUG
                        Debug.WriteLine($"[圣经服务] 向后合并节号: {mergedVerseNumbers} => {currentVerse.Scripture?.Substring(0, Math.Min(20, currentVerse.Scripture.Length))}...");
                        #endif
                        
                        dashVerseNumbers.Clear();
                    }
                    
                    // 🔧 建立映射：正常节号 -> 自己
                    verseMap[currentVerse.Verse] = currentVerse;
                    
                    // 添加当前正常经文
                    result.Add(currentVerse);
                }
            }
            
            // 处理末尾的"-"节（如果最后几节都是"-"）
            if (dashVerseNumbers.Count > 0 && result.Count > 0)
            {
                var lastVerse = result[result.Count - 1];
                var mergedVerseNumbers = lastVerse.DisplayVerseNumber ?? lastVerse.Verse.ToString();
                foreach (var dashVerse in dashVerseNumbers)
                {
                    mergedVerseNumbers += $"、{dashVerse}";
                    // 🔧 建立映射：末尾"-"节号 -> 最后一节的经文
                    verseMap[dashVerse] = lastVerse;
                }
                lastVerse.DisplayVerseNumber = mergedVerseNumbers;
                
                #if DEBUG
                Debug.WriteLine($"[圣经服务] 末尾合并节号: {mergedVerseNumbers}");
                #endif
            }

            return (result, verseMap);
        }

        /// <summary>
        /// 处理只有"-"符号的节，合并到前一节（简化版本，仅返回经文列表）
        /// </summary>
        private List<BibleVerse> ProcessDashOnlyVerses(List<BibleVerse> verses)
        {
            var (result, _) = ProcessDashOnlyVersesWithMap(verses);
            return result;
        }

        /// <summary>
        /// 获取指定范围的经文（智能处理"-"节）
        /// 例如：用户选择11节，但11节是"-"，则返回包含11节的实际经文（如第10节）
        /// </summary>
        public async Task<List<BibleVerse>> GetVerseRangeAsync(int book, int chapter, int startVerse, int endVerse)
        {
            // 获取整章经文（已处理"-"节合并）
            var allVerses = await GetChapterVersesAsync(book, chapter);
            
            // 重新获取原始经文以构建映射
            using var context = CreateDbContext();
            var originalVerses = await context.Bible
                .Where(v => v.Book == book && v.Chapter == chapter)
                .OrderBy(v => v.Verse)
                .ToListAsync();
            
            // 构建节号映射
            var (processedVerses, verseMap) = ProcessDashOnlyVersesWithMap(originalVerses);
            
            // 使用HashSet来收集所有需要显示的经文（去重）
            var versesToShow = new HashSet<BibleVerse>();
            
            // 遍历用户选择的节号范围
            for (int verseNum = startVerse; verseNum <= endVerse; verseNum++)
            {
                if (verseMap.TryGetValue(verseNum, out var mappedVerse))
                {
                    // 找到了对应的经文（可能是"-"节映射到的实际经文）
                    versesToShow.Add(mappedVerse);
                    
                    #if DEBUG
                    if (verseNum != mappedVerse.Verse)
                    {
                        Debug.WriteLine($"[圣经服务] 节号{verseNum}映射到第{mappedVerse.Verse}节");
                    }
                    #endif
                }
            }
            
            // 按节号排序返回
            var result = versesToShow.OrderBy(v => v.Verse).ToList();
            
            #if DEBUG
            Debug.WriteLine($"[圣经服务] 获取节范围 {startVerse}-{endVerse}，返回 {result.Count} 节经文");
            #endif
            
            return result;
        }

        /// <summary>
        /// 获取章节标题
        /// </summary>
        public async Task<List<BibleTitle>> GetChapterTitlesAsync(int book, int chapter)
        {
            var cacheKey = $"titles_{book}_{chapter}";

            if (_cache.TryGetValue(cacheKey, out List<BibleTitle> cachedTitles))
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经服务] 缓存命中: {cacheKey}");
                //#endif
                return cachedTitles;
            }

            try
            {
                using var context = CreateDbContext();
                var titles = await context.Titles
                    .Where(t => t.Book == book && t.Chapter == chapter)
                    .OrderBy(t => t.Verse)
                    .ToListAsync();

                #if DEBUG
                Debug.WriteLine($"[圣经服务] 查询标题: 结果数: {titles.Count}");
                #endif

                // 缓存30分钟
                _cache.Set(cacheKey, titles, TimeSpan.FromMinutes(30));

                return titles;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经服务] 查询标题失败: {ex.Message}");
                return new List<BibleTitle>();
            }
#else
            catch (Exception)
            {
                return new List<BibleTitle>();
            }
#endif
        }

        /// <summary>
        /// 获取整章内容（经文+标题混合）
        /// </summary>
        public async Task<List<object>> GetChapterContentAsync(int book, int chapter)
        {
            var verses = await GetChapterVersesAsync(book, chapter);
            var titles = await GetChapterTitlesAsync(book, chapter);

            // 合并经文和标题，按节号排序
            var content = new List<object>();
            var titleDict = titles.ToDictionary(t => t.Verse);

            foreach (var verse in verses)
            {
                // 如果该节有标题，先添加标题
                if (titleDict.ContainsKey(verse.Verse))
                {
                    content.Add(titleDict[verse.Verse]);
                }

                // 添加经文
                content.Add(verse);
            }

            return content;
        }

        /// <summary>
        /// 搜索经文
        /// </summary>
        public async Task<List<BibleSearchResult>> SearchVersesAsync(string keyword, int? bookId = null)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new List<BibleSearchResult>();

            #if DEBUG
            var sw = Stopwatch.StartNew();
            #endif

            try
            {
                using var context = CreateDbContext();
                var query = context.Bible.AsQueryable();

                if (bookId.HasValue)
                {
                    query = query.Where(v => v.Book == bookId.Value);
                }

                query = query.Where(v => v.Scripture.Contains(keyword));

                var results = await query
                    .Take(100)
                    .Select(v => new BibleSearchResult
                    {
                        Book = v.Book,
                        Chapter = v.Chapter,
                        Verse = v.Verse,
                        Scripture = v.Scripture,
                        BookName = BibleBookConfig.GetBook(v.Book).Name,
                        Reference = $"{BibleBookConfig.GetBook(v.Book).Name} {v.Chapter}:{v.Verse}"
                    })
                    .ToListAsync();

                #if DEBUG
                sw.Stop();
                Debug.WriteLine($"[圣经服务] 搜索 '{keyword}': {sw.ElapsedMilliseconds}ms, 结果数: {results.Count}");
                #endif

                return results;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经服务] 搜索失败: {ex.Message}");
                throw;
            }
#else
            catch (Exception)
            {
                throw;
            }
#endif
        }

        /// <summary>
        /// 获取书卷章数
        /// </summary>
        public int GetChapterCount(int book)
        {
            return BibleBookConfig.GetBook(book)?.ChapterCount ?? 0;
        }

        /// <summary>
        /// 获取所有章节的节数(批量查询,用于初始化)
        /// </summary>
        public async Task<Dictionary<(int book, int chapter), int>> GetAllVerseCountsAsync()
        {
            const string cacheKey = "all_verse_counts";

            if (_cache.TryGetValue(cacheKey, out Dictionary<(int, int), int> cachedCounts))
            {
                return cachedCounts;
            }

            try
            {
                using var context = CreateDbContext();
                
                // 一次性查询所有章节的节数
                var counts = await context.Bible
                    .GroupBy(v => new { v.Book, v.Chapter })
                    .Select(g => new { g.Key.Book, g.Key.Chapter, Count = g.Count() })
                    .ToDictionaryAsync(x => (x.Book, x.Chapter), x => x.Count);

                // 缓存1小时
                _cache.Set(cacheKey, counts, TimeSpan.FromHours(1));

                return counts;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经服务] 批量获取节数失败: {ex.Message}");
                return new Dictionary<(int, int), int>();
            }
#else
            catch (Exception)
            {
                return new Dictionary<(int, int), int>();
            }
#endif
        }

        /// <summary>
        /// 获取章节数
        /// </summary>
        public async Task<int> GetVerseCountAsync(int book, int chapter)
        {
            var cacheKey = $"versecount_{book}_{chapter}";

            if (_cache.TryGetValue(cacheKey, out int cachedCount))
            {
                return cachedCount;
            }

            try
            {
                using var context = CreateDbContext();
                var count = await context.Bible
                    .CountAsync(v => v.Book == book && v.Chapter == chapter);

                // 缓存1小时
                _cache.Set(cacheKey, count, TimeSpan.FromHours(1));

                return count;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经服务] 获取节数失败: {ex.Message}");
                return 0;
            }
#else
            catch (Exception)
            {
                return 0;
            }
#endif
        }

        /// <summary>
        /// 检查数据库是否可用
        /// </summary>
        public async Task<bool> IsDatabaseAvailableAsync()
        {
            try
            {
                if (!File.Exists(_currentDatabasePath))
                {
                    #if DEBUG
                    Debug.WriteLine($"[圣经服务] 数据库文件不存在: {_currentDatabasePath}");
                    #endif
                    return false;
                }

                using var context = CreateDbContext();
                var count = await context.Bible.CountAsync();

                //#if DEBUG
                //Debug.WriteLine($"[圣经服务] 数据库可用，经文数: {count}");
                //#endif

                return count > 0;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经服务] 数据库检查失败: {ex.Message}");
                return false;
            }
#else
            catch (Exception)
            {
                return false;
            }
#endif
        }

        /// <summary>
        /// 获取数据库元数据
        /// </summary>
        public async Task<Dictionary<string, string>> GetMetadataAsync()
        {
            try
            {
                using var context = CreateDbContext();
                var metadata = await context.Metadata
                    .ToDictionaryAsync(m => m.Name, m => m.Value);
                return metadata;
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"[圣经服务] 获取元数据失败: {ex.Message}");
                return new Dictionary<string, string>();
            }
#else
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
#endif
        }
    }
}

