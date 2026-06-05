using System;

namespace ImageColorChanger.Services.Licensing
{
    public static class FeatureClaimMapper
    {
        public static string ToClaim(PremiumFeature feature)
        {
            return feature switch
            {
                PremiumFeature.SplitImage => "premium.split_image",
                PremiumFeature.Lyrics => "premium.lyrics",
                PremiumFeature.Ndi => "premium.ndi",
                PremiumFeature.AiPanel => "premium.ai_panel",
                PremiumFeature.LiveCaption => "premium.live_caption",
                PremiumFeature.Slides => "premium.slides",
                _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null)
            };
        }

        public static string ToDisplayName(PremiumFeature feature)
        {
            return feature switch
            {
                PremiumFeature.SplitImage => "创建分割图",
                PremiumFeature.Lyrics => "歌词模块",
                PremiumFeature.Ndi => "NDI网络",
                PremiumFeature.AiPanel => "AI面板",
                PremiumFeature.LiveCaption => "实时字幕",
                PremiumFeature.Slides => "幻灯片",
                _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null)
            };
        }
    }
}
