using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.Automation;
using ImageColorChanger.Services.TextEditor.Application.Models;
using ImageColorChanger.UI.Controls;
using ImageColorChanger.Utils;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfPanel = System.Windows.Controls.Panel;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private CanvasMcpLocalServer _canvasMcpLocalServer;
        private CanvasMcpToolResponse _canvasMcpLastOperationResult;
        private readonly object _canvasMcpOperationLock = new object();

        private void InitializeCanvasMcpBridge()
        {
            try
            {
                if (_canvasMcpLocalServer != null)
                {
                    return;
                }

                _canvasMcpLocalServer = new CanvasMcpLocalServer(ExecuteCanvasMcpToolAsync);
                if (_canvasMcpLocalServer.Start())
                {
                    Closed += (_, _) => ShutdownCanvasMcpBridge();
                    StartupPerfLogger.Mark("MainWindow.CanvasMcp.Started", $"Port={_canvasMcpLocalServer.Port}");
                }
            }
            catch (Exception ex)
            {
                _canvasMcpLocalServer = null;
                StartupPerfLogger.Error("MainWindow.CanvasMcp.StartFailed", ex);
            }
        }

        private void ShutdownCanvasMcpBridge()
        {
            try
            {
                _canvasMcpLocalServer?.Dispose();
            }
            catch
            {
            }
            finally
            {
                _canvasMcpLocalServer = null;
            }
        }

        private async Task<CanvasMcpToolResponse> ExecuteCanvasMcpToolAsync(
            CanvasMcpToolCall call,
            CancellationToken cancellationToken)
        {
            if (Dispatcher.CheckAccess())
            {
                return await ExecuteCanvasMcpToolOnUiThreadAsync(call, cancellationToken);
            }

            var operation = Dispatcher.InvokeAsync(() => ExecuteCanvasMcpToolOnUiThreadAsync(call, cancellationToken));
            return await await operation.Task.ConfigureAwait(false);
        }

        private async Task<CanvasMcpToolResponse> ExecuteCanvasMcpToolOnUiThreadAsync(
            CanvasMcpToolCall call,
            CancellationToken cancellationToken)
        {
            string tool = call?.Tool?.Trim() ?? string.Empty;
            CanvasMcpToolResponse response;

            try
            {
                response = tool switch
                {
                    "get_app_state" => CanvasMcpToolResponse.Success(tool, "已获取软件状态", BuildCanvasMcpAppState()),
                    "get_text_editor_state" => CanvasMcpToolResponse.Success(tool, "已获取幻灯片编辑状态", BuildCanvasMcpTextEditorState()),
                    "select_textbox" => SelectCanvasMcpTextBox(tool, call.Arguments),
                    "move_textbox" => MoveCanvasMcpTextBox(tool, call.Arguments),
                    "enter_textbox_edit_mode" => EnterCanvasMcpTextBoxEditMode(tool, call.Arguments),
                    "set_textbox_text" => SetCanvasMcpTextBoxText(tool, call.Arguments),
                    "save_text_editor_state" => await SaveCanvasMcpTextEditorStateAsync(tool, cancellationToken),
                    "get_last_operation_result" => GetCanvasMcpLastOperationResult(tool),
                    _ => CanvasMcpToolResponse.Failure(tool, $"未知 MCP 工具: {tool}", "unknown_tool")
                };
            }
            catch (Exception ex)
            {
                response = CanvasMcpToolResponse.Failure(tool, ex.Message, ex.GetType().Name);
            }

            if (!string.Equals(tool, "get_last_operation_result", StringComparison.Ordinal))
            {
                SetCanvasMcpLastOperationResult(response);
            }

            return response;
        }

        private object BuildCanvasMcpAppState()
        {
            return new
            {
                windowTitle = Title,
                isActive = IsActive,
                windowState = WindowState.ToString(),
                currentView = _currentViewMode.ToString(),
                projectionActive = _projectionManager?.IsProjectionActive == true,
                projectionLocked = _isProjectionLocked,
                mcp = new
                {
                    running = _canvasMcpLocalServer?.IsRunning == true,
                    port = _canvasMcpLocalServer?.Port ?? 0,
                    baseUrl = _canvasMcpLocalServer?.IsRunning == true
                        ? $"http://127.0.0.1:{_canvasMcpLocalServer.Port}/mcp"
                        : null
                }
            };
        }

        private object BuildCanvasMcpTextEditorState()
        {
            return new
            {
                project = _currentTextProject == null
                    ? null
                    : new
                    {
                        id = _currentTextProject.Id,
                        name = _currentTextProject.Name,
                        canvasWidth = _currentTextProject.CanvasWidth,
                        canvasHeight = _currentTextProject.CanvasHeight
                    },
                slide = _currentSlide == null
                    ? null
                    : new
                    {
                        id = _currentSlide.Id,
                        title = _currentSlide.Title,
                        sortOrder = _currentSlide.SortOrder,
                        splitMode = _currentSlide.SplitMode,
                        outputMode = _currentSlide.OutputMode.ToString()
                    },
                canvas = new
                {
                    actualWidth = EditorCanvas?.ActualWidth ?? 0,
                    actualHeight = EditorCanvas?.ActualHeight ?? 0
                },
                selectedTextBoxIds = GetActiveSelectedTextBoxes()
                    .Where(tb => tb?.Data != null)
                    .Select(tb => tb.Data.Id)
                    .ToArray(),
                textBoxes = _textBoxes
                    .Where(tb => tb?.Data != null)
                    .Select(BuildCanvasMcpTextBoxState)
                    .ToArray()
            };
        }

        private object BuildCanvasMcpTextBoxState(DraggableTextBox textBox)
        {
            var data = textBox.Data;
            return new
            {
                id = data.Id,
                slideId = data.SlideId,
                x = data.X,
                y = data.Y,
                width = data.Width,
                height = data.Height,
                zIndex = data.ZIndex,
                componentType = data.ComponentType ?? string.Empty,
                content = data.Content ?? string.Empty,
                isSelected = textBox.IsSelected,
                isEditing = textBox.IsInEditMode,
                panelZIndex = WpfPanel.GetZIndex(textBox)
            };
        }

        private CanvasMcpToolResponse SelectCanvasMcpTextBox(string tool, JsonElement arguments)
        {
            var textBox = RequireCanvasMcpTextBox(arguments);
            SelectSingleTextBox(textBox);
            textBox.Focus();
            return CanvasMcpToolResponse.Success(tool, "文本框已选中", BuildCanvasMcpTextBoxState(textBox));
        }

        private CanvasMcpToolResponse MoveCanvasMcpTextBox(string tool, JsonElement arguments)
        {
            var textBox = RequireCanvasMcpTextBox(arguments);
            if (IsCanvasMcpNoticeTextBox(textBox))
            {
                return CanvasMcpToolResponse.Failure(tool, "通知组件位置由参数控制，不能直接拖动。", "notice_locked");
            }

            double x = CanvasMcpArguments.GetRequiredDouble(arguments, "x");
            double y = CanvasMcpArguments.GetRequiredDouble(arguments, "y");

            textBox.Data.X = x;
            textBox.Data.Y = y;
            WpfCanvas.SetLeft(textBox, x);
            WpfCanvas.SetTop(textBox, y);
            MarkContentAsModified();
            RefreshCanvasMcpProjectionIfNeeded();

            return CanvasMcpToolResponse.Success(tool, "文本框已移动", BuildCanvasMcpTextBoxState(textBox));
        }

        private CanvasMcpToolResponse EnterCanvasMcpTextBoxEditMode(string tool, JsonElement arguments)
        {
            var textBox = RequireCanvasMcpTextBox(arguments);
            bool selectAll = CanvasMcpArguments.GetOptionalBoolean(arguments, "selectAll", false);
            SelectSingleTextBox(textBox);
            textBox.FocusTextBox(selectAll);

            return CanvasMcpToolResponse.Success(tool, "文本框已进入编辑模式", BuildCanvasMcpTextBoxState(textBox));
        }

        private CanvasMcpToolResponse SetCanvasMcpTextBoxText(string tool, JsonElement arguments)
        {
            var textBox = RequireCanvasMcpTextBox(arguments);
            string text = CanvasMcpArguments.GetRequiredString(arguments, "text");
            textBox.SetPlainTextContentFromAutomation(text);
            MarkContentAsModified();
            RefreshCanvasMcpProjectionIfNeeded();

            return CanvasMcpToolResponse.Success(tool, "文本框文本已更新", BuildCanvasMcpTextBoxState(textBox));
        }

        private async Task<CanvasMcpToolResponse> SaveCanvasMcpTextEditorStateAsync(
            string tool,
            CancellationToken cancellationToken)
        {
            if (_currentSlide == null)
            {
                return CanvasMcpToolResponse.Failure(tool, "当前没有打开幻灯片。", "no_active_slide");
            }

            var result = await SaveTextEditorStateAsync(
                SaveTrigger.Manual,
                _textBoxes.ToList(),
                persistAdditionalState: true,
                saveThumbnail: true,
                cancellationToken: cancellationToken);

            var data = new
            {
                result.Succeeded,
                trigger = result.Trigger.ToString(),
                result.TextElementsSaved,
                result.AdditionalStateSaved,
                result.ThumbnailSaved,
                result.ThumbnailPath,
                error = result.Exception?.Message
            };

            return result.Succeeded
                ? CanvasMcpToolResponse.Success(tool, "幻灯片状态已保存", data)
                : CanvasMcpToolResponse.Failure(tool, result.Exception?.Message ?? "保存失败", "save_failed", data);
        }

        private CanvasMcpToolResponse GetCanvasMcpLastOperationResult(string tool)
        {
            lock (_canvasMcpOperationLock)
            {
                return CanvasMcpToolResponse.Success(
                    tool,
                    _canvasMcpLastOperationResult == null ? "暂无 MCP 操作记录" : "已获取最近一次 MCP 操作结果",
                    _canvasMcpLastOperationResult);
            }
        }

        private void SetCanvasMcpLastOperationResult(CanvasMcpToolResponse response)
        {
            lock (_canvasMcpOperationLock)
            {
                _canvasMcpLastOperationResult = response;
            }
        }

        private DraggableTextBox RequireCanvasMcpTextBox(JsonElement arguments)
        {
            int id = CanvasMcpArguments.GetRequiredInt32(arguments, "id");
            var textBox = _textBoxes.FirstOrDefault(tb => tb?.Data?.Id == id);
            if (textBox == null)
            {
                throw new InvalidOperationException($"未找到文本框 id={id}。");
            }

            return textBox;
        }

        private static bool IsCanvasMcpNoticeTextBox(DraggableTextBox textBox)
        {
            return string.Equals(textBox?.Data?.ComponentType, "Notice", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshCanvasMcpProjectionIfNeeded()
        {
            if (_projectionManager?.IsProjectionActive == true && _currentSlide != null && !_isProjectionLocked)
            {
                UpdateProjectionFromCanvas();
            }
        }
    }
}
