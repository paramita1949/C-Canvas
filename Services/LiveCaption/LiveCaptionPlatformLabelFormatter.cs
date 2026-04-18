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
                "tencent" => "腾讯",
                "aliyun" => "阿里",
                "doubao" => "豆包",
                "funasr" => "FunASR",
                _ => "百度"
            };
        }

        public static string NormalizeProvider(string provider)
        {
            string normalized = (provider ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "baidu" => "baidu",
                "tencent" => "tencent",
                "aliyun" => "aliyun",
                "doubao" => "doubao",
                "funasr" => "funasr",
                _ => "baidu"
            };
        }
    }
}
