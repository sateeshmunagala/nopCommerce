using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Domain;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Data;

public class WhatsAppBlacklistBuilder : NopEntityBuilder<WhatsAppBlacklist>
{
	public override void MapEntity(CreateTableExpressionBuilder table)
	{
		table
			.WithColumn(nameof(WhatsAppBlacklist.CustomerId)).AsInt32().NotNullable()
			.WithColumn(nameof(WhatsAppBlacklist.PhoneNumber)).AsString(50).NotNullable()
			.WithColumn(nameof(WhatsAppBlacklist.FailedAt)).AsDateTime2().NotNullable()
			.WithColumn(nameof(WhatsAppBlacklist.Reason)).AsString(500).Nullable();
	}
}
