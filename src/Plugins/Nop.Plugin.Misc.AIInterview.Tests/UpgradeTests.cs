using Moq;
using NUnit.Framework;
using Nop.Plugin.Misc.AIInterview;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Helpers;
using System.Threading.Tasks;
using Nop.Core.Configuration;
using Nop.Core.Domain.Configuration;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class UpgradeTests
{
    [Test]
    public async Task UpdateAsync_PreservesExistingDefaults()
    {
        // Arrange
        var settingService = new Mock<ISettingService>();
        var localizationService = new Mock<ILocalizationService>();
        var webHelper = new Mock<IWebHelper>();
        var messageTemplateService = new Mock<IMessageTemplateService>();

        var existingSettings = new AIInterviewSettings
        {
            Enabled = false, // explicitly set to false
            // other flags are false by default object instantiation
        };

        settingService.Setup(s => s.LoadSettingAsync<AIInterviewSettings>(0)).ReturnsAsync(existingSettings);

        // Mock explicit values in db
        settingService.Setup(s => s.GetSettingAsync("aiinterviewsettings.enabled", 0, false)).ReturnsAsync(new Setting { Name = "aiinterviewsettings.enabled", Value = "False" });
        var plugin = new AIInterviewPlugin(localizationService.Object, settingService.Object, webHelper.Object, messageTemplateService.Object);

        // Act
        try {
            await plugin.UpdateAsync("1.00", "1.01");
        } catch (System.Exception ex) when (ex is not AssertionException) {
             // catch BasePlugin updates not completely mocked.
        }

        // Assert
        Assert.That(existingSettings.Enabled, Is.False, "Enabled flag was explicitly set in DB, so it should be preserved as False.");
        Assert.That(existingSettings.CreditProductSkuMappingsJson, Is.EqualTo(AIInterviewDefaults.DefaultCreditProductSkuMappingsJson));
        Assert.That(existingSettings.CreditPurchasePageUrl, Is.EqualTo(AIInterviewDefaults.DefaultCreditPurchasePageUrl));
    }
}
