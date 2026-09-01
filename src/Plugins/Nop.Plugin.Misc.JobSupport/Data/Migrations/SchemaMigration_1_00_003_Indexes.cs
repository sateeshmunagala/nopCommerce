using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Migrations;

[NopMigration("2026-01-01 00:00:02", "Misc.JobSupport query indexes", MigrationProcessType.Update)]
public class SchemaMigration_1_00_003_Indexes : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Index("IX_JobSupportProfile_Type_Published")
            .OnTable(nameof(JobSupportProfile))
            .OnColumn(nameof(JobSupportProfile.ProfileType)).Ascending()
            .OnColumn(nameof(JobSupportProfile.IsPublished)).Ascending();

        Create.Index("IX_JobSupportProfile_UpdatedOnUtc")
            .OnTable(nameof(JobSupportProfile))
            .OnColumn(nameof(JobSupportProfile.UpdatedOnUtc)).Descending();

        Create.Index("IX_JobSupportProfileSkill_Profile_Type")
            .OnTable(nameof(JobSupportProfileSkill))
            .OnColumn(nameof(JobSupportProfileSkill.ProfileId)).Ascending()
            .OnColumn(nameof(JobSupportProfileSkill.SkillType)).Ascending();

        Create.Index("IX_JobSupportProfileView_ViewedProfile")
            .OnTable(nameof(JobSupportProfileView))
            .OnColumn(nameof(JobSupportProfileView.ViewedProfileId)).Ascending();
    }
}
