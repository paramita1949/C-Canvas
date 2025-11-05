using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Database.Models.Bible;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 圣经书卷配置静态类
    /// </summary>
    public static class BibleBookConfig
    {
        /// <summary>
        /// 所有66卷圣经书卷信息
        /// </summary>
        public static readonly List<BibleBook> Books = new()
        {
            // 旧约 - 摩西五经 (1-5)
            new() { BookId = 1, Name = "创世记", ShortName = "创", ChapterCount = 50, Category = "摩西五经", Testament = "旧约" },
            new() { BookId = 2, Name = "出埃及记", ShortName = "出", ChapterCount = 40, Category = "摩西五经", Testament = "旧约" },
            new() { BookId = 3, Name = "利未记", ShortName = "利", ChapterCount = 27, Category = "摩西五经", Testament = "旧约" },
            new() { BookId = 4, Name = "民数记", ShortName = "民", ChapterCount = 36, Category = "摩西五经", Testament = "旧约" },
            new() { BookId = 5, Name = "申命记", ShortName = "申", ChapterCount = 34, Category = "摩西五经", Testament = "旧约" },

            // 旧约 - 历史书 (6-17)
            new() { BookId = 6, Name = "约书亚记", ShortName = "书", ChapterCount = 24, Category = "历史书", Testament = "旧约" },
            new() { BookId = 7, Name = "士师记", ShortName = "士", ChapterCount = 21, Category = "历史书", Testament = "旧约" },
            new() { BookId = 8, Name = "路得记", ShortName = "得", ChapterCount = 4, Category = "历史书", Testament = "旧约" },
            new() { BookId = 9, Name = "撒母耳记上", ShortName = "撒上", ChapterCount = 31, Category = "历史书", Testament = "旧约" },
            new() { BookId = 10, Name = "撒母耳记下", ShortName = "撒下", ChapterCount = 24, Category = "历史书", Testament = "旧约" },
            new() { BookId = 11, Name = "列王纪上", ShortName = "王上", ChapterCount = 22, Category = "历史书", Testament = "旧约" },
            new() { BookId = 12, Name = "列王纪下", ShortName = "王下", ChapterCount = 25, Category = "历史书", Testament = "旧约" },
            new() { BookId = 13, Name = "历代志上", ShortName = "代上", ChapterCount = 29, Category = "历史书", Testament = "旧约" },
            new() { BookId = 14, Name = "历代志下", ShortName = "代下", ChapterCount = 36, Category = "历史书", Testament = "旧约" },
            new() { BookId = 15, Name = "以斯拉记", ShortName = "拉", ChapterCount = 10, Category = "历史书", Testament = "旧约" },
            new() { BookId = 16, Name = "尼希米记", ShortName = "尼", ChapterCount = 13, Category = "历史书", Testament = "旧约" },
            new() { BookId = 17, Name = "以斯帖记", ShortName = "斯", ChapterCount = 10, Category = "历史书", Testament = "旧约" },

            // 旧约 - 诗歌智慧书 (18-22)
            new() { BookId = 18, Name = "约伯记", ShortName = "伯", ChapterCount = 42, Category = "诗歌智慧书", Testament = "旧约" },
            new() { BookId = 19, Name = "诗篇", ShortName = "诗", ChapterCount = 150, Category = "诗歌智慧书", Testament = "旧约" },
            new() { BookId = 20, Name = "箴言", ShortName = "箴", ChapterCount = 31, Category = "诗歌智慧书", Testament = "旧约" },
            new() { BookId = 21, Name = "传道书", ShortName = "传", ChapterCount = 12, Category = "诗歌智慧书", Testament = "旧约" },
            new() { BookId = 22, Name = "雅歌", ShortName = "歌", ChapterCount = 8, Category = "诗歌智慧书", Testament = "旧约" },

            // 旧约 - 大先知书 (23-27)
            new() { BookId = 23, Name = "以赛亚书", ShortName = "赛", ChapterCount = 66, Category = "大先知书", Testament = "旧约" },
            new() { BookId = 24, Name = "耶利米书", ShortName = "耶", ChapterCount = 52, Category = "大先知书", Testament = "旧约" },
            new() { BookId = 25, Name = "耶利米哀歌", ShortName = "哀", ChapterCount = 5, Category = "大先知书", Testament = "旧约" },
            new() { BookId = 26, Name = "以西结书", ShortName = "结", ChapterCount = 48, Category = "大先知书", Testament = "旧约" },
            new() { BookId = 27, Name = "但以理书", ShortName = "但", ChapterCount = 12, Category = "大先知书", Testament = "旧约" },

            // 旧约 - 小先知书 (28-39)
            new() { BookId = 28, Name = "何西阿书", ShortName = "何", ChapterCount = 14, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 29, Name = "约珥书", ShortName = "珥", ChapterCount = 3, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 30, Name = "阿摩司书", ShortName = "摩", ChapterCount = 9, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 31, Name = "俄巴底亚书", ShortName = "俄", ChapterCount = 1, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 32, Name = "约拿书", ShortName = "拿", ChapterCount = 4, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 33, Name = "弥迦书", ShortName = "弥", ChapterCount = 7, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 34, Name = "那鸿书", ShortName = "鸿", ChapterCount = 3, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 35, Name = "哈巴谷书", ShortName = "哈", ChapterCount = 3, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 36, Name = "西番雅书", ShortName = "番", ChapterCount = 3, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 37, Name = "哈该书", ShortName = "该", ChapterCount = 2, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 38, Name = "撒迦利亚书", ShortName = "亚", ChapterCount = 14, Category = "小先知书", Testament = "旧约" },
            new() { BookId = 39, Name = "玛拉基书", ShortName = "玛", ChapterCount = 4, Category = "小先知书", Testament = "旧约" },

            // 新约 - 福音书 (40-43)
            new() { BookId = 40, Name = "马太福音", ShortName = "太", ChapterCount = 28, Category = "福音书", Testament = "新约" },
            new() { BookId = 41, Name = "马可福音", ShortName = "可", ChapterCount = 16, Category = "福音书", Testament = "新约" },
            new() { BookId = 42, Name = "路加福音", ShortName = "路", ChapterCount = 24, Category = "福音书", Testament = "新约" },
            new() { BookId = 43, Name = "约翰福音", ShortName = "约", ChapterCount = 21, Category = "福音书", Testament = "新约" },

            // 新约 - 历史书 (44)
            new() { BookId = 44, Name = "使徒行传", ShortName = "徒", ChapterCount = 28, Category = "历史书", Testament = "新约" },

            // 新约 - 保罗书信 (45-57)
            new() { BookId = 45, Name = "罗马书", ShortName = "罗", ChapterCount = 16, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 46, Name = "哥林多前书", ShortName = "林前", ChapterCount = 16, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 47, Name = "哥林多后书", ShortName = "林后", ChapterCount = 13, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 48, Name = "加拉太书", ShortName = "加", ChapterCount = 6, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 49, Name = "以弗所书", ShortName = "弗", ChapterCount = 6, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 50, Name = "腓立比书", ShortName = "腓", ChapterCount = 4, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 51, Name = "歌罗西书", ShortName = "西", ChapterCount = 4, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 52, Name = "帖撒罗尼迦前书", ShortName = "帖前", ChapterCount = 5, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 53, Name = "帖撒罗尼迦后书", ShortName = "帖后", ChapterCount = 3, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 54, Name = "提摩太前书", ShortName = "提前", ChapterCount = 6, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 55, Name = "提摩太后书", ShortName = "提后", ChapterCount = 4, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 56, Name = "提多书", ShortName = "多", ChapterCount = 3, Category = "保罗书信", Testament = "新约" },
            new() { BookId = 57, Name = "腓利门书", ShortName = "门", ChapterCount = 1, Category = "保罗书信", Testament = "新约" },

            // 新约 - 普通书信 (58-65)
            new() { BookId = 58, Name = "希伯来书", ShortName = "来", ChapterCount = 13, Category = "普通书信", Testament = "新约" },
            new() { BookId = 59, Name = "雅各书", ShortName = "雅", ChapterCount = 5, Category = "普通书信", Testament = "新约" },
            new() { BookId = 60, Name = "彼得前书", ShortName = "彼前", ChapterCount = 5, Category = "普通书信", Testament = "新约" },
            new() { BookId = 61, Name = "彼得后书", ShortName = "彼后", ChapterCount = 3, Category = "普通书信", Testament = "新约" },
            new() { BookId = 62, Name = "约翰一书", ShortName = "约一", ChapterCount = 5, Category = "普通书信", Testament = "新约" },
            new() { BookId = 63, Name = "约翰二书", ShortName = "约二", ChapterCount = 1, Category = "普通书信", Testament = "新约" },
            new() { BookId = 64, Name = "约翰三书", ShortName = "约三", ChapterCount = 1, Category = "普通书信", Testament = "新约" },
            new() { BookId = 65, Name = "犹大书", ShortName = "犹", ChapterCount = 1, Category = "普通书信", Testament = "新约" },

            // 新约 - 预言书 (66)
            new() { BookId = 66, Name = "启示录", ShortName = "启", ChapterCount = 22, Category = "预言书", Testament = "新约" }
        };

        /// <summary>
        /// 根据书卷编号获取书卷信息
        /// </summary>
        public static BibleBook GetBook(int bookId)
        {
            return Books.FirstOrDefault(b => b.BookId == bookId);
        }

        /// <summary>
        /// 根据书卷名称获取书卷信息
        /// </summary>
        public static BibleBook FindByName(string name)
        {
            return Books.FirstOrDefault(b => 
                b.Name == name || b.ShortName == name);
        }

        /// <summary>
        /// 获取旧约书卷列表
        /// </summary>
        public static List<BibleBook> GetOldTestament()
        {
            return Books.Where(b => b.Testament == "旧约").ToList();
        }

        /// <summary>
        /// 获取新约书卷列表
        /// </summary>
        public static List<BibleBook> GetNewTestament()
        {
            return Books.Where(b => b.Testament == "新约").ToList();
        }

        /// <summary>
        /// 按分类获取书卷列表
        /// </summary>
        public static List<BibleBook> GetBooksByCategory(string category)
        {
            return Books.Where(b => b.Category == category).ToList();
        }

        /// <summary>
        /// 获取所有分类名称
        /// </summary>
        public static List<string> GetAllCategories()
        {
            return Books.Select(b => b.Category).Distinct().ToList();
        }
    }
}

