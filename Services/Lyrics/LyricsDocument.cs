using System;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Services.Lyrics
{
    /// <summary>
    /// 编辑和渲染的文档数据，不携带图片/歌曲所有权。
    /// Content完整保留现有__LYRICS_MODE_V1__序列化内容（含分页、分区、主题、字体等）。
    /// </summary>
    public sealed class LyricsDocument
    {
        public string Content { get; set; } = "";
        public double FontSize { get; set; } = 60;
        public string TextAlign { get; set; } = "Center";
        public int ViewMode { get; set; }
        public string ProjectionWatermarkPath { get; set; } = "";

        public static LyricsDocument FromProject(LyricsProject row) => row == null ? null : new LyricsDocument
        { Content = row.Content, FontSize = row.FontSize, TextAlign = row.TextAlign, ViewMode = row.ViewMode, ProjectionWatermarkPath = row.ProjectionWatermarkPath };

        public static LyricsDocument FromImage(ImageLyrics row) => row == null ? null : new LyricsDocument
        { Content = row.Content, FontSize = row.FontSize, TextAlign = row.TextAlign, ViewMode = row.ViewMode, ProjectionWatermarkPath = row.ProjectionWatermarkPath };

        public void ApplyTo(LyricsProject row)
        { row.Content = Content; row.FontSize = FontSize; row.TextAlign = TextAlign; row.ViewMode = ViewMode; row.ProjectionWatermarkPath = ProjectionWatermarkPath; }

        public void ApplyTo(ImageLyrics row)
        { row.Content = Content; row.FontSize = FontSize; row.TextAlign = TextAlign; row.ViewMode = ViewMode; row.ProjectionWatermarkPath = ProjectionWatermarkPath; }

        public bool SameAs(LyricsDocument other) => other != null && Content == other.Content && FontSize == other.FontSize && TextAlign == other.TextAlign && ViewMode == other.ViewMode && ProjectionWatermarkPath == other.ProjectionWatermarkPath;
    }
}
