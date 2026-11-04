using FluentMigrator;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Web.Framework.Extensions;

namespace Nop.Plugin.Misc.SinglePageCheckout.Data.Migrations;

[NopMigration("2025/11/02 10:00:00:0000000", "Misc.SinglePageCheckout add settings", MigrationProcessType.Installation)]
public class AddSettings : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        this.SetSettingIfNotExists<SinglePageCheckoutSettings, bool>(settings => settings.EnableBuyNow, true);
        this.SetSettingIfNotExists<SinglePageCheckoutSettings, bool>(settings => settings.ShowBuyNowOnProductDetails, true);
        this.SetSettingIfNotExists<SinglePageCheckoutSettings, bool>(settings => settings.ShowBuyNowOnProductBoxes, true);
    }

    public override void Down()
    {
        //nothing
    }
}
