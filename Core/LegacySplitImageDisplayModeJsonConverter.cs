using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 兼容历史配置：
    /// - false -> FitCenter
    /// - true  -> Fill
    /// - 数字  -> 对应枚举值
    /// - 字符串 -> 枚举名或数字
    /// </summary>
    public sealed class LegacySplitImageDisplayModeJsonConverter : JsonConverter<SplitImageDisplayMode>
    {
        public override SplitImageDisplayMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return SplitImageDisplayMode.Fill;
                case JsonTokenType.False:
                    return SplitImageDisplayMode.FitCenter;
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out int numeric))
                    {
                        return Normalize(numeric);
                    }
                    break;
                case JsonTokenType.String:
                    string raw = reader.GetString();
                    if (int.TryParse(raw, out int parsedNumeric))
                    {
                        return Normalize(parsedNumeric);
                    }

                    if (Enum.TryParse(raw, ignoreCase: true, out SplitImageDisplayMode parsedEnum))
                    {
                        return parsedEnum;
                    }
                    break;
            }

            return SplitImageDisplayMode.FitTop;
        }

        public override void Write(Utf8JsonWriter writer, SplitImageDisplayMode value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue((int)value);
        }

        private static SplitImageDisplayMode Normalize(int value)
        {
            return value switch
            {
                (int)SplitImageDisplayMode.FitCenter => SplitImageDisplayMode.FitCenter,
                (int)SplitImageDisplayMode.Fill => SplitImageDisplayMode.Fill,
                (int)SplitImageDisplayMode.FitTop => SplitImageDisplayMode.FitTop,
                _ => SplitImageDisplayMode.FitTop
            };
        }
    }
}
