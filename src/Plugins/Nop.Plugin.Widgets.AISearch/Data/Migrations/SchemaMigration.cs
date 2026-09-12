using FluentMigrator;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Widgets.AISearch.Domain;

namespace Nop.Plugin.Widgets.AISearch.Data.Migrations;

[NopMigration("2026/09/12 10:00:00:0000000", "Nop.Plugin.Widgets.AISearch schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    private readonly INopDataProvider _dataProvider;

    public SchemaMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        this.CreateTableIfNotExists<AISearchProductEmbedding>();

        _dataProvider.ExecuteNonQueryAsync(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[AISearchProductEmbedding]') AND name = N'Embedding')
BEGIN
    ALTER TABLE [dbo].[AISearchProductEmbedding] ADD [Embedding] VECTOR(1536) NULL;
END").GetAwaiter().GetResult();

        _dataProvider.ExecuteNonQueryAsync(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[AISearchProductEmbedding]')
      AND name = N'IX_AISearchProductEmbedding_ProductId_StoreId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AISearchProductEmbedding_ProductId_StoreId]
    ON [dbo].[AISearchProductEmbedding] ([ProductId], [StoreId]);
END").GetAwaiter().GetResult();
    }

    public override void Down()
    {
        this.DeleteTableIfExists<AISearchProductEmbedding>();
    }
}
