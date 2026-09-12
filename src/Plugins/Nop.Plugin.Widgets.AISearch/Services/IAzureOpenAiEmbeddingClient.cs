namespace Nop.Plugin.Widgets.AISearch.Services;

public interface IAzureOpenAiEmbeddingClient
{
    Task<float[]> GetEmbeddingAsync(string text);
}
