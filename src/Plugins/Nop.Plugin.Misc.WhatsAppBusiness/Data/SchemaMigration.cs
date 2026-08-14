using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Domain;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Data;

[NopMigration("2025/01/01 12:00:00", "Misc.WhatsAppBusiness base schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
	public override void Up()
	{
		this.CreateTableIfNotExists<WhatsAppMessageLog>();
		this.CreateTableIfNotExists<WhatsAppBlacklist>();
	}

	public override void Down()
	{
		this.DeleteTableIfExists<WhatsAppMessageLog>();
		this.DeleteTableIfExists<WhatsAppBlacklist>();
	}
}
