using System;
using System.Threading;

namespace ImageColorChanger.Services.OnlineScores
{
    /// <summary>数字简谱的统一入口。下载、编辑预览和三种版式复用同一解释器，不再保留另一套简化语法。</summary>
    public static class ScoreNotationRenderer
    {
        public const int MaxSourceBytes = 200 * 1024;

        public static bool CanRender(string source, out string reason)
            => ExtendedScoreNotationRenderer.CanRender(source, out reason);

        public static void RenderToPng(string source, string outputPath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try { ExtendedScoreNotationRenderer.RenderToPng(source, outputPath, ct); }
            catch (OverflowException)
            {
                throw new ScoreIntegrationException("结构化歌谱时值或尺寸超出安全范围；将尝试原图。");
            }
        }
    }
}
