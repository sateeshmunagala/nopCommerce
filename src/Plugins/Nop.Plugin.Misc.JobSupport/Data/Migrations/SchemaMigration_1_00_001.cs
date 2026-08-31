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
        Create.TableFor<JobSupportProfile>();
        Create.TableFor<JobSupportProfileSkill>();
        Create.TableFor<JobSupportProfileAttributeDefinition>();
        Create.TableFor<JobSupportProfileAttributeOption>();
        Create.TableFor<JobSupportProfileAttributeValue>();
        Create.TableFor<JobSupportRelationship>();
        Create.TableFor<JobSupportProfileView>();
        Create.TableFor<JobSupportSubscription>();
        Create.TableFor<JobSupportContactReveal>();
        Create.TableFor<JobSupportMigrationCheckpoint>();
    }
}
