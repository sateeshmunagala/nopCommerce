using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Mapping.Builders;

public class JobSupportProfileSkillBuilder : NopEntityBuilder<JobSupportProfileSkill>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportProfileSkill.ProfileId)).AsInt32().NotNullable().ForeignKey<JobSupportProfile>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfileSkill.SkillType)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileSkill.Name)).AsString(400).NotNullable()
            .WithColumn(nameof(JobSupportProfileSkill.LegacySpecificationAttributeId)).AsInt32().Nullable()
            .WithColumn(nameof(JobSupportProfileSkill.LegacySpecificationAttributeOptionId)).AsInt32().Nullable()
            .WithColumn(nameof(JobSupportProfileSkill.DisplayOrder)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileSkill.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportProfileSkill.UpdatedOnUtc)).AsDateTime2().NotNullable();
    }
}
