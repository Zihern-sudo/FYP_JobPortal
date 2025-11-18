using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using JobPortal.Areas.Shared.Options;
using System.Net.Http.Headers;

namespace JobPortal.Areas.Shared.AI
{
    public interface IOpenAIClient
    {
        Task<float[][]> CreateEmbeddingsAsync(IEnumerable<string> inputs, CancellationToken ct = default);
        Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

        // NEW: Strict JSON-mode chat (response_format = json_object)
        Task<string> ChatJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

        // Health
        Task<(bool ok, long ms, string msg, int status, string? reqId)> PingTextAsync(CancellationToken ct = default);
        Task<(bool ok, long ms, string msg, int status, string? reqId)> PingEmbedAsync(CancellationToken ct = default);
    }

    internal sealed class OpenAIClient : IOpenAIClient
    {
        private readonly HttpClient _http;
        private readonly OpenAIOptions _opts;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public OpenAIClient(HttpClient http, IOptions<OpenAIOptions> options)
        {
            _http = http;
            _opts = options.Value;

            _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, _opts.TimeoutSeconds));
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
            _http.BaseAddress = string.IsNullOrWhiteSpace(_opts.BaseUrl)
                ? new Uri("https://api.openai.com/", UriKind.Absolute)
                : new Uri(_opts.BaseUrl!, UriKind.Absolute);
        }

        public async Task<float[][]> CreateEmbeddingsAsync(IEnumerable<string> inputs, CancellationToken ct = default)
        {
            var payload = new { input = inputs.ToArray(), model = _opts.ModelEmbed };
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
            };
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var stream = await res.Content.ReadAsStreamAsync(ct);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var data = doc.RootElement.GetProperty("data");
            var list = new List<float[]>();
            foreach (var item in data.EnumerateArray())
                list.Add(item.GetProperty("embedding").EnumerateArray().Select(x => (float)x.GetDouble()).ToArray());
            return list.ToArray();
        }

        public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            var payload = new
            {
                model = _opts.ModelText,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.0,
                max_tokens = 800
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
            };
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var stream = await res.Content.ReadAsStreamAsync(ct);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }

        // ===== NEW: strict-JSON chat for parsing =====
        public async Task<string> ChatJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            var payload = new
            {
                model = _opts.ModelText, // gpt-4o-mini
                response_format = new { type = "json_object" },
                temperature = 0.2,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
            };

            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            res.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content").GetString();

            return string.IsNullOrWhiteSpace(content) ? "{}" : content!;
        }

        // ===== Health pings =====

        public async Task<(bool ok, long ms, string msg, int status, string? reqId)> PingTextAsync(CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            var payload = new
            {
                model = _opts.ModelText,
                messages = new[] { new { role = "user", content = "ping" } },
                max_tokens = 5
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
            };

            using var res = await _http.SendAsync(req, ct);
            sw.Stop();

            var status = (int)res.StatusCode;
            var reqId = res.Headers.TryGetValues("x-request-id", out var v) ? v.FirstOrDefault() : null;
            var body = await res.Content.ReadAsStringAsync(ct);

            var ok = res.IsSuccessStatusCode;
            var msg = ok ? "Connected" : body;
            return (ok, sw.ElapsedMilliseconds, msg, status, reqId);
        }

        public async Task<(bool ok, long ms, string msg, int status, string? reqId)> PingEmbedAsync(CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            var payload = new
            {
                model = _opts.ModelEmbed,
                input = new[] { "ping" }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
            };

            using var res = await _http.SendAsync(req, ct);
            sw.Stop();

            var status = (int)res.StatusCode;
            var reqId = res.Headers.TryGetValues("x-request-id", out var v) ? v.FirstOrDefault() : null;
            var body = await res.Content.ReadAsStringAsync(ct);

            var ok = res.IsSuccessStatusCode;
            var msg = ok ? "Connected" : body;
            return (ok, sw.ElapsedMilliseconds, msg, status, reqId);
        }
    }
}