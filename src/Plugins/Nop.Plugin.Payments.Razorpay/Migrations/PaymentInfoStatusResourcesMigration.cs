using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Caching;
using Nop.Core.Domain.Localization;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Services.Helpers;

namespace Nop.Plugin.Payments.Razorpay.Migrations;

[NopMigration("2026/08/21 12:00:00", "Payments.Razorpay payment info status resources", MigrationProcessType.NoMatter)]
public class PaymentInfoStatusResourcesMigration : Migration
{
    private static readonly Dictionary<string, string> Resources = new()
    {
        ["Plugins.Payments.Razorpay.PaymentInfo.Success"] = "Payment verified successfully.",
        ["Plugins.Payments.Razorpay.PaymentInfo.PlacingOrder"] = "Payment verified. Placing your order...",
        ["Plugins.Payments.Razorpay.PaymentInfo.SaveDeferred"] = "Payment verified. Checkout is busy; wait, then select Continue to proceed.",
        ["Plugins.Payments.Razorpay.PaymentInfo.ConfirmationUnavailable"] = "Payment verified, but order confirmation is not ready. Use Confirm order when the checkout controls become available.",
        ["Plugins.Payments.Razorpay.PaymentInfo.TermsRequired"] = "Payment verified. Accept the terms and select Confirm order to finish.",
        ["Plugins.Payments.Razorpay.PaymentInfo.CaptchaRequired"] = "Payment verified. Complete the CAPTCHA, then select Confirm order.",
        ["Plugins.Payments.Razorpay.PaymentInfo.SaveFailed"] = "Payment was verified, but checkout could not save the payment details. Select Continue to retry.",
        ["Plugins.Payments.Razorpay.PaymentInfo.ConfirmationFailed"] = "Payment was verified, but order confirmation did not complete. Review any messages and select Confirm order to continue."
    };

    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        var syncCodeHelper = EngineContext.Current.Resolve<ISyncCodeHelper>();
        var resourcesByName = Resources.ToDictionary(resource => resource.Key.ToLowerInvariant(), resource => resource.Value);
        var resourceNames = resourcesByName.Keys.ToArray();
        var existingResources = syncCodeHelper.GetAllEntities<LocaleStringResource>(query =>
            query.Where(resource => resourceNames.Contains(resource.ResourceName)));
        var existing = existingResources
            .Select(resource => (resource.LanguageId, resource.ResourceName))
            .ToHashSet();
        var missingResources = syncCodeHelper.GetAllLanguages(true)
            .SelectMany(language => resourcesByName
                .Where(resource => !existing.Contains((language.Id, resource.Key)))
                .Select(resource => new LocaleStringResource
                {
                    LanguageId = language.Id,
                    ResourceName = resource.Key,
                    ResourceValue = resource.Value
                }))
            .ToList();

        if (missingResources.Count == 0)
            return;

        syncCodeHelper.InsertEntities(missingResources);
        EngineContext.Current.Resolve<IStaticCacheManager>()
            .RemoveByPrefixAsync(NopEntityCacheDefaults<LocaleStringResource>.Prefix).Wait();
    }

    public override void Down()
    {
    }
}
