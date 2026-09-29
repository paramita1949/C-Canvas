using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiModelOption
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public int ContextWindow { get; init; }

        public string DisplayText => string.Equals(Id, DisplayName, StringComparison.OrdinalIgnoreCase)
            ? Id
            : $"{DisplayName}（{Id}）";
    }

    public sealed class AiModelCatalogClient
    {
        private readonly HttpClient _httpClient;

        public AiModelCatalogClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<IReadOnlyList<string>> GetModelsAsync(
            string baseUrl,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var models = await GetModelOptionsAsync(baseUrl, apiKey, cancellationToken).ConfigureAwait(false);
            return models.Select(model => model.Id).ToArray();
        }

        public async Task<IReadOnlyList<AiModelOption>> GetModelOptionsAsync(
            string baseUrl,
            string apiKey,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("请先填写 Base URL。");
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("请先填写 API Key。");
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl.Trim().TrimEnd('/')}/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"获取模型失败：{(int)response.StatusCode} {response.ReasonPhrase}");
            }

            using JsonDocument document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<AiModelOption>();
            }

            return data.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
                .Select(item => new AiModelOption
                {
                    Id = item.GetProperty("id").GetString() ?? string.Empty,
                    DisplayName = item.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String
                        ? name.GetString() ?? item.GetProperty("id").GetString() ?? string.Empty
                        : item.GetProperty("id").GetString() ?? string.Empty,
                    ContextWindow = item.TryGetProperty("context_window", out JsonElement contextWindow) && contextWindow.ValueKind == JsonValueKind.Number
                        ? contextWindow.GetInt32()
                        : 0
                })
                .Where(model => !string.IsNullOrWhiteSpace(model.Id))
                .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
