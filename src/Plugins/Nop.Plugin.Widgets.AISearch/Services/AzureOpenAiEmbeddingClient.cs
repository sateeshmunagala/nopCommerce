using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Nop.Services.Logging;

namespace Nop.Plugin.Widgets.AISearch.Services;

public class AzureOpenAiEmbeddingClient : IAzureOpenAiEmbeddingClient
{
    private const int EmbeddingDimensions = 1536;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly AISearchSettings _settings;

    public AzureOpenAiEmbeddingClient(AISearchSettings settings, ILogger logger, HttpClient httpClient = null)
    {
        _settings = settings;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await _logger.WarningAsync("AI Search embedding request skipped because input text is empty.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(_settings.AzureOpenAiEndpointUrl))
        {
            await _logger.WarningAsync("AI Search embedding request skipped because AzureOpenAiEndpointUrl is empty.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(_settings.AzureOpenAiApiKey))
        {
            await _logger.WarningAsync("AI Search embedding request skipped because AzureOpenAiApiKey is empty.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(_settings.AzureOpenAiEmbeddingDeploymentName))
        {
            await _logger.WarningAsync("AI Search embedding request skipped because AzureOpenAiEmbeddingDeploymentName is empty.");
            return null;
        }

        try
        {
            var endpoint = _settings.AzureOpenAiEndpointUrl.TrimEnd('/');
            var deployment = Uri.EscapeDataString(_settings.AzureOpenAiEmbeddingDeploymentName.Trim());
            var apiVersion = Uri.EscapeDataString(_settings.AzureOpenAiApiVersion?.Trim() ?? "2024-10-21");
            var uri = $"{endpoint}/openai/deployments/{deployment}/embeddings?api-version={apiVersion}";

            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("api-key", _settings.AzureOpenAiApiKey.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(new { input = text }), Encoding.UTF8, "application/json");

            await _logger.InformationAsync(
                $"Sending AI Search embedding request to deployment '{_settings.AzureOpenAiEmbeddingDeploymentName.Trim()}' with {text.Length} input characters.");
            using var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                await _logger.ErrorAsync($"Azure OpenAI embeddings request failed with HTTP {(int)response.StatusCode}.");
                return null;
            }

            using var document = JsonDocument.Parse(responseBody);
            var embedding = document.RootElement.GetProperty("data")[0].GetProperty("embedding")
                .EnumerateArray().Select(value => value.GetSingle()).ToArray();

            if (embedding.Length != EmbeddingDimensions)
            {
                await _logger.ErrorAsync($"Azure OpenAI returned an embedding with {embedding.Length} dimensions; expected {EmbeddingDimensions}.");
                return null;
            }

            return embedding;
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync("Azure OpenAI embeddings request failed.", exception);
            return null;
        }
    }
}
