using System;

namespace ImageColorChanger.Services.LiveCaption
{
    internal static class LiveCaptionPlatformLabelFormatter
    {
        public static string BuildRealtimeTag(string provider)
        {
            return $"{ToPlatformDisplayName(provider)}实时";
        }

        public static string BuildShortPhraseTag(string provider)
        {
            return $"{ToPlatformDisplayName(provider)}短语";
        }

        public static string ToPlatformDisplayName(string provider)
        {
            return NormalizeProvider(provider) switch
            {
                "baidu" => "百度",
                "xfyun" => "飞讯语音",
                "doubao" => "豆包",
                "funasr" => "FunASR",
                _ => "飞讯语音"
            };
        }

        public static string NormalizeProvider(string provider)
        {
            string normalized = (provider ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "baidu" => "baidu",
                "xfyun" => "xfyun",
                "doubao" => "doubao",
                "funasr" => "funasr",
                _ => "xfyun"
            };
        }
    }
}
