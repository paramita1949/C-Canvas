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
        private readonly string _databasePath;

        public BibleService(IMemoryCache cache)
        {
            _cache = cache;
            
            // 构建数据库路径
            _databasePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "data", "assets", "bible.db");

            //#if DEBUG
            //Debug.WriteLine($"[圣经服务] 数据库路径: {_databasePath}");
            //Debug.WriteLine($"[圣经服务] 数据库存在: {File.Exists(_databasePath)}");
            //#endif
        }

        /// <summary>
        /// 创建数据库上下文
        /// </summary>
        private BibleDbContext CreateDbContext()
        {
            return new BibleDbContext(_databasePath);
        }

        /// <summary>
        /// 获取单节经文（带缓存）
        /// </summary>
        public async Task<BibleVerse> GetVerseAsync(int book, int chapter, int verse)
        {
            var cacheKey = $"verse_{book}_{chapter}_{verse}";

            if (_cache.TryGetValue(cacheKey, out BibleVerse cachedVerse))
            {
                #if DEBUG
                Debug.WriteLine($"[圣经服务] 缓存命中: {cacheKey}");
                #endif
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
                Debug.WriteLine($"[圣经服务] 查询单节耗时: {sw.ElapsedMilliseconds}ms");
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
                #if DEBUG
                Debug.WriteLine($"[圣经服务] 缓存命中: {cacheKey}");
                #endif
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

                #if DEBUG
                sw.Stop();
                Debug.WriteLine($"[圣经服务] 查询整章: {sw.ElapsedMilliseconds}ms, 结果数: {verses.Count}");
                #endif

                // 缓存30分钟
                _cache.Set(cacheKey, verses, TimeSpan.FromMinutes(30));

                return verses;
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
        /// 获取章节标题
        /// </summary>
        public async Task<List<BibleTitle>> GetChapterTitlesAsync(int book, int chapter)
        {
            var cacheKey = $"titles_{book}_{chapter}";

            if (_cache.TryGetValue(cacheKey, out List<BibleTitle> cachedTitles))
            {
                #if DEBUG
                Debug.WriteLine($"[圣经服务] 缓存命中: {cacheKey}");
                #endif
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
                if (!File.Exists(_databasePath))
                {
                    #if DEBUG
                    Debug.WriteLine($"[圣经服务] 数据库文件不存在: {_databasePath}");
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

