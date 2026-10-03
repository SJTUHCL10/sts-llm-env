using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Forge.Core;

public sealed class OpenAiCardGenerator(HttpClient client, ProviderConfig config) : IContentGenerator<CardBatch>
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
        if (config.JsonMode) body["response_format"] = new { type = "json_object" };
        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl.TrimEnd('/') + "/chat/completions");
        var key = config.ResolveKey();
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        // Provider error bodies may echo headers or keys: never include them in logs/exceptions.
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"LLM HTTP {(int)response.StatusCode}.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > config.MaxResponseBytes) throw new FormatException("LLM response exceeds byte limit.");
            buffer.Write(chunk, 0, count);
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        var choice = document.RootElement.GetProperty("choices")[0];
        if (choice.TryGetProperty("finish_reason", out var reason) && reason.GetString() is "length" or "content_filter")
            throw new FormatException("LLM response incomplete or filtered.");
        string content = choice.GetProperty("message").GetProperty("content").GetString()
            ?? throw new FormatException("LLM returned no content.");
        content = content.Trim();
        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            int newline = content.IndexOf('\n');
            if (newline < 0 || !content.EndsWith("```", StringComparison.Ordinal)) throw new FormatException("Invalid fenced JSON.");
            content = content[(newline + 1)..^3].Trim();
        }
        return Wire.Decode<CardBatch>(content);
    }
}
