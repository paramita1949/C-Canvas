using System;
using System.Globalization;
using System.Text.Json;

namespace ImageColorChanger.Services.Automation
{
    /// <summary>
    /// MCP 工具参数读取器，支持 JSON number 与 string 两类输入。
    /// </summary>
    public static class CanvasMcpArguments
    {
        public static int GetRequiredInt32(JsonElement arguments, string name)
        {
            var property = GetRequiredProperty(arguments, name);
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String &&
                int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }

            throw new ArgumentException($"参数 {name} 必须是整数。");
        }

        public static double GetRequiredDouble(JsonElement arguments, string name)
        {
            var property = GetRequiredProperty(arguments, name);
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String &&
                double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }

            throw new ArgumentException($"参数 {name} 必须是数字。");
        }

        public static string GetRequiredString(JsonElement arguments, string name)
        {
            var property = GetRequiredProperty(arguments, name);
            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }

            throw new ArgumentException($"参数 {name} 必须是字符串。");
        }

        public static string GetOptionalString(JsonElement arguments, string name, string defaultValue = "")
        {
            if (!TryGetProperty(arguments, name, out var property) || property.ValueKind == JsonValueKind.Null)
            {
                return defaultValue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? defaultValue;
            }

            return property.ToString();
        }

        public static bool GetOptionalBoolean(JsonElement arguments, string name, bool defaultValue = false)
        {
            if (!TryGetProperty(arguments, name, out var property) || property.ValueKind == JsonValueKind.Null)
            {
                return defaultValue;
            }

            if (property.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.String &&
                bool.TryParse(property.GetString(), out bool parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        private static JsonElement GetRequiredProperty(JsonElement arguments, string name)
        {
            if (TryGetProperty(arguments, name, out var property))
            {
                return property;
            }

            throw new ArgumentException($"缺少参数 {name}。");
        }

        private static bool TryGetProperty(JsonElement arguments, string name, out JsonElement property)
        {
            property = default;
            return arguments.ValueKind == JsonValueKind.Object &&
                   arguments.TryGetProperty(name, out property);
        }
    }
}
