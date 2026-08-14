using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Domain;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Data;

public class WhatsAppMessageLogBuilder : NopEntityBuilder<WhatsAppMessageLog>
{
	public override void MapEntity(CreateTableExpressionBuilder table)
	{
		table
			.WithColumn(nameof(WhatsAppMessageLog.OrderId)).AsInt32().NotNullable()
			.WithColumn(nameof(WhatsAppMessageLog.CustomerId)).AsInt32().NotNullable()
			.WithColumn(nameof(WhatsAppMessageLog.PhoneNumber)).AsString(50).NotNullable()
			.WithColumn(nameof(WhatsAppMessageLog.MessageType)).AsString(50).NotNullable()
			.WithColumn(nameof(WhatsAppMessageLog.Status)).AsString(20).NotNullable()
			.WithColumn(nameof(WhatsAppMessageLog.SentAt)).AsDateTime2().NotNullable()
			.WithColumn(nameof(WhatsAppMessageLog.Error)).AsString(1000).Nullable()
			.WithColumn(nameof(WhatsAppMessageLog.TrackingNumber)).AsString(100).Nullable()
			.WithColumn(nameof(WhatsAppMessageLog.WhatsAppMessageId)).AsString(200).Nullable()
			.WithColumn(nameof(WhatsAppMessageLog.TemplateUsed)).AsString(100).Nullable();
	}
}
