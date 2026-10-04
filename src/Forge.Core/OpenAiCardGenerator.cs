using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Forge.Core;

public sealed class OpenAiCardGenerator(HttpClient client, ProviderConfig config, Action<ProviderDiagnostics>? diagnostics = null) : IContentGenerator<CardBatch>
{
    public async Task<CardBatch> GenerateAsync(Prompt prompt, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        var body = new Dictionary<string, object>
        {
            ["model"] = config.Model,
            ["messages"] = new[] { new { role = "system", content = prompt.System }, new { role = "user", content = prompt.User } },
            [config.TokenLimitParameter] = config.MaxTokens
        };
        if (config.IncludeTemperature) body["temperature"] = config.Temperature;
        if (config.ReasoningEffort is not null) body["reasoning_effort"] = config.ReasoningEffort;
        if (config.JsonMode) body["response_format"] = new { type = "json_object" };
        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl.TrimEnd('/') + "/chat/completions");
        var key = config.ResolveKey();
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        // Provider error bodies may echo headers or keys: never include them in logs/exceptions.
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"LLM HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > config.MaxResponseBytes) throw new GenerationFailureException("response_byte_limit");
            buffer.Write(chunk, 0, count);
        }
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray());
            var choice = document.RootElement.GetProperty("choices")[0];
            var message = choice.TryGetProperty("message", out var responseMessage) ? responseMessage : default;
            string? reasoning = message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("reasoning_content", out var thinking) && thinking.ValueKind == JsonValueKind.String
                ? thinking.GetString() : null;
            // Only explicit diagnostic fields are persisted; never log headers or an arbitrary response envelope.
            if (reasoning is not null)
                foreach (string secret in new[] { key, config.ApiKey, config.BaseUrl }.Where(s => s.Length > 0).Distinct())
                    reasoning = reasoning.Replace(secret, "[redacted]", StringComparison.Ordinal);
            string? finish = choice.TryGetProperty("finish_reason", out var finished) && finished.ValueKind == JsonValueKind.String
                ? finished.GetString() : null;
            var usage = document.RootElement.TryGetProperty("usage", out var tokenUsage) ? tokenUsage : default;
            var promptDetails = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("prompt_tokens_details", out var details)
                ? details : default;
            diagnostics?.Invoke(new(prompt.Revision, reasoning, finish is "stop" or "length" or "content_filter" ? finish : null,
                ReadTokens(usage, "prompt_tokens"), ReadTokens(usage, "completion_tokens"), ReadTokens(usage, "total_tokens"),
                ReadTokens(usage, "prompt_cache_hit_tokens") ?? ReadTokens(promptDetails, "cached_tokens"),
                ReadTokens(usage, "prompt_cache_miss_tokens")));
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.GetString() is "length" or "content_filter")
                throw new GenerationFailureException(reason.GetString() == "length" ? "completion_token_limit" : "content_filtered");
            string content = choice.GetProperty("message").GetProperty("content").GetString()
                ?? throw new GenerationFailureException("missing_content");
            content = content.Trim();
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = content.IndexOf('\n');
                if (newline < 0 || !content.EndsWith("```", StringComparison.Ordinal)) throw new GenerationFailureException("invalid_json_fence");
                content = content[(newline + 1)..^3].Trim();
            }
            return Wire.Decode<CardBatch>(content);
        }
        catch (JsonException) { throw new GenerationFailureException("invalid_response_json_or_schema"); }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { throw new GenerationFailureException("invalid_response_shape"); }
    }
    private static int? ReadTokens(JsonElement usage, string name) => usage.ValueKind == JsonValueKind.Object
        && usage.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int value) ? value : null;
}
