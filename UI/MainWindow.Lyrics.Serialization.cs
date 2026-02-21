using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Lyrics Serialization
    /// </summary>
    public partial class MainWindow
    {
        private bool TryParseSplitLyricsContent(string content, out LyricsSplitContentData splitData)
        {
            splitData = null;
            if (string.IsNullOrWhiteSpace(content) || !content.StartsWith(LyricsSplitContentPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string json = content.Substring(LyricsSplitContentPrefix.Length);
            try
            {
                splitData = JsonSerializer.Deserialize<LyricsSplitContentData>(json);
                if (splitData == null)
                {
                    return false;
                }

                if (splitData.Regions == null || splitData.Regions.Length < 4)
                {
                    splitData.Regions = (splitData.Regions ?? Array.Empty<string>())
                        .Concat(Enumerable.Repeat(string.Empty, 4))
                        .Take(4)
                        .ToArray();
                }

                if (splitData.SplitMode < (int)ViewSplitMode.Single || splitData.SplitMode > (int)ViewSplitMode.Quad)
                {
                    splitData.SplitMode = (int)ViewSplitMode.Single;
                }

                var styles = splitData.RegionStyles ?? Array.Empty<LyricsSplitRegionStyle>();
                if (styles.Length < 4)
                {
                    styles = styles.Concat(Enumerable.Range(0, 4 - styles.Length)
                        .Select(_ => new LyricsSplitRegionStyle())).ToArray();
                }
                splitData.RegionStyles = styles.Take(4)
                    .Select(s => s ?? new LyricsSplitRegionStyle())
                    .ToArray();

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseModeLyricsContent(string content, out LyricsModeContentData modeData)
        {
            modeData = null;
            if (string.IsNullOrWhiteSpace(content) || !content.StartsWith(LyricsModeContentPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string json = content.Substring(LyricsModeContentPrefix.Length);
            try
            {
                modeData = JsonSerializer.Deserialize<LyricsModeContentData>(json);
                if (modeData == null)
                {
                    return false;
                }

                modeData.SingleContent ??= "";
                modeData.SplitContent ??= CreateDefaultSplitPage(ViewSplitMode.Horizontal);

                var split = modeData.SplitContent;
                if (split.Regions == null || split.Regions.Length < 4)
                {
                    split.Regions = (split.Regions ?? Array.Empty<string>())
                        .Concat(Enumerable.Repeat(string.Empty, 4))
                        .Take(4)
                        .ToArray();
                }

                var styles = split.RegionStyles ?? Array.Empty<LyricsSplitRegionStyle>();
                if (styles.Length < 4)
                {
                    styles = styles.Concat(Enumerable.Range(0, 4 - styles.Length)
                        .Select(_ => new LyricsSplitRegionStyle())).ToArray();
                }
                split.RegionStyles = styles.Take(4).Select(s => s ?? new LyricsSplitRegionStyle()).ToArray();

                if (split.SplitMode < (int)ViewSplitMode.Horizontal || split.SplitMode > (int)ViewSplitMode.Quad)
                {
                    split.SplitMode = (int)ViewSplitMode.Horizontal;
                }

                if (modeData.ActiveMode < (int)ViewSplitMode.Single || modeData.ActiveMode > (int)ViewSplitMode.Quad)
                {
                    modeData.ActiveMode = (int)ViewSplitMode.Single;
                }
                modeData.SliceLinesPerPage = Math.Clamp(modeData.SliceLinesPerPage, 1, 3);
                modeData.SliceCurrentIndex = Math.Max(0, modeData.SliceCurrentIndex);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParsePagesLyricsContent(string content, out LyricsPagesContentData pagesData)
        {
            pagesData = null;
            if (string.IsNullOrWhiteSpace(content) || !content.StartsWith(LyricsPagesContentPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string json = content.Substring(LyricsPagesContentPrefix.Length);
            try
            {
                pagesData = JsonSerializer.Deserialize<LyricsPagesContentData>(json);
                if (pagesData == null)
                {
                    return false;
                }

                pagesData.Pages ??= new List<LyricsSplitContentData>();
                if (pagesData.Pages.Count == 0)
                {
                    pagesData.Pages.Add(CreateDefaultSplitPage(ViewSplitMode.Horizontal));
                }

                for (int i = 0; i < pagesData.Pages.Count; i++)
                {
                    var page = pagesData.Pages[i] ?? CreateDefaultSplitPage(ViewSplitMode.Horizontal);
                    pagesData.Pages[i] = page;

                    if (page.Regions == null || page.Regions.Length < 4)
                    {
                        page.Regions = (page.Regions ?? Array.Empty<string>())
                            .Concat(Enumerable.Repeat(string.Empty, 4))
                            .Take(4)
                            .ToArray();
                    }

                    var styles = page.RegionStyles ?? Array.Empty<LyricsSplitRegionStyle>();
                    if (styles.Length < 4)
                    {
                        styles = styles.Concat(Enumerable.Range(0, 4 - styles.Length)
                            .Select(_ => new LyricsSplitRegionStyle())).ToArray();
                    }
                    page.RegionStyles = styles.Take(4).Select(s => s ?? new LyricsSplitRegionStyle()).ToArray();

                    if (page.SplitMode < (int)ViewSplitMode.Horizontal || page.SplitMode > (int)ViewSplitMode.Quad)
                    {
                        page.SplitMode = (int)ViewSplitMode.Horizontal;
                    }
                }

                pagesData.CurrentPageIndex = Math.Clamp(pagesData.CurrentPageIndex, 0, pagesData.Pages.Count - 1);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
