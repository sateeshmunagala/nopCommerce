using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/01 12:00:00", "Misc.AIInterview resume profile fields", MigrationProcessType.Update)]
public class ResumeProfileMigration : Migration
{
    private const string TableName = nameof(JobApplication);

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(nameof(JobApplication.ResumeProfileJson)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(JobApplication.ResumeProfileJson))
                .AsString(int.MaxValue)
                .Nullable();
        }

        if (!Schema.Table(TableName).Column(nameof(JobApplication.ResumeProfileGeneratedOnUtc)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(JobApplication.ResumeProfileGeneratedOnUtc))
                .AsDateTime2()
                .Nullable();
        }

        if (!Schema.Table(TableName).Column(nameof(JobApplication.ResumeProfileError)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(JobApplication.ResumeProfileError))
                .AsString(1000)
                .Nullable();
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(nameof(JobApplication.ResumeProfileError)).Exists())
            Delete.Column(nameof(JobApplication.ResumeProfileError)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(JobApplication.ResumeProfileGeneratedOnUtc)).Exists())
            Delete.Column(nameof(JobApplication.ResumeProfileGeneratedOnUtc)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(JobApplication.ResumeProfileJson)).Exists())
            Delete.Column(nameof(JobApplication.ResumeProfileJson)).FromTable(TableName);
    }
}
