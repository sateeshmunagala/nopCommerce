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

        var contactRevealTableName = nameof(JobSupportContactReveal);

        if (!Schema.Table(contactRevealTableName).Column(nameof(JobSupportContactReveal.CreatedOnUtc)).Exists())
        {
            Alter.Table(contactRevealTableName)
                .AddColumn(nameof(JobSupportContactReveal.CreatedOnUtc))
                .AsDateTime2()
                .NotNullable()
                .WithDefault(SystemMethods.CurrentUTCDateTime);
        }

        var subscriptionTableName = nameof(JobSupportSubscription);

        if (!Schema.Table(subscriptionTableName).Column(nameof(JobSupportSubscription.LegacyRewardPointsHistoryId)).Exists())
        {
            Alter.Table(subscriptionTableName)
                .AddColumn(nameof(JobSupportSubscription.LegacyRewardPointsHistoryId))
                .AsInt32()
                .Nullable();
        }

        if (!Schema.Table(subscriptionTableName).Column(nameof(JobSupportSubscription.MigrationSource)).Exists())
        {
            Alter.Table(subscriptionTableName)
                .AddColumn(nameof(JobSupportSubscription.MigrationSource))
                .AsString(int.MaxValue)
                .Nullable();
        }

        var attributeDefinitionTableName = nameof(JobSupportProfileAttributeDefinition);

        if (!Schema.Table(attributeDefinitionTableName).Column(nameof(JobSupportProfileAttributeDefinition.SystemName)).Exists())
        {
            Alter.Table(attributeDefinitionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeDefinition.SystemName))
                .AsString(int.MaxValue)
                .Nullable();
        }

        if (!Schema.Table(attributeDefinitionTableName).Column(nameof(JobSupportProfileAttributeDefinition.HelpText)).Exists())
        {
            Alter.Table(attributeDefinitionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeDefinition.HelpText))
                .AsString(int.MaxValue)
                .Nullable();
        }

        if (!Schema.Table(attributeDefinitionTableName).Column(nameof(JobSupportProfileAttributeDefinition.ControlType)).Exists())
        {
            Alter.Table(attributeDefinitionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeDefinition.ControlType))
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(0);
        }

        if (!Schema.Table(attributeDefinitionTableName).Column(nameof(JobSupportProfileAttributeDefinition.ShowOnOnboarding)).Exists())
        {
            Alter.Table(attributeDefinitionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeDefinition.ShowOnOnboarding))
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!Schema.Table(attributeDefinitionTableName).Column(nameof(JobSupportProfileAttributeDefinition.ShowOnProfileEdit)).Exists())
        {
            Alter.Table(attributeDefinitionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeDefinition.ShowOnProfileEdit))
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!Schema.Table(attributeDefinitionTableName).Column(nameof(JobSupportProfileAttributeDefinition.ShowOnPublicProfile)).Exists())
        {
            Alter.Table(attributeDefinitionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeDefinition.ShowOnPublicProfile))
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!Schema.Table(attributeDefinitionTableName).Column(nameof(JobSupportProfileAttributeDefinition.IsActive)).Exists())
        {
            Alter.Table(attributeDefinitionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeDefinition.IsActive))
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(true);
        }

        var attributeOptionTableName = nameof(JobSupportProfileAttributeOption);

        if (!Schema.Table(attributeOptionTableName).Column(nameof(JobSupportProfileAttributeOption.IsActive)).Exists())
        {
            Alter.Table(attributeOptionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeOption.IsActive))
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(true);
        }

        if (!Schema.Table(attributeOptionTableName).Column(nameof(JobSupportProfileAttributeOption.LegacyOptionId)).Exists())
        {
            Alter.Table(attributeOptionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeOption.LegacyOptionId))
                .AsInt32()
                .Nullable();
        }

        if (!Schema.Table(attributeOptionTableName).Column(nameof(JobSupportProfileAttributeOption.CreatedOnUtc)).Exists())
        {
            Alter.Table(attributeOptionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeOption.CreatedOnUtc))
                .AsDateTime2()
                .NotNullable()
                .WithDefault(SystemMethods.CurrentUTCDateTime);
        }

        if (!Schema.Table(attributeOptionTableName).Column(nameof(JobSupportProfileAttributeOption.UpdatedOnUtc)).Exists())
        {
            Alter.Table(attributeOptionTableName)
                .AddColumn(nameof(JobSupportProfileAttributeOption.UpdatedOnUtc))
                .AsDateTime2()
                .NotNullable()
                .WithDefault(SystemMethods.CurrentUTCDateTime);
        }

        var attributeValueTableName = nameof(JobSupportProfileAttributeValue);

        if (!Schema.Table(attributeValueTableName).Column(nameof(JobSupportProfileAttributeValue.DisplayOrder)).Exists())
        {
            Alter.Table(attributeValueTableName)
                .AddColumn(nameof(JobSupportProfileAttributeValue.DisplayOrder))
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(0);
        }
    }
}
