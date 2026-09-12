using Nop.Core.Domain.Catalog;

namespace Nop.Plugin.Widgets.AISearch.Services;

public interface IProductEmbeddingService
{
    bool LastUpsertWasSkipped { get; }

    Task<bool> UpsertProductEmbeddingAsync(Product product, int storeId);
    Task EnsureVectorIndexAsync();
    Task<IList<int>> SearchAsync(string queryText, int storeId, int topK);
}
