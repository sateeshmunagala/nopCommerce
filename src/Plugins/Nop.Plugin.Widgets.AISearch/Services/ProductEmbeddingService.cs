using System.Text.Json;
using LinqToDB.Data;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Plugin.Widgets.AISearch.Domain;
using Nop.Services.Logging;

namespace Nop.Plugin.Widgets.AISearch.Services;

public class ProductEmbeddingService : IProductEmbeddingService
{
    private readonly IAzureOpenAiEmbeddingClient _embeddingClient;
    private readonly INopDataProvider _dataProvider;
    private readonly IProductContentBuilder _productContentBuilder;
    private readonly IRepository<AISearchProductEmbedding> _repository;
    private readonly ILogger _logger;
    private readonly AISearchSettings _settings;

    public bool LastUpsertWasSkipped { get; private set; }

    public ProductEmbeddingService(IAzureOpenAiEmbeddingClient embeddingClient,
        INopDataProvider dataProvider,
        IProductContentBuilder productContentBuilder,
        IRepository<AISearchProductEmbedding> repository,
        ILogger logger,
        AISearchSettings settings)
    {
        _embeddingClient = embeddingClient;
        _dataProvider = dataProvider;
        _productContentBuilder = productContentBuilder;
        _repository = repository;
        _logger = logger;
        _settings = settings;
    }

    public async Task<bool> UpsertProductEmbeddingAsync(Product product, int storeId)
    {
        ArgumentNullException.ThrowIfNull(product);
        LastUpsertWasSkipped = false;

        var content = await _productContentBuilder.BuildContentAsync(product);
        var rows = await _repository.GetAllAsync(query => query.Where(row =>
            row.ProductId == product.Id && row.StoreId == storeId));
        var row = rows.FirstOrDefault();

        if (row != null && string.Equals(row.ContentHash, content.ContentHash, StringComparison.Ordinal) &&
            row.Published == product.Published)
        {
            LastUpsertWasSkipped = true;
            return false;
        }

        var embedding = await _embeddingClient.GetEmbeddingAsync(content.SourceText);
        if (embedding == null)
        {
            await _logger.WarningAsync($"AI Search embedding generation returned null for ProductId {product.Id}, StoreId {storeId}.");
            return false;
        }

        if (row == null)
        {
            row = new AISearchProductEmbedding
            {
                ProductId = product.Id,
                StoreId = storeId,
                ContentHash = content.ContentHash,
                SourceText = content.SourceText,
                Published = product.Published && !product.Deleted,
                UpdatedOnUtc = DateTime.UtcNow
            };
            await _repository.InsertAsync(row);
        }
        else
        {
            row.ContentHash = content.ContentHash;
            row.SourceText = content.SourceText;
            row.Published = product.Published && !product.Deleted;
            row.UpdatedOnUtc = DateTime.UtcNow;
            await _repository.UpdateAsync(row);
        }

        await _dataProvider.ExecuteNonQueryAsync(@"
UPDATE [dbo].[AISearchProductEmbedding]
SET [Embedding] = CAST(@vector AS VECTOR(1536))
WHERE [Id] = @id;",
            new DataParameter("vector", JsonSerializer.Serialize(embedding)),
            new DataParameter("id", row.Id));

        return true;
    }

    public async Task EnsureVectorIndexAsync()
    {
        var indexStatus = await _dataProvider.QueryAsync<ScalarCountRow>(@"
SELECT COUNT(*) AS [Count]
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'[dbo].[AISearchProductEmbedding]')
  AND name = N'IX_AISearchProductEmbedding_Embedding';");

        if (indexStatus.FirstOrDefault()?.Count > 0)
            return;

        var vectorRowCount = await _dataProvider.QueryAsync<ScalarCountRow>(@"
SELECT COUNT(*) AS [Count]
FROM [dbo].[AISearchProductEmbedding]
WHERE [Embedding] IS NOT NULL;");

        if ((vectorRowCount.FirstOrDefault()?.Count ?? 0) < 100)
            return;

        try
        {
            await _dataProvider.ExecuteNonQueryAsync(@"
CREATE VECTOR INDEX [IX_AISearchProductEmbedding_Embedding]
ON [dbo].[AISearchProductEmbedding] ([Embedding])
WITH (METRIC = 'COSINE', TYPE = 'DISKANN');");
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync("Failed to create the AI Search product embedding vector index.", exception);
        }
    }

    public async Task<IList<int>> SearchAsync(string queryText, int storeId, int topK)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            await _logger.InformationAsync($"AI Search query '{queryText}' for StoreId {storeId} found 0 matching product ids.");
            return new List<int>();
        }

        var queryEmbedding = await _embeddingClient.GetEmbeddingAsync(queryText.Trim());
        if (queryEmbedding == null)
        {
            await _logger.InformationAsync($"AI Search query '{queryText}' for StoreId {storeId} found 0 matching product ids.");
            return new List<int>();
        }

        var take = Math.Clamp(topK > 0 ? topK : _settings.TopK, 1, 50);
        var matches = await _dataProvider.QueryAsync<VectorSearchRow>(@"
SELECT TOP (@topK)
    [ProductId],
    VECTOR_DISTANCE('cosine', [Embedding], CAST(@queryVector AS VECTOR(1536))) AS [Distance]
FROM [dbo].[AISearchProductEmbedding]
WHERE [StoreId] = @storeId AND [Published] = 1 AND [Embedding] IS NOT NULL
ORDER BY VECTOR_DISTANCE('cosine', [Embedding], CAST(@queryVector AS VECTOR(1536))) ASC;",
            new DataParameter("topK", take),
            new DataParameter("queryVector", JsonSerializer.Serialize(queryEmbedding)),
            new DataParameter("storeId", storeId));

        var productIds = matches
            .Where(match => 1d - match.Distance >= _settings.SimilarityThreshold)
            .Select(match => match.ProductId)
            .ToList();

        await _logger.InformationAsync($"AI Search query '{queryText}' for StoreId {storeId} found {productIds.Count} matching product ids.");
        return productIds;
    }

    private sealed class VectorSearchRow
    {
        public int ProductId { get; set; }
        public double Distance { get; set; }
    }

    private sealed class ScalarCountRow
    {
        public int Count { get; set; }
    }
}
