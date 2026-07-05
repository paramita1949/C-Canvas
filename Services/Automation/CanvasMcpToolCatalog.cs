using System.Collections.Generic;

namespace ImageColorChanger.Services.Automation
{
    /// <summary>
    /// CanvasCast 本机控制工具目录。
    /// </summary>
    public static class CanvasMcpToolCatalog
    {
        public static IReadOnlyList<CanvasMcpToolDefinition> GetToolDefinitions()
        {
            return new[]
            {
                Tool("get_app_state", "获取窗口、投影和 MCP 桥基础状态。"),
                Tool("get_text_editor_state", "获取当前幻灯片项目、当前页和文本框列表。"),
                Tool("select_textbox", "按文本框 id 选中一个幻灯片文本框。", new Dictionary<string, object>
                {
                    ["id"] = NumberProperty("文本框 id")
                }, new[] { "id" }),
                Tool("move_textbox", "按文本框 id 移动到指定画布坐标。", new Dictionary<string, object>
                {
                    ["id"] = NumberProperty("文本框 id"),
                    ["x"] = NumberProperty("目标 X 坐标"),
                    ["y"] = NumberProperty("目标 Y 坐标")
                }, new[] { "id", "x", "y" }),
                Tool("enter_textbox_edit_mode", "按文本框 id 进入编辑模式。", new Dictionary<string, object>
                {
                    ["id"] = NumberProperty("文本框 id"),
                    ["selectAll"] = BooleanProperty("是否全选文本")
                }, new[] { "id" }),
                Tool("set_textbox_text", "按文本框 id 设置纯文本内容。", new Dictionary<string, object>
                {
                    ["id"] = NumberProperty("文本框 id"),
                    ["text"] = StringProperty("新的纯文本内容")
                }, new[] { "id", "text" }),
                Tool("save_text_editor_state", "保存当前幻灯片文本编辑器状态。"),
                Tool("get_last_operation_result", "获取最近一次 MCP 工具操作结果。")
            };
        }

        private static CanvasMcpToolDefinition Tool(
            string name,
            string description,
            IDictionary<string, object> properties = null,
            IReadOnlyList<string> required = null)
        {
            return new CanvasMcpToolDefinition
            {
                Name = name,
                Description = description,
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = properties ?? new Dictionary<string, object>(),
                    ["required"] = required ?? new string[0]
                }
            };
        }

        private static Dictionary<string, object> NumberProperty(string description)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "number",
                ["description"] = description
            };
        }

        private static Dictionary<string, object> StringProperty(string description)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = description
            };
        }

        private static Dictionary<string, object> BooleanProperty(string description)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "boolean",
                ["description"] = description
            };
        }
    }

    public sealed class CanvasMcpToolDefinition
    {
        public string Name { get; init; }
        public string Description { get; init; }
        public object InputSchema { get; init; }
    }
}
