using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Vendors;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data.Builders;

public class FixedQuestionSetBuilder : NopEntityBuilder<FixedQuestionSet>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(FixedQuestionSet.VendorId)).AsInt32().NotNullable().ForeignKey<Vendor>()
            .WithColumn(nameof(FixedQuestionSet.Name)).AsString(300).NotNullable();
    }
}

public class FixedQuestionSetItemBuilder : NopEntityBuilder<FixedQuestionSetItem>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(FixedQuestionSetItem.FixedQuestionSetId)).AsInt32().NotNullable().ForeignKey<FixedQuestionSet>()
            .WithColumn(nameof(FixedQuestionSetItem.QuestionText)).AsString(2000).NotNullable()
            .WithColumn(nameof(FixedQuestionSetItem.RubricHint)).AsString(2000).Nullable()
            .WithColumn(nameof(FixedQuestionSetItem.ExpectedSignalNotes)).AsString(2000).Nullable();
    }
}
