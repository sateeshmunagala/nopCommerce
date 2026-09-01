using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Migrations;

[NopMigration("2026-01-01 00:00:01", "Misc.JobSupport schema indexes", MigrationProcessType.Installation)]
public class SchemaMigration_1_00_002_Indexes : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.UniqueConstraint("UX_JobSupportProfile_CustomerId")
            .OnTable(nameof(JobSupportProfile))
            .Column(nameof(JobSupportProfile.CustomerId));

        Create.UniqueConstraint("UX_JobSupportProfileSkill_ProfileId_SkillType_Name")
            .OnTable(nameof(JobSupportProfileSkill))
            .Columns(nameof(JobSupportProfileSkill.ProfileId), nameof(JobSupportProfileSkill.SkillType), nameof(JobSupportProfileSkill.Name));

        Create.Index("IX_JobSupportRelationship_Source_Type_Status")
            .OnTable(nameof(JobSupportRelationship))
            .OnColumn(nameof(JobSupportRelationship.SourceCustomerId)).Ascending()
            .OnColumn(nameof(JobSupportRelationship.RelationshipTypeId)).Ascending()
            .OnColumn(nameof(JobSupportRelationship.StatusId)).Ascending();

        Create.Index("IX_JobSupportRelationship_Target_Type_Status")
            .OnTable(nameof(JobSupportRelationship))
            .OnColumn(nameof(JobSupportRelationship.TargetCustomerId)).Ascending()
            .OnColumn(nameof(JobSupportRelationship.RelationshipTypeId)).Ascending()
            .OnColumn(nameof(JobSupportRelationship.StatusId)).Ascending();

        Create.UniqueConstraint("UX_JobSupportProfileView_Viewer_ViewedProfile")
            .OnTable(nameof(JobSupportProfileView))
            .Columns(nameof(JobSupportProfileView.ViewerCustomerId), nameof(JobSupportProfileView.ViewedProfileId));

        Create.UniqueConstraint("UX_JobSupportSubscription_Order_OrderItem")
            .OnTable(nameof(JobSupportSubscription))
            .Columns(nameof(JobSupportSubscription.OrderId), nameof(JobSupportSubscription.OrderItemId));

        Create.Index("IX_JobSupportSubscription_Customer_Status_End")
            .OnTable(nameof(JobSupportSubscription))
            .OnColumn(nameof(JobSupportSubscription.CustomerId)).Ascending()
            .OnColumn(nameof(JobSupportSubscription.Status)).Ascending()
            .OnColumn(nameof(JobSupportSubscription.EndOnUtc)).Ascending();

        Create.UniqueConstraint("UX_JobSupportContactReveal_Viewer_TargetProfile")
            .OnTable(nameof(JobSupportContactReveal))
            .Columns(nameof(JobSupportContactReveal.ViewerCustomerId), nameof(JobSupportContactReveal.TargetProfileId));
    }
}
