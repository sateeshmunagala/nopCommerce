using FluentMigrator.Builders;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Domain;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Data;

public class WhatsAppBlacklistBuilder : NopEntityBuilder<WhatsAppBlacklist>
{
	public override void MapEntity(CreateTableExpressionBuilder table)
	{
		((IColumnOptionSyntax<ICreateTableColumnOptionOrWithColumnSyntax, ICreateTableColumnOptionOrForeignKeyCascadeOrWithColumnSyntax>)(object)((IColumnTypeSyntax<ICreateTableColumnOptionOrWithColumnSyntax>)(object)((ICreateTableWithColumnSyntax)((IColumnOptionSyntax<ICreateTableColumnOptionOrWithColumnSyntax, ICreateTableColumnOptionOrForeignKeyCascadeOrWithColumnSyntax>)(object)((IColumnTypeSyntax<ICreateTableColumnOptionOrWithColumnSyntax>)(object)((ICreateTableWithColumnSyntax)((IColumnOptionSyntax<ICreateTableColumnOptionOrWithColumnSyntax, ICreateTableColumnOptionOrForeignKeyCascadeOrWithColumnSyntax>)(object)((IColumnTypeSyntax<ICreateTableColumnOptionOrWithColumnSyntax>)(object)((ICreateTableWithColumnSyntax)((IColumnOptionSyntax<ICreateTableColumnOptionOrWithColumnSyntax, ICreateTableColumnOptionOrForeignKeyCascadeOrWithColumnSyntax>)(object)((IColumnTypeSyntax<ICreateTableColumnOptionOrWithColumnSyntax>)(object)table.WithColumn("CustomerId")).AsInt32()).NotNullable()).WithColumn("PhoneNumber")).AsString(50)).NotNullable()).WithColumn("FailedAt")).AsDateTime2()).NotNullable()).WithColumn("Reason")).AsString(500)).Nullable();
	}
}
