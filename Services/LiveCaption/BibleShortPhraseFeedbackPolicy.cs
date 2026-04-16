using System;

namespace ImageColorChanger.Services.LiveCaption
{
    internal static class BibleShortPhraseFeedbackPolicy
    {
        internal static string BuildStatusMessage(BibleShortPhraseConsumer.Result result)
        {
            if (result == null)
            {
                return "经文识别中：本轮未得到有效结果";
            }

            if (result.Success)
            {
                string recognized = (result.RecognizedText ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(recognized)
                    ? "经文识别成功"
                    : $"经文识别成功：{recognized}";
            }

            string reason = (result.FailureReason ?? string.Empty).Trim().ToLowerInvariant();
            return reason switch
            {
                "audio-too-short" => "经文识别中：语音过短，请稍后再试",
                "empty-transcript" => "经文识别中：暂未识别到清晰语音",
                "unresolved-reference" => "经文识别中：已识别语音，但未匹配到经文",
                "busy" => "经文识别中：正在处理上一段语音",
                "not-running" => "经文识别未运行",
                _ => "经文识别中：本轮未得到有效结果"
            };
        }
    }
}
