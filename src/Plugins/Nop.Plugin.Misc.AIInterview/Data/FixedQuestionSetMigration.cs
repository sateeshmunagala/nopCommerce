using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/08/18 12:00:00", "Misc.AIInterview fixed question sets", MigrationProcessType.Update)]
public class FixedQuestionSetMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<FixedQuestionSet>();
        this.CreateTableIfNotExists<FixedQuestionSetItem>();

        if (!Schema.Table(nameof(FixedQuestionSet)).Index("IX_AIInterview_FixedQuestionSet_VendorId_IsActive").Exists())
        {
            Create.Index("IX_AIInterview_FixedQuestionSet_VendorId_IsActive")
                .OnTable(nameof(FixedQuestionSet))
                .OnColumn(nameof(FixedQuestionSet.VendorId)).Ascending()
                .OnColumn(nameof(FixedQuestionSet.IsActive)).Ascending();
        }

        if (!Schema.Table(nameof(FixedQuestionSetItem)).Index("UX_AIInterview_FixedQuestionSetItem_SetId_Sequence").Exists())
        {
            Create.Index("UX_AIInterview_FixedQuestionSetItem_SetId_Sequence")
                .OnTable(nameof(FixedQuestionSetItem))
                .OnColumn(nameof(FixedQuestionSetItem.FixedQuestionSetId)).Ascending()
                .OnColumn(nameof(FixedQuestionSetItem.SequenceNumber)).Ascending()
                .WithOptions().Unique();
        }
    }

    public override void Down()
    {
        this.DeleteTableIfExists<FixedQuestionSetItem>();
        this.DeleteTableIfExists<FixedQuestionSet>();
    }
}
