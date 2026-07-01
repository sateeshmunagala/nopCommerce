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
using Nop.Plugin.Misc.AIInterview.Models;

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
        Assert.That(typeof(AIInterviewSettings).GetProperty("ResumeRequired"), Is.Null);
        Assert.That(typeof(AIInterviewSettings).GetProperty("InterviewRequired"), Is.Null);
        Assert.That(typeof(ConfigurationModel).GetProperty("ResumeRequired"), Is.Null);
        Assert.That(typeof(ConfigurationModel).GetProperty("InterviewRequired"), Is.Null);
        localizationService.Verify(x => x.AddOrUpdateLocaleResourceAsync(It.Is<Dictionary<string, string>>(resources =>
            resources.ContainsKey("Plugins.Misc.AIInterview.Admin.Credits.Activity.Title") &&
            resources.ContainsKey("Plugins.Misc.AIInterview.Admin.Credits.Ledger.Customer"))), Times.AtLeastOnce);
        localizationService.Verify(x => x.AddOrUpdateLocaleResourceAsync(It.Is<Dictionary<string, string>>(resources =>
            resources.ContainsKey("Plugins.Misc.AIInterview.Employer.Applications.Resume") &&
            resources.ContainsKey("Plugins.Misc.AIInterview.Employer.Applications.DownloadResume") &&
            resources.ContainsKey("Plugins.Misc.AIInterview.Employer.Applications.NoResume"))), Times.AtLeastOnce);
    }

    [Test]
    public void InterviewTurnMigration_Guards_Index_Creation()
    {
        var migrationText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Data", "InterviewTurnMigration.cs"));
        var schemaText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Data", "SchemaMigration.cs"));

        Assert.That(migrationText, Does.Contain("Index(\"IX_AIInterview_InterviewTurn_SessionId_SequenceNumber\").Exists()"));
        Assert.That(schemaText, Does.Contain("Index(\"IX_AIInterview_InterviewTurn_SessionId_SequenceNumber\").Exists()"));
    }

    [Test]
    public void InterviewSessionTokenExpiryMigration_Guards_Column_Creation()
    {
        var migrationText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Data", "InterviewSessionTokenExpiryMigration.cs"));

        Assert.That(migrationText, Does.Contain("Schema.Table(TableName).Column(ColumnName).Exists()"));
        Assert.That(migrationText, Does.Contain("nameof(InterviewSession.TokenExpiryUtc)"));
        Assert.That(migrationText, Does.Contain("AsDateTime2().Nullable()"));
    }

    [Test]
    public void CreditPurchaseGrantMigration_Guards_Index_Creation_And_Deletion()
    {
        var migrationText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Data", "CreditPurchaseGrantMigration.cs"));

        Assert.That(migrationText, Does.Contain("Index(\"IX_AIInterview_CreditPurchaseGrant_OrderItemId\").Exists()"));
        Assert.That(migrationText, Does.Contain("Delete.Index(\"IX_AIInterview_CreditPurchaseGrant_OrderItemId\")"));
    }
}
