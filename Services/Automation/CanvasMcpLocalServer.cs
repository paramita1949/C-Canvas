using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Automation
{
    /// <summary>
    /// 只绑定 127.0.0.1 的轻量 HTTP/MCP 桥，避免额外 NuGet 依赖。
    /// </summary>
    public sealed class CanvasMcpLocalServer : IDisposable
    {
        private const int MaxHeaderBytes = 32 * 1024;
        private readonly Func<CanvasMcpToolCall, CancellationToken, Task<CanvasMcpToolResponse>> _executeToolAsync;
        private readonly CanvasMcpServerOptions _options;
        private readonly CanvasMcpJsonRpcRouter _jsonRpcRouter;
        private TcpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _acceptLoopTask;

        public CanvasMcpLocalServer(
            Func<CanvasMcpToolCall, CancellationToken, Task<CanvasMcpToolResponse>> executeToolAsync,
            CanvasMcpServerOptions options = null)
        {
            _executeToolAsync = executeToolAsync ?? throw new ArgumentNullException(nameof(executeToolAsync));
            _options = options ?? CanvasMcpServerOptions.FromEnvironment();
            _jsonRpcRouter = new CanvasMcpJsonRpcRouter(_executeToolAsync);
        }

        public bool IsRunning { get; private set; }

        public int Port { get; private set; }

        public bool Start()
        {
            if (!_options.Enabled || IsRunning)
            {
                return false;
            }

            _listener = new TcpListener(IPAddress.Loopback, _options.Port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cancellationTokenSource = new CancellationTokenSource();
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cancellationTokenSource.Token));
            IsRunning = true;
            return true;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    client?.Dispose();
                    break;
                }
                catch (ObjectDisposedException)
                {
                    client?.Dispose();
                    break;
                }
                catch
                {
                    client?.Dispose();
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                    var (statusCode, responseJson) = await RouteAsync(request, cancellationToken).ConfigureAwait(false);
                    await WriteResponseAsync(stream, statusCode, responseJson, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    try
                    {
                        using var stream = client.GetStream();
                        string response = JsonSerializer.Serialize(
                            CanvasMcpToolResponse.Failure("http", ex.Message, "http_error"),
                            CanvasMcpJsonRpcRouter.JsonOptions);
                        await WriteResponseAsync(stream, 500, response, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private async Task<(int statusCode, string responseJson)> RouteAsync(
            CanvasMcpHttpRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Method == "GET" && request.Path == "/mcp/health")
            {
                return (200, JsonSerializer.Serialize(
                    CanvasMcpToolResponse.Success("health", "CanvasCast MCP 本机桥运行中", new
                    {
                        port = Port,
                        endpoints = new[] { "/mcp", "/mcp/tools", "/mcp/tools/call", "/mcp/health" }
                    }),
                    CanvasMcpJsonRpcRouter.JsonOptions));
            }

            if (request.Method == "GET" && request.Path == "/mcp/tools")
            {
                return (200, JsonSerializer.Serialize(
                    new { tools = CanvasMcpToolCatalog.GetToolDefinitions() },
                    CanvasMcpJsonRpcRouter.JsonOptions));
            }

            if (request.Method == "POST" && request.Path == "/mcp")
            {
                return (200, await _jsonRpcRouter.HandleAsync(request.Body, cancellationToken).ConfigureAwait(false));
            }

            if (request.Method == "POST" && request.Path == "/mcp/tools/call")
            {
                var call = ParseDirectToolCall(request.Body);
                var response = await _executeToolAsync(call, cancellationToken).ConfigureAwait(false);
                return (200, JsonSerializer.Serialize(response, CanvasMcpJsonRpcRouter.JsonOptions));
            }

            return (404, JsonSerializer.Serialize(
                CanvasMcpToolResponse.Failure("http", $"未找到 MCP 路径: {request.Path}", "not_found"),
                CanvasMcpJsonRpcRouter.JsonOptions));
        }

        private static CanvasMcpToolCall ParseDirectToolCall(string body)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            string tool = root.TryGetProperty("tool", out var toolElement)
                ? toolElement.GetString()
                : root.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : string.Empty;

            if (string.IsNullOrWhiteSpace(tool))
            {
                throw new ArgumentException("缺少 tool/name。");
            }

            JsonElement arguments = root.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement
                : root.TryGetProperty("args", out var argsElement)
                    ? argsElement
                    : default;

            return new CanvasMcpToolCall(tool, arguments);
        }

        private static async Task<CanvasMcpHttpRequest> ReadRequestAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            using var headerBytes = new MemoryStream();
            var buffer = new byte[1];
            while (headerBytes.Length < MaxHeaderBytes)
            {
                int read = await stream.ReadAsync(buffer, 0, 1, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                headerBytes.WriteByte(buffer[0]);
                if (EndsWithHeaderTerminator(headerBytes))
                {
                    break;
                }
            }

            string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            var headerLines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (headerLines.Length == 0 || string.IsNullOrWhiteSpace(headerLines[0]))
            {
                throw new InvalidOperationException("HTTP 请求为空。");
            }

            var requestLine = headerLines[0].Split(' ');
            if (requestLine.Length < 2)
            {
                throw new InvalidOperationException("HTTP 请求行无效。");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < headerLines.Length; i++)
            {
                var line = headerLines[i];
                int colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                {
                    continue;
                }

                headers[line.Substring(0, colonIndex).Trim()] = line.Substring(colonIndex + 1).Trim();
            }

            int contentLength = 0;
            if (headers.TryGetValue("Content-Length", out string contentLengthText))
            {
                int.TryParse(contentLengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength);
            }

            string body = string.Empty;
            if (contentLength > 0)
            {
                byte[] bodyBytes = new byte[contentLength];
                int offset = 0;
                while (offset < contentLength)
                {
                    int read = await stream.ReadAsync(bodyBytes, offset, contentLength - offset, cancellationToken)
                        .ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    offset += read;
                }

                body = Encoding.UTF8.GetString(bodyBytes, 0, offset);
            }

            string path = requestLine[1];
            int queryIndex = path.IndexOf('?');
            if (queryIndex >= 0)
            {
                path = path.Substring(0, queryIndex);
            }

            return new CanvasMcpHttpRequest
            {
                Method = requestLine[0].ToUpperInvariant(),
                Path = path,
                Body = body
            };
        }

        private static bool EndsWithHeaderTerminator(MemoryStream stream)
        {
            if (stream.Length < 4)
            {
                return false;
            }

            var bytes = stream.GetBuffer();
            long length = stream.Length;
            return bytes[length - 4] == '\r' &&
                   bytes[length - 3] == '\n' &&
                   bytes[length - 2] == '\r' &&
                   bytes[length - 1] == '\n';
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            int statusCode,
            string responseJson,
            CancellationToken cancellationToken)
        {
            string reason = statusCode switch
            {
                200 => "OK",
                404 => "Not Found",
                _ => "Internal Server Error"
            };

            byte[] bodyBytes = Encoding.UTF8.GetBytes(responseJson ?? "{}");
            string header =
                $"HTTP/1.1 {statusCode} {reason}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n" +
                "\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _listener?.Stop();
            }
            catch
            {
            }

            try
            {
                _acceptLoopTask?.Wait(TimeSpan.FromMilliseconds(250));
            }
            catch
            {
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _listener = null;
            _acceptLoopTask = null;
            IsRunning = false;
        }

        private sealed class CanvasMcpHttpRequest
        {
            public string Method { get; init; }
            public string Path { get; init; }
            public string Body { get; init; }
        }
    }
}
