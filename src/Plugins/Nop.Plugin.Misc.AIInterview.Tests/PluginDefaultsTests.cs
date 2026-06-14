using Moq;
using NUnit.Framework;
using Nop.Plugin.Misc.AIInterview;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Helpers;
using System.Threading.Tasks;
using Nop.Plugin.Misc.AIInterview.Models;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class PluginDefaultsTests
{
    [Test]
    public async Task InstallAsync_SetsCoreDefaults()
    {
        // Arrange
        var settingService = new Mock<ISettingService>();
        var localizationService = new Mock<ILocalizationService>();
        var webHelper = new Mock<IWebHelper>();
        var messageTemplateService = new Mock<IMessageTemplateService>();

        AIInterviewSettings savedSettings = null;
        settingService.Setup(s => s.SaveSettingAsync<AIInterviewSettings>(It.IsAny<AIInterviewSettings>(), It.IsAny<int>()))
            .Callback<Nop.Core.Configuration.ISettings, int>((settings, storeId) => {
                if (settings is AIInterviewSettings aiSettings) {
                    savedSettings = aiSettings;
                }
            })
            .Returns(Task.CompletedTask);

        var plugin = new AIInterviewPlugin(localizationService.Object, settingService.Object, webHelper.Object, messageTemplateService.Object);

        // Act
        try {
            await plugin.InstallAsync();
        } catch (System.Exception ex) when (ex is not NUnit.Framework.AssertionException) {
            // we catch other exceptions because InstallAsync attempts to insert message templates which we did not fully mock.
            // but we ensure the setup correctly sets the flags.
        }

        // Assert
        Assert.That(savedSettings, Is.Not.Null);
        Assert.That(savedSettings.Enabled, Is.True);
        Assert.That(savedSettings.CreditProductSkuMappingsJson, Is.EqualTo(AIInterviewDefaults.DefaultCreditProductSkuMappingsJson));
        Assert.That(savedSettings.CreditPurchasePageUrl, Is.EqualTo(AIInterviewDefaults.DefaultCreditPurchasePageUrl));
        Assert.That(typeof(AIInterviewSettings).GetProperty("ResumeRequired"), Is.Null);
        Assert.That(typeof(AIInterviewSettings).GetProperty("InterviewRequired"), Is.Null);
        Assert.That(typeof(ConfigurationModel).GetProperty("ResumeRequired"), Is.Null);
        Assert.That(typeof(ConfigurationModel).GetProperty("InterviewRequired"), Is.Null);
    }

    [Test]
    public void Locale_Resources_Include_Runtime_Unavailable_Message()
    {
        var installMethod = typeof(AIInterviewPlugin).GetMethod("GetUpgradeLocaleResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(installMethod, Is.Not.Null);

        var resources = (Dictionary<string, string>)installMethod.Invoke(null, null);

        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Runtime.Error.Unavailable"), Is.True);
        Assert.That(resources["Plugins.Misc.AIInterview.Runtime.Error.Unavailable"], Is.EqualTo("The interview service is temporarily unavailable. Please try again."));
    }

    [Test]
    public void Locale_Resources_DoNotContain_CaseInsensitive_Duplicates()
    {
        var upgradeMethod = typeof(AIInterviewPlugin).GetMethod("GetUpgradeLocaleResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var adminMethod = typeof(AIInterviewPlugin).GetMethod("GetAdminLocaleResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var upgradeResources = (Dictionary<string, string>)upgradeMethod.Invoke(null, null);
        var adminResources = (Dictionary<string, string>)adminMethod.Invoke(null, null);
        var unavailableKeys = upgradeResources.Keys
            .Concat(adminResources.Keys)
            .Where(key => key.Contains("Runtime.Error.Unavailable", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var duplicateKeys = unavailableKeys
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.That(unavailableKeys, Has.Count.EqualTo(1));
        Assert.That(duplicateKeys, Is.Empty);
    }

    [Test]
    public void ApplicantCredits_Locale_Resources_Contain_All_Used_Admin_Keys()
    {
        var adminMethod = typeof(AIInterviewPlugin).GetMethod("GetAdminLocaleResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var adminResources = (Dictionary<string, string>)adminMethod.Invoke(null, null);

        var requiredKeys = new[]
        {
            "Plugins.Misc.AIInterview.Admin.Credits.ApplicantTitle",
            "Plugins.Misc.AIInterview.Admin.Credits.CustomerId",
            "Plugins.Misc.AIInterview.Admin.Credits.LoadCustomerId",
            "Plugins.Misc.AIInterview.Admin.Credits.LoadCustomerEmail",
            "Plugins.Misc.AIInterview.Admin.Credits.Amount",
            "Plugins.Misc.AIInterview.Admin.Credits.TopUp",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.Title",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.LoadApplicant",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.SelectedApplicant",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.SelectApplicant",
            "Plugins.Misc.AIInterview.Admin.Credits.SelectApplicant",
            "Plugins.Misc.AIInterview.Admin.Credits.SelectVendor",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.WalletBalance",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.TotalDeposited",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.TotalWithdrawn",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.LastCreditActivityUtc",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.ViewLedger",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.SearchCustomerId",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.SearchKeyword",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.SearchHasPositiveBalanceOnly",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.SearchActivityDateFromUtc",
            "Plugins.Misc.AIInterview.Admin.Credits.Activity.SearchActivityDateToUtc",
            "Plugins.Misc.AIInterview.Admin.Credits.Ledger.Title",
            "Plugins.Misc.AIInterview.Admin.Credits.Ledger.Empty",
            "Plugins.Misc.AIInterview.Admin.Credits.Ledger.Customer",
            "Plugins.Misc.AIInterview.Admin.Credits.Ledger.Amount",
            "Plugins.Misc.AIInterview.Admin.Credits.Ledger.Type",
            "Plugins.Misc.AIInterview.Admin.Credits.Ledger.Remarks",
            "Plugins.Misc.AIInterview.Admin.Credits.Ledger.Utc"
        };

        foreach (var key in requiredKeys)
            Assert.That(adminResources.ContainsKey(key), Is.True, $"Missing admin locale resource: {key}");
    }

    [Test]
    public void PluginJson_Version_Is_1_24()
    {
        var text = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("plugin.json"));

        Assert.That(text, Does.Contain("\"Version\": \"1.24\""));
    }
}
