using System.Net.Http.Headers;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WebTools;
using Aiursoft.WeChatExam.Configuration;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Calls the same Ollama embedding API used by the neighbouring RAG applications.</summary>
public class EmbeddingService(
    IGlobalSettingsService settingsService,
    IHttpClientFactory httpClientFactory,
    ILogger<EmbeddingService> logger) : IScopedDependency
{
    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        var endpoint = await settingsService.GetEmbeddingEndpointAsync();
        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        var token = await settingsService.GetEmbeddingTokenAsync();
        var http = httpClientFactory.CreateClient();

        // bge-m3 has an 8192-token context window. Preserve the head and tail,
        // then halve the budget if the model still reports a context error.
        var maxChars = 8000;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/embed?keep_alive=-1");
            request.Content = JsonContent.Create(new { model, input = TruncateForEmbedding(text, maxChars) });
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(timeout.Token);
                if (result?.Embeddings is not { Length: 1 } ||
                    result.Embeddings[0] is not { Length: 1024 } vector ||
                    vector.Any(v => !float.IsFinite(v)) || !vector.Any(v => v != 0))
                    throw new InvalidOperationException("The embedding API did not return a valid 1024-dimension vector.");

                EmbeddingHelper.Normalize(vector);
                return vector;
            }

            var error = await response.Content.ReadAsStringAsync(timeout.Token);
            var isContextError = EmbeddingHelper.IsContextLengthError(response.StatusCode, error);
            if (!isContextError || maxChars <= 500)
                throw new HttpRequestException($"Embedding request failed (HTTP {(int)response.StatusCode}).",
                    null, response.StatusCode);

            maxChars /= 2;
            logger.LogWarning("Embedding input exceeds the context window. Retrying with {MaxChars} characters.", maxChars);
        }
    }

    private static string TruncateForEmbedding(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        var head = (int)(maxChars * 0.75);
        var tail = maxChars - head - 5;
        return string.Concat(text.AsSpan(0, head), "\n...\n", text.AsSpan(text.Length - tail));
    }

    private sealed class OllamaEmbedResponse
    {
        public float[][]? Embeddings { get; set; }
    }
}
