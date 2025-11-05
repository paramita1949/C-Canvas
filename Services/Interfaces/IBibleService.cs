using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Bible;

namespace ImageColorChanger.Services.Interfaces
{
    /// <summary>
    /// 圣经数据服务接口
    /// </summary>
    public interface IBibleService
    {
        /// <summary>
        /// 获取单节经文
        /// </summary>
        Task<BibleVerse> GetVerseAsync(int book, int chapter, int verse);

        /// <summary>
        /// 获取整章经文
        /// </summary>
        Task<List<BibleVerse>> GetChapterVersesAsync(int book, int chapter);

        /// <summary>
        /// 获取章节标题
        /// </summary>
        Task<List<BibleTitle>> GetChapterTitlesAsync(int book, int chapter);

        /// <summary>
        /// 获取整章内容（经文+标题混合）
        /// </summary>
        Task<List<object>> GetChapterContentAsync(int book, int chapter);

        /// <summary>
        /// 搜索经文
        /// </summary>
        Task<List<BibleSearchResult>> SearchVersesAsync(string keyword, int? bookId = null);

        /// <summary>
        /// 获取书卷章数
        /// </summary>
        int GetChapterCount(int book);

        /// <summary>
        /// 获取所有章节的节数（批量查询，用于初始化）
        /// </summary>
        Task<Dictionary<(int book, int chapter), int>> GetAllVerseCountsAsync();

        /// <summary>
        /// 获取章节数
        /// </summary>
        Task<int> GetVerseCountAsync(int book, int chapter);

        /// <summary>
        /// 检查数据库是否可用
        /// </summary>
        Task<bool> IsDatabaseAvailableAsync();

        /// <summary>
        /// 获取数据库元数据
        /// </summary>
        Task<Dictionary<string, string>> GetMetadataAsync();
    }
}

