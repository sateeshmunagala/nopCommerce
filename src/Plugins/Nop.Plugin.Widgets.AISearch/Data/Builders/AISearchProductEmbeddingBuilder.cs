using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Widgets.AISearch.Domain;

namespace Nop.Plugin.Widgets.AISearch.Data.Builders;

public class AISearchProductEmbeddingBuilder : NopEntityBuilder<AISearchProductEmbedding>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(AISearchProductEmbedding.ProductId)).AsInt32().NotNullable()
            .WithColumn(nameof(AISearchProductEmbedding.StoreId)).AsInt32().NotNullable()
            .WithColumn(nameof(AISearchProductEmbedding.ContentHash)).AsString(64).NotNullable()
            .WithColumn(nameof(AISearchProductEmbedding.SourceText)).AsString(int.MaxValue).NotNullable()
            .WithColumn(nameof(AISearchProductEmbedding.Published)).AsBoolean().NotNullable()
            .WithColumn(nameof(AISearchProductEmbedding.UpdatedOnUtc)).AsDateTime2().NotNullable();
    }
}
