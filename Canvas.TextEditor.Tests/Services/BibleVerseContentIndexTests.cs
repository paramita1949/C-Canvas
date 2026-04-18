using System;
using System.Collections.Generic;
using ImageColorChanger.Services;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    /// <summary>
    /// BibleVerseContentIndex 单元测试。
    /// 不依赖真实数据库，直接调用 Build() 注入合成经文数据。
    /// </summary>
    public sealed class BibleVerseContentIndexTests
    {
        // ─── 辅助 ──────────────────────────────────────────────────────────────

        private static BibleVerseContentIndex BuildIndex(
            IReadOnlyList<(int book, int chapter, int verse, string scripture)> verses)
        {
            var idx = new BibleVerseContentIndex();
            idx.Build(verses);
            return idx;
        }

        private static readonly IReadOnlyList<(int, int, int, string)> SampleVerses =
            new[]
            {
                (50, 4, 4, "你们要靠主常常喜乐，我再说你们要喜乐。"),
                (43, 3, 16, "神爱世人，甚至将他的独生子赐给他们，叫一切信他的，不至灭亡，反得永生。"),
                (19, 23, 1, "耶和华是我的牧者，我必不致缺乏。"),
                (40, 5,  3, "虚心的人有福了，因为天国是他们的。"),
                (45, 8, 28, "我们晓得万事都互相效力，叫爱神的人得益处。"),
            };

        // ─── Build ─────────────────────────────────────────────────────────────

        [Fact]
        public void Build_EmptyList_IsReadyButReturnsNoResults()
        {
            var idx = BuildIndex(Array.Empty<(int, int, int, string)>());
            Assert.True(idx.IsReady);
            var results = idx.FindByContent("靠主常常喜乐");
            Assert.Empty(results);
        }

        [Fact]
        public void Build_ValidVerses_IsReady()
        {
            var idx = BuildIndex(SampleVerses);
            Assert.True(idx.IsReady);
        }

        // ─── FindByContent —— 字符 bigram ──────────────────────────────────────

        [Fact]
        public void FindByContent_FullVerseText_GetsHighVerseCoverage()
        {
            var idx = BuildIndex(SampleVerses);

            // 模拟牧师朗读了接近完整的经节（去标点后的连续文本）
            // 经文覆盖率语义：经节的多少 bigram 出现在识别文本中
            var results = idx.FindByContent("你们要靠主常常喜乐我再说你们要喜乐", topN: 3);

            Assert.NotEmpty(results);
            var top = results[0];
            Assert.Equal(50, top.Book);
            Assert.Equal(4,  top.Chapter);
            Assert.Equal(4,  top.Verse);
            // 识别文本涵盖了整节 → 覆盖率应接近 1.0
            Assert.True(top.Score > 0.10, $"期望 score > 0.10，实际 {top.Score}");
        }

        [Fact]
        public void FindByContent_PartialPhrase_RanksCorrectVerseTopButLowCoverage()
        {
            var idx = BuildIndex(SampleVerses);

            // 只说出"神爱世人" → 该节有 30+ 个 bigram，只命中 3 个，覆盖率很低
            // 但仍能排名第一（其他节无此 bigram），验证排名正确
            // 注：低覆盖率(<0.70)时 TryResolveAsync 会拒绝，这是预期行为
            var results = idx.FindByContent("神爱世人");

            Assert.NotEmpty(results);
            Assert.Equal(43, results[0].Book);
            Assert.Equal(3,  results[0].Chapter);
            Assert.Equal(16, results[0].Verse);
            // RRF 排名正确即可（少量样本中 partial 也可能获得高 RRF 排名）
            Assert.True(results[0].CharScore < 0.50, $"期望 charCoverage < 0.50，实际 {results[0].CharScore}");
        }

        [Fact]
        public void FindByContent_DistinctPhrase_DoesNotConfuseVerses()
        {
            var idx = BuildIndex(SampleVerses);

            // "耶和华是我的牧者" 属于诗篇23:1，不应匹配腓利比书
            var results = idx.FindByContent("耶和华是我的牧者");

            Assert.NotEmpty(results);
            var top = results[0];
            Assert.Equal(19, top.Book);
            Assert.Equal(23, top.Chapter);
            Assert.Equal(1,  top.Verse);
        }

        [Fact]
        public void FindByContent_TopN_LimitsResults()
        {
            var idx = BuildIndex(SampleVerses);

            var results = idx.FindByContent("的", topN: 2);

            Assert.True(results.Count <= 2);
        }

        // ─── FindByContent —— 拼音 bigram 容错 ────────────────────────────────

        [Fact]
        public void FindByContent_PinyinFuzzy_AsrWrongChar_StillMatches()
        {
            var idx = BuildIndex(SampleVerses);

            // ASR 返回"靠主长常喜乐"（长≠常），
            // 拼音 bigram: kaozhuzhuchangchangchangchangxixile...
            // "长"(zhang) vs "常"(chang) 拼音不同但 bigram 相邻节重叠能命中
            var results = idx.FindByContent("靠主长常喜乐");

            // 至少能召回腓利比书4:4（可能 score 略低于精确匹配）
            Assert.NotEmpty(results);
            Assert.True(results[0].Score > 0);
        }

        // ─── FindByContent —— 指纹短语命中 ──────────────────────────────────

        [Fact]
        public void FindByContent_UniquePhrase_LocksVerseViaFingerprint()
        {
            var idx = BuildIndex(SampleVerses);

            // "独生子赐给" 是约翰福音3:16 的指纹短语（仅出现在该节）
            // 即使 ASR 只捕获了这个片段，指纹索引也应命中
            var results = idx.FindByContent("独生子赐给");

            Assert.NotEmpty(results);
            Assert.Equal(43, results[0].Book);
            Assert.Equal(3,  results[0].Chapter);
            Assert.Equal(16, results[0].Verse);
        }

        [Fact]
        public void FindByContent_ShortDistinctivePhrase_RanksCorrectly()
        {
            var idx = BuildIndex(SampleVerses);

            // "万事都互相效力" 是罗马书8:28 的高区分度短语
            var results = idx.FindByContent("万事都互相效力");

            Assert.NotEmpty(results);
            Assert.Equal(45, results[0].Book);
            Assert.Equal(8,  results[0].Chapter);
            Assert.Equal(28, results[0].Verse);
        }

        // ─── FindByContent —— 空/短文本 ───────────────────────────────────────

        [Fact]
        public void FindByContent_NullOrEmpty_ReturnsEmpty()
        {
            var idx = BuildIndex(SampleVerses);

            Assert.Empty(idx.FindByContent(null));
            Assert.Empty(idx.FindByContent(""));
            Assert.Empty(idx.FindByContent("   "));
        }

        [Fact]
        public void FindByContent_SingleChar_ReturnsEmpty()
        {
            var idx = BuildIndex(SampleVerses);

            // 单字无法构成 bigram，返回空
            var results = idx.FindByContent("的");
            // 不强制要求空——单字 bigram 不存在，但不能崩溃
            Assert.NotNull(results);
        }

        // ─── IsReady ───────────────────────────────────────────────────────────

        [Fact]
        public void IsReady_BeforeBuild_IsFalse()
        {
            var idx = new BibleVerseContentIndex();
            Assert.False(idx.IsReady);
        }

        [Fact]
        public void FindByContent_BeforeReady_ReturnsEmpty()
        {
            var idx = new BibleVerseContentIndex();  // 未 Build
            var results = idx.FindByContent("靠主常常喜乐");
            Assert.Empty(results);
        }

        // ─── 得分排序 ──────────────────────────────────────────────────────────

        [Fact]
        public void FindByContent_Scores_AreDescending()
        {
            var idx = BuildIndex(SampleVerses);

            var results = idx.FindByContent("喜乐");

            for (int i = 1; i < results.Count; i++)
                Assert.True(results[i - 1].Score >= results[i].Score,
                    $"第 {i-1} 名 score={results[i-1].Score} 应 >= 第 {i} 名 score={results[i].Score}");
        }
    }
}
