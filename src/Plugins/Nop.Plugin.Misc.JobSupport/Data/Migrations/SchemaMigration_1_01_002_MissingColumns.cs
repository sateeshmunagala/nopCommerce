using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Migrations;

[NopMigration("2026-02-01 00:00:01", "Misc.JobSupport missing schema columns", MigrationProcessType.Update)]
public class SchemaMigration_1_01_002_MissingColumns : ForwardOnlyMigration
{
    public override void Up()
    {
        var profileTableName = nameof(JobSupportProfile);

        if (!Schema.Table(profileTableName).Column(nameof(JobSupportProfile.Slug)).Exists())
        {
            Alter.Table(profileTableName)
                .AddColumn(nameof(JobSupportProfile.Slug))
                .AsString(int.MaxValue)
                .Nullable();
        }

        if (!Schema.Table(profileTableName).Column(nameof(JobSupportProfile.AvailabilityDays)).Exists())
        {
            Alter.Table(profileTableName)
                .AddColumn(nameof(JobSupportProfile.AvailabilityDays))
                .AsString(int.MaxValue)
                .Nullable();
        }

        if (!Schema.Table(profileTableName).Column(nameof(JobSupportProfile.AvailabilityTimings)).Exists())
        {
            Alter.Table(profileTableName)
                .AddColumn(nameof(JobSupportProfile.AvailabilityTimings))
                .AsString(int.MaxValue)
                .Nullable();
        }

        if (!Schema.Table(profileTableName).Column(nameof(JobSupportProfile.HoursPerWeek)).Exists())
        {
            Alter.Table(profileTableName)
                .AddColumn(nameof(JobSupportProfile.HoursPerWeek))
                .AsString(int.MaxValue)
                .Nullable();
        }

        var profileViewTableName = nameof(JobSupportProfileView);

        if (!Schema.Table(profileViewTableName).Column(nameof(JobSupportProfileView.ContactRevealed)).Exists())
        {
            Alter.Table(profileViewTableName)
                .AddColumn(nameof(JobSupportProfileView.ContactRevealed))
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!Schema.Table(profileViewTableName).Column(nameof(JobSupportProfileView.ContactRevealedOnUtc)).Exists())
        {
            Alter.Table(profileViewTableName)
                .AddColumn(nameof(JobSupportProfileView.ContactRevealedOnUtc))
                .AsDateTime2()
                .Nullable();
        }

        if (!Schema.Table(profileViewTableName).Column(nameof(JobSupportProfileView.CreatedOnUtc)).Exists())
        {
            Alter.Table(profileViewTableName)
                .AddColumn(nameof(JobSupportProfileView.CreatedOnUtc))
                .AsDateTime2()
                .NotNullable()
                .WithDefault(SystemMethods.CurrentUTCDateTime);
        }

        if (!Schema.Table(profileViewTableName).Column(nameof(JobSupportProfileView.UpdatedOnUtc)).Exists())
        {
            Alter.Table(profileViewTableName)
                .AddColumn(nameof(JobSupportProfileView.UpdatedOnUtc))
                .AsDateTime2()
                .NotNullable()
                .WithDefault(SystemMethods.CurrentUTCDateTime);
        }
    }
}
