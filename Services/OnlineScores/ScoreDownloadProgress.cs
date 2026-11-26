namespace ImageColorChanger.Services.OnlineScores
{
    public enum ScoreDownloadStage { Preparing, ProbingNodes, Downloading, SwitchingNode, Completed }

    // 仅包含安全的界面状态；不传出节点地址、设备凭据或临时票据。
    public sealed record ScoreDownloadProgress(ScoreDownloadStage Stage, string Message);
}
