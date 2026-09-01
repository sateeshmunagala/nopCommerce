using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Migrations;

[NopMigration("2026-01-01 00:00:00", "Misc.JobSupport base schema", MigrationProcessType.Installation)]
public class SchemaMigration_1_00_001 : ForwardOnlyMigration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<JobSupportProfile>();
        this.CreateTableIfNotExists<JobSupportProfileSkill>();
        this.CreateTableIfNotExists<JobSupportProfileAttributeDefinition>();
        this.CreateTableIfNotExists<JobSupportProfileAttributeOption>();
        this.CreateTableIfNotExists<JobSupportProfileAttributeValue>();
        this.CreateTableIfNotExists<JobSupportRelationship>();
        this.CreateTableIfNotExists<JobSupportProfileView>();
        this.CreateTableIfNotExists<JobSupportSubscription>();
        this.CreateTableIfNotExists<JobSupportContactReveal>();
        this.CreateTableIfNotExists<JobSupportMigrationCheckpoint>();
    }
}
