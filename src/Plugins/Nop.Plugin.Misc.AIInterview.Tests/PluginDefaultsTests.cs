using Moq;
using NUnit.Framework;
using Nop.Plugin.Misc.AIInterview;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Helpers;
using System.Threading.Tasks;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Web.Framework.Mvc.ModelBinding;
using System.Reflection;
using System.Text.RegularExpressions;

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
        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Interview.NextQuestion"), Is.True);
        Assert.That(resources["Plugins.Misc.AIInterview.Interview.NextQuestion"], Is.EqualTo("Next question ready."));
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

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in new[]
                 {
                     TestFilePathHelper.GetPluginFilePath("Views", "Admin", "ApplicantCredits.cshtml"),
                     TestFilePathHelper.GetPluginFilePath("Controllers", "AIInterviewAdminController.cs")
                 })
        {
            foreach (System.Text.RegularExpressions.Match match in Regex.Matches(File.ReadAllText(filePath), "\"(Plugins\\.Misc\\.AIInterview\\.Admin\\.Credits\\.[^\"]+)\""))
                usedKeys.Add(match.Groups[1].Value);
        }

        foreach (var modelType in new[] { typeof(CreditManagementModel), typeof(ApplicantCreditActivitySearchModel) })
        {
            foreach (var property in modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var attribute = property.GetCustomAttribute<NopResourceDisplayNameAttribute>();
                if (attribute?.ResourceKey?.StartsWith("Plugins.Misc.AIInterview.Admin.Credits.", StringComparison.OrdinalIgnoreCase) == true)
                    usedKeys.Add(attribute.ResourceKey);
            }
        }

        Assert.That(usedKeys, Is.Not.Empty, "Applicant Credits resource usage scan did not find any keys.");

        foreach (var key in usedKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            Assert.That(adminResources.ContainsKey(key), Is.True, $"Missing admin locale resource: {key}");
    }

    [Test]
    public void ApplicantCredits_Models_Remove_Deprecated_Load_And_Search_Fields()
    {
        Assert.That(typeof(CreditManagementModel).GetProperty("LoadCustomerId"), Is.Null);
        Assert.That(typeof(CreditManagementModel).GetProperty("LoadCustomerEmail"), Is.Null);
        Assert.That(typeof(ApplicantCreditActivitySearchModel).GetProperty("SearchCustomerId"), Is.Null);
        Assert.That(typeof(ApplicantCreditActivitySearchModel).GetProperty("SearchHasPositiveBalanceOnly"), Is.Null);
        Assert.That(typeof(ApplicantCreditActivitySearchModel).GetProperty("SearchActivityDateFromUtc"), Is.Null);
        Assert.That(typeof(ApplicantCreditActivitySearchModel).GetProperty("SearchActivityDateToUtc"), Is.Null);
    }

    [Test]
    public void VendorJobModel_Uses_Localized_Display_Attributes_For_Metadata_Fields()
    {
        Assert.That(typeof(VendorJobModel).GetProperty("ResumeRequired")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.ResumeRequired"));
        Assert.That(typeof(VendorJobModel).GetProperty("InterviewRequired")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.InterviewRequired"));
        Assert.That(typeof(VendorJobModel).GetProperty("ApplyUntilUtc")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.ApplyUntilUtc"));
        Assert.That(typeof(VendorJobModel).GetProperty("ExperienceLevelOptionId")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.ExperienceLevel"));
        Assert.That(typeof(VendorJobModel).GetProperty("WorkModeOptionId")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.WorkMode"));
        Assert.That(typeof(VendorJobModel).GetProperty("EmploymentTypeOptionId")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.EmploymentType"));
        Assert.That(typeof(VendorJobModel).GetProperty("JobLocation")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.JobLocation"));
        Assert.That(typeof(VendorJobModel).GetProperty("SalaryRange")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.SalaryRange"));
    }

    [Test]
    public void VendorJobCreation_Locale_Resources_Contain_New_Metadata_Keys()
    {
        var method = typeof(AIInterviewPlugin).GetMethod("GetUpgradeLocaleResources", BindingFlags.NonPublic | BindingFlags.Static);
        var resources = (Dictionary<string, string>)method.Invoke(null, null);

        foreach (var key in new[]
                 {
                     "Plugins.Misc.AIInterview.VendorJobCreation.ApplyUntilUtc",
                     "Plugins.Misc.AIInterview.VendorJobCreation.ExperienceLevel",
                     "Plugins.Misc.AIInterview.VendorJobCreation.WorkMode",
                     "Plugins.Misc.AIInterview.VendorJobCreation.EmploymentType",
                     "Plugins.Misc.AIInterview.VendorJobCreation.JobLocation",
                     "Plugins.Misc.AIInterview.VendorJobCreation.SalaryRange",
                     "Plugins.Misc.AIInterview.VendorJobCreation.Settings",
                     "Plugins.Misc.AIInterview.VendorJobCreation.Select"
                 })
        {
            Assert.That(resources.ContainsKey(key), Is.True, $"Missing vendor job locale resource: {key}");
        }
    }

    [Test]
    public void Runtime_Localization_Resources_Contain_Directly_Used_NextQuestion_Key()
    {
        var upgradeMethod = typeof(AIInterviewPlugin).GetMethod("GetUpgradeLocaleResources", BindingFlags.NonPublic | BindingFlags.Static);
        var resources = (Dictionary<string, string>)upgradeMethod.Invoke(null, null);
        var runtimeServiceText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Services", "InterviewRuntimeService.cs"));

        Assert.That(runtimeServiceText, Does.Contain("Plugins.Misc.AIInterview.Interview.NextQuestion"));
        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Interview.NextQuestion"), Is.True);
    }

    [Test]
    public void NopWeb_Project_No_Longer_Contains_Admin_Static_Asset_Publish_Blocker()
    {
        var pluginRoot = TestFilePathHelper.GetPluginRootPath();
        var srcRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", ".."));
        var text = File.ReadAllText(Path.Combine(srcRoot, "Presentation", "Nop.Web", "Nop.Web.csproj"));

        Assert.That(text, Does.Not.Contain("RequiredAdminStaticAsset"));
        Assert.That(text, Does.Not.Contain("VerifyRequiredAdminStaticAssets"));
        Assert.That(text, Does.Not.Contain("Missing required admin static asset"));
        Assert.That(text, Does.Not.Contain("npm ci"));
        Assert.That(text, Does.Not.Contain("npx gulp"));
    }

    [Test]
    public void PluginJson_Version_Is_1_28()
    {
        var text = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("plugin.json"));

        Assert.That(text, Does.Contain("\"Version\": \"1.28\""));
    }
}
