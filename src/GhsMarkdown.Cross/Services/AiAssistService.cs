using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace GhsMarkdown.Cross.Services;

public class AiAssistService
{
    private const string ApiUrl    = "https://api.anthropic.com/v1/messages";
    private const string Model     = "claude-opus-4-5";
    private const string ApiVersion = "2023-06-01";
    private const int    MaxTokens  = 1024;

    private readonly SettingsService _settingsService;
    private readonly HttpClient      _http;

    public AiAssistService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>Returns true if API key is configured.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settingsService.Load().AnthropicApiKey);

    /// <summary>Test the API key with a minimal request.</summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var result = await SendAsync("Say 'ok'", null, CancellationToken.None)
                .FirstOrDefaultAsync();
            return result is not null;
        }
        catch { return false; }
    }

    /// <summary>
    /// Streams response tokens from the Anthropic API.
    /// Yields text chunks as they arrive.
    /// </summary>
    public async IAsyncEnumerable<string> SendAsync(
        string userPrompt,
        string? documentContext,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var apiKey = _settingsService.Load().AnthropicApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return "[API key not configured. Add your Anthropic API key in Settings.]";
            yield break;
        }

        // Build system prompt
        var systemPrompt = "You are a writing assistant embedded in GHS Markdown Editor. " +
            "Help the user with their Markdown document. " +
            "Respond concisely and in Markdown format when appropriate.";

        // Build user message — include document context if provided
        var userContent = string.IsNullOrEmpty(documentContext)
            ? userPrompt
            : $"Document context:\n\n```markdown\n{documentContext}\n```\n\n{userPrompt}";

        var requestBody = new
        {
            model      = Model,
            max_tokens = MaxTokens,
            stream     = true,
            system     = systemPrompt,
            messages   = new[] { new { role = "user", content = userContent } }
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key",         apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        HttpResponseMessage response;
        string? connectionError = null;
        try
        {
            response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            connectionError = $"[Connection error: {ex.Message}]";
            response = null!;
        }

        if (connectionError is not null)
        {
            yield return connectionError;
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            yield return $"[API error {(int)response.StatusCode}: {err}]";
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader       = new System.IO.StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
        {
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(data); }
            catch { continue; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("type", out var typeEl)) continue;
                if (typeEl.GetString() != "content_block_delta") continue;

                if (!doc.RootElement.TryGetProperty("delta", out var delta)) continue;
                if (!delta.TryGetProperty("text", out var textEl)) continue;

                var chunk = textEl.GetString();
                if (!string.IsNullOrEmpty(chunk))
                    yield return chunk;
            }
        }
    }
}
