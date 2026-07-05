using System.Text.Json;
using ImageColorChanger.Services.Automation;

namespace Canvas.TextEditor.Tests.Automation;

public class CanvasMcpJsonRpcRouterTests
{
    [Fact]
    public async Task ToolsList_ReturnsCanvasControlTools()
    {
        var router = new CanvasMcpJsonRpcRouter((_, _) =>
            Task.FromResult(CanvasMcpToolResponse.Success("unused", "unused")));

        string responseJson = await router.HandleAsync(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var tools = document.RootElement.GetProperty("result").GetProperty("tools");
        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("get_app_state", toolNames);
        Assert.Contains("get_text_editor_state", toolNames);
        Assert.Contains("select_textbox", toolNames);
        Assert.Contains("move_textbox", toolNames);
        Assert.Contains("enter_textbox_edit_mode", toolNames);
        Assert.Contains("set_textbox_text", toolNames);
        Assert.Contains("save_text_editor_state", toolNames);
        Assert.Contains("get_last_operation_result", toolNames);
    }

    [Fact]
    public async Task ToolsCall_PassesNameAndArgumentsToExecutor()
    {
        CanvasMcpToolCall captured = null;
        var router = new CanvasMcpJsonRpcRouter((call, _) =>
        {
            captured = call;
            return Task.FromResult(CanvasMcpToolResponse.Success(
                call.Tool,
                "移动完成",
                new { id = 12, x = 30.5, y = 40.5 }));
        });

        string responseJson = await router.HandleAsync(
            """
            {
              "jsonrpc": "2.0",
              "id": 2,
              "method": "tools/call",
              "params": {
                "name": "move_textbox",
                "arguments": { "id": 12, "x": 30.5, "y": 40.5 }
              }
            }
            """,
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("move_textbox", captured.Tool);
        Assert.Equal(12, CanvasMcpArguments.GetRequiredInt32(captured.Arguments, "id"));
        Assert.Equal(30.5, CanvasMcpArguments.GetRequiredDouble(captured.Arguments, "x"));
        Assert.Equal(40.5, CanvasMcpArguments.GetRequiredDouble(captured.Arguments, "y"));

        using var document = JsonDocument.Parse(responseJson);
        var contentText = document.RootElement
            .GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        Assert.Contains("\"ok\":true", contentText);
        Assert.Contains("\"tool\":\"move_textbox\"", contentText);
        Assert.Contains("移动完成", contentText);
    }

    [Fact]
    public async Task Initialize_ReturnsMcpServerCapabilities()
    {
        var router = new CanvasMcpJsonRpcRouter((_, _) =>
            Task.FromResult(CanvasMcpToolResponse.Success("unused", "unused")));

        string responseJson = await router.HandleAsync(
            """{"jsonrpc":"2.0","id":"init-1","method":"initialize","params":{}}""",
            CancellationToken.None);

        using var document = JsonDocument.Parse(responseJson);
        var result = document.RootElement.GetProperty("result");

        Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal("CanvasCast Local Control", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }
}
