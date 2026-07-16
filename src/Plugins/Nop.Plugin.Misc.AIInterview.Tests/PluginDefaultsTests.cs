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
        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Report.TechnicalScore"), Is.True);
        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Report.Recording"), Is.True);
        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Report.OpenRecording"), Is.True);
    }

    [Test]
    public void Employer_Resume_Locale_Resources_Are_Exposed_For_Install_And_Update()
    {
        var employerMethod = typeof(AIInterviewPlugin).GetMethod("GetEmployerApplicationsLocaleResources", BindingFlags.NonPublic | BindingFlags.Static);
        var resources = (Dictionary<string, string>)employerMethod.Invoke(null, null);
        var pluginText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("AIInterviewPlugin.cs"));

        Assert.That(resources["Plugins.Misc.AIInterview.Employer.Applications.Resume"], Is.EqualTo("Resume"));
        Assert.That(resources["Plugins.Misc.AIInterview.Employer.Applications.DownloadResume"], Is.EqualTo("Download resume"));
        Assert.That(resources["Plugins.Misc.AIInterview.Employer.Applications.NoResume"], Is.EqualTo("No resume"));
        Assert.That(pluginText, Does.Contain("AddOrUpdateLocaleResourceAsync(GetEmployerApplicationsLocaleResources())"));
    }

    [Test]
    public void Locale_Resources_DoNotContain_CaseInsensitive_Duplicates()
    {
        var upgradeMethod = typeof(AIInterviewPlugin).GetMethod("GetUpgradeLocaleResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var adminMethod = typeof(AIInterviewPlugin).GetMethod("GetAdminLocaleResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var adminResources = (Dictionary<string, string>)adminMethod.Invoke(null, null);
        var duplicateKeys = adminResources.Keys
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.That(duplicateKeys, Is.Empty);
        Assert.That(adminResources.Keys.Count(key => key.Equals("Plugins.Misc.AIInterview.Admin.Menu.Root", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
    }

    [Test]
    public void MockPracticeSessions_Route_Constants_And_Mappings_Are_Configured()
    {
        var routeProviderText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Infrastructure", "RouteProvider.cs"));

        Assert.That(AIInterviewDefaults.AdminMockPracticeSessionsRouteName, Is.EqualTo("Plugin.Misc.AIInterview.Admin.MockPracticeSessions"));
        Assert.That(AIInterviewDefaults.AdminMockPracticeSessionsListRouteName, Is.EqualTo("Plugin.Misc.AIInterview.Admin.MockPracticeSessions.List"));
        Assert.That(AIInterviewDefaults.AdminMockPracticeSessionsMenuSystemName, Is.EqualTo("AIInterview.MockPracticeSessions"));
        Assert.That(routeProviderText, Does.Contain("pattern: \"Admin/AIInterviewAdmin/MockPracticeSessions\""));
        Assert.That(routeProviderText, Does.Contain("pattern: \"Admin/AIInterviewAdmin/MockPracticeSessionsList\""));
        Assert.That(routeProviderText, Does.Contain("name: AIInterviewDefaults.AdminMockPracticeSessionsRouteName"));
        Assert.That(routeProviderText, Does.Contain("name: AIInterviewDefaults.AdminMockPracticeSessionsListRouteName"));
    }

    [Test]
    public void MockPracticeSessions_Locale_Resources_Contain_All_Used_Admin_Keys()
    {
        var adminMethod = typeof(AIInterviewPlugin).GetMethod("GetAdminLocaleResources", BindingFlags.NonPublic | BindingFlags.Static);
        var adminResources = (Dictionary<string, string>)adminMethod.Invoke(null, null);

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in new[]
                 {
                     TestFilePathHelper.GetPluginFilePath("Views", "Admin", "MockPracticeSessions.cshtml"),
                     TestFilePathHelper.GetPluginFilePath("Controllers", "AIInterviewAdminController.cs")
                 })
        {
            foreach (System.Text.RegularExpressions.Match match in Regex.Matches(File.ReadAllText(filePath), "\"(Plugins\\.Misc\\.AIInterview\\.Admin\\.MockPracticeSessions\\.[^\"]+)\""))
                usedKeys.Add(match.Groups[1].Value);
        }

        foreach (var property in typeof(MockPracticeSessionSearchModel).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var attribute = property.GetCustomAttribute<NopResourceDisplayNameAttribute>();
            if (attribute?.ResourceKey?.StartsWith("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.", StringComparison.OrdinalIgnoreCase) == true)
                usedKeys.Add(attribute.ResourceKey);
        }

        Assert.That(usedKeys, Is.Not.Empty);

        foreach (var key in usedKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            Assert.That(adminResources.ContainsKey(key), Is.True, $"Missing mock practice locale resource: {key}");
    }

    [Test]
    public void Admin_Time_Labels_No_Longer_Say_Utc_For_Local_Displays()
    {
        var adminMethod = typeof(AIInterviewPlugin).GetMethod("GetAdminLocaleResources", BindingFlags.NonPublic | BindingFlags.Static);
        var adminResources = (Dictionary<string, string>)adminMethod.Invoke(null, null);

        Assert.That(adminResources["Plugins.Misc.AIInterview.Admin.Credits.Activity.LastCreditActivityUtc"], Is.EqualTo("Last Credit Activity"));
        Assert.That(adminResources["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Utc"], Is.EqualTo("Created On"));
        Assert.That(adminResources["Plugins.Misc.AIInterview.Admin.SponsorInvites.ExpiryDateUtc"], Is.EqualTo("Expiry Date"));
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
    public void MockConfigure_Locale_Resources_Contain_All_Used_Admin_Keys()
    {
        var adminMethod = typeof(AIInterviewPlugin).GetMethod("GetAdminLocaleResources", BindingFlags.NonPublic | BindingFlags.Static);
        var adminResources = (Dictionary<string, string>)adminMethod.Invoke(null, null);

        var viewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterviewAdmin", "Configure.cshtml"));
        var usedKeys = Regex.Matches(viewText, "\"(Plugins\\.Misc\\.AIInterview\\.Admin\\.[^\"]+)\"")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.That(usedKeys, Is.Not.Empty, "Mock configure resource usage scan did not find any keys.");

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
        Assert.That(typeof(VendorJobModel).GetProperty("JobLocationOptionId")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.JobLocation"));
        Assert.That(typeof(VendorJobModel).GetProperty("SalaryRange")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.SalaryRange"));
        Assert.That(typeof(VendorJobModel).GetProperty("SalaryMinCtcPa")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.SalaryMinCtcPa"));
        Assert.That(typeof(VendorJobModel).GetProperty("SalaryMaxCtcPa")?.GetCustomAttribute<NopResourceDisplayNameAttribute>()?.ResourceKey,
            Is.EqualTo("Plugins.Misc.AIInterview.VendorJobCreation.SalaryMaxCtcPa"));
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
                     "Plugins.Misc.AIInterview.VendorJobCreation.SalaryMinCtcPa",
                     "Plugins.Misc.AIInterview.VendorJobCreation.SalaryMaxCtcPa",
                     "Plugins.Misc.AIInterview.VendorJobCreation.SalaryMinCtcPa.Invalid",
                     "Plugins.Misc.AIInterview.VendorJobCreation.SalaryMaxCtcPa.Invalid",
                     "Plugins.Misc.AIInterview.VendorJobCreation.SalaryRange.Invalid",
                     "Plugins.Misc.AIInterview.VendorJobCreation.SubmitEdit",
                     "Plugins.Misc.AIInterview.VendorJobCreation.UpdateSuccess",
                     "Plugins.Misc.AIInterview.VendorJobCreation.EditTitle",
                     "Plugins.Misc.AIInterview.VendorJobCreation.Settings",
                     "Plugins.Misc.AIInterview.VendorJobCreation.Select",
                     "Plugins.Misc.AIInterview.VendorJobCreation.Section.RoleOverview",
                     "Plugins.Misc.AIInterview.VendorJobCreation.Section.Requirements",
                     "Plugins.Misc.AIInterview.VendorJobCreation.Section.JobContent",
                     "Plugins.Misc.AIInterview.VendorJobCreation.Section.InterviewSettings",
                     "Plugins.Misc.AIInterview.VendorJobCreation.BackToJobs",
                     "Plugins.Misc.AIInterview.VendorJobCreation.ViewJob",
                     "Plugins.Misc.AIInterview.Employer.Dashboard.Title",
                     "Plugins.Misc.AIInterview.Employer.Dashboard.Tab.Overview",
                     "Plugins.Misc.AIInterview.Employer.Dashboard.Tab.Jobs",
                     "Plugins.Misc.AIInterview.Employer.Dashboard.Tab.Applications",
                     "Plugins.Misc.AIInterview.Employer.Dashboard.Tab.Invites",
                     "Plugins.Misc.AIInterview.Employer.Dashboard.Action.ReviewQueue",
                     "Plugins.Misc.AIInterview.Employer.Dashboard.Action.ViewAnalysis",
                     "Plugins.Misc.AIInterview.Employer.Jobs.Title",
                     "Plugins.Misc.AIInterview.Employer.Jobs.Create",
                     "Plugins.Misc.AIInterview.Employer.Invite.Title",
                     "Plugins.Misc.AIInterview.Employer.Invite.CreateTitle",
                     "Plugins.Misc.AIInterview.Employer.Invite.ActiveTitle",
                     "Plugins.Misc.AIInterview.Employer.Invite.ExpiryDate",
                     "Plugins.Misc.AIInterview.Employer.Invite.Deactivate.Tooltip",
                     "Plugins.Misc.AIInterview.Employer.Applications.ChargeMode.CompanySponsored",
                     "Plugins.Misc.AIInterview.Employer.Applications.ChargeMode.CandidatePaid",
                     "Plugins.Misc.AIInterview.Employer.Applications.PageSize",
                     "Plugins.Misc.AIInterview.Employer.Applications.Reset",
                     "Plugins.Misc.AIInterview.Employer.Applications.All",
                     "Plugins.Misc.AIInterview.Employer.Applications.Phone",
                     "Plugins.Misc.AIInterview.VendorScoreboard.Title",
                     "Plugins.Misc.AIInterview.VendorScoreboard.Eyebrow",
                     "Plugins.Misc.AIInterview.VendorScoreboard.TotalCompletedAssessments",
                     "Plugins.Misc.AIInterview.VendorScoreboard.ShortlistedDetail",
                     "Plugins.Misc.AIInterview.VendorScoreboard.ViewAnalysis"
                 })
        {
            Assert.That(resources.ContainsKey(key), Is.True, $"Missing vendor job locale resource: {key}");
        }
    }

    [Test]
    public void JobCard_Locale_Resources_Contain_All_Required_Keys()
    {
        var method = typeof(AIInterviewPlugin).GetMethod("GetUpgradeLocaleResources", BindingFlags.NonPublic | BindingFlags.Static);
        var resources = (Dictionary<string, string>)method.Invoke(null, null);

        foreach (var key in new[]
                 {
                     "Plugins.Misc.AIInterview.JobCard.Kicker",
                     "Plugins.Misc.AIInterview.JobCard.WorkArrangement",
                     "Plugins.Misc.AIInterview.JobCard.EmploymentType",
                     "Plugins.Misc.AIInterview.JobCard.JobLocation",
                     "Plugins.Misc.AIInterview.JobCard.SalaryRange",
                     "Plugins.Misc.AIInterview.JobCard.ExperienceLevel",
                     "Plugins.Misc.AIInterview.JobCard.Posted",
                     "Plugins.Misc.AIInterview.JobCard.AppliedCount",
                     "Plugins.Misc.AIInterview.JobCard.ViewJob",
                     "Plugins.Misc.AIInterview.JobCard.SaveJob",
                     "Plugins.Misc.AIInterview.JobCard.RemoveSavedJob",
                     "Plugins.Misc.AIInterview.JobCard.SavedToSavedJobs",
                     "Plugins.Misc.AIInterview.JobCard.RemovedFromSavedJobs",
                     "Plugins.Misc.AIInterview.JobCard.JobPreview",
                     "Plugins.Misc.AIInterview.JobCard.CloseJobPreview",
                     "Plugins.Misc.AIInterview.JobCard.LoadingJobDetails",
                     "Plugins.Misc.AIInterview.JobCard.UnableToLoadJobDetails",
                     "Plugins.Misc.AIInterview.JobCard.SavedJobsUnavailable",
                     "Plugins.Misc.AIInterview.JobCard.JobNotFound",
                     "Plugins.Misc.AIInterview.JobCard.InvalidJob"
                 })
        {
            Assert.That(resources.ContainsKey(key), Is.True, $"Missing job card locale resource: {key}");
        }
    }

    [Test]
    public void JobDetails_Locale_Resources_Contain_All_Required_Keys()
    {
        var method = typeof(AIInterviewPlugin).GetMethod("GetUpgradeLocaleResources", BindingFlags.NonPublic | BindingFlags.Static);
        var resources = (Dictionary<string, string>)method.Invoke(null, null);

        foreach (var key in new[]
                 {
                     "Plugins.Misc.AIInterview.JobDetails.Kicker",
                     "Plugins.Misc.AIInterview.JobDetails.HiringCompany",
                     "Plugins.Misc.AIInterview.JobDetails.CandidatesApplied",
                     "Plugins.Misc.AIInterview.JobDetails.ViewJob",
                     "Plugins.Misc.AIInterview.JobDetails.EmailAFriend",
                     "Plugins.Misc.AIInterview.JobDetails.SaveJob",
                     "Plugins.Misc.AIInterview.JobDetails.SavedJob",
                     "Plugins.Misc.AIInterview.JobDetails.SaveToCustomWishlist",
                     "Plugins.Misc.AIInterview.JobDetails.SaveFirstForWishlist",
                     "Plugins.Misc.AIInterview.JobDetails.JobDescription",
                     "Plugins.Misc.AIInterview.JobDetails.RoleHighlights",
                     "Plugins.Misc.AIInterview.JobDetails.RoleHighlightsFallback",
                     "Plugins.Misc.AIInterview.JobDetails.Skills",
                     "Plugins.Misc.AIInterview.JobDetails.SkillsFallback",
                     "Plugins.Misc.AIInterview.JobDetails.JobDetails"
                 })
        {
            Assert.That(resources.ContainsKey(key), Is.True, $"Missing job details locale resource: {key}");
        }

        Assert.That(resources["Plugins.Misc.AIInterview.VendorJobCreation.ShortDescription"], Is.EqualTo("Job Title"));
    }

    [Test]
    public void Public_Vendor_Resources_Are_Seeded_In_Install_And_Upgrade_Paths()
    {
        var pluginText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("AIInterviewPlugin.cs"));

        foreach (var key in new[]
                 {
                     ".JobCard.LoadingJobDetails\"] =",
                     ".JobCard.UnableToLoadJobDetails\"] =",
                     ".VendorScoreboard.ShortlistedDetail\"] =",
                     ".Employer.Invite.CreateTitle\"] =",
                     ".Employer.Invite.ActiveTitle\"] =",
                     ".Employer.Applications.PageSize\"] =",
                     ".Employer.Applications.Reset\"] =",
                     ".Employer.Applications.All\"] =",
                     ".Employer.Applications.Phone\"] =",
                     ".JobDetails.Kicker\"] =",
                     ".JobDetails.HiringCompany\"] =",
                     ".JobDetails.CandidatesApplied\"] =",
                     ".JobDetails.ViewJob\"] =",
                     ".JobDetails.EmailAFriend\"] =",
                     ".JobDetails.SaveJob\"] =",
                     ".JobDetails.SavedJob\"] =",
                     ".JobDetails.SaveToCustomWishlist\"] =",
                     ".JobDetails.SaveFirstForWishlist\"] =",
                     ".JobDetails.JobDescription\"] =",
                     ".JobDetails.RoleHighlights\"] =",
                     ".JobDetails.RoleHighlightsFallback\"] =",
                     ".JobDetails.Skills\"] =",
                     ".JobDetails.SkillsFallback\"] =",
                     ".JobDetails.JobDetails\"] =",
                     ".VendorJobCreation.ShortDescription\"] = \"Job Title\""
                 })
        {
            Assert.That(pluginText.Split(key, StringSplitOptions.None).Length - 1, Is.GreaterThanOrEqualTo(2), $"Expected install and upgrade seeding for: {key}");
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
    public void PluginJson_Version_Is_1_29()
    {
        var text = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("plugin.json"));

        Assert.That(text, Does.Contain("\"Version\": \"1.29\""));
    }

    [Test]
    public void MockPractice_Defaults_And_Project_Content_Are_Configured()
    {
        var projectText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Nop.Plugin.Misc.AIInterview.csproj"));
        var templateViewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "ProductTemplate.MockPractice.cshtml"));
        var pluginText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("AIInterviewPlugin.cs"));

        Assert.That(AIInterviewDefaults.MockPracticeProductTemplateName, Is.EqualTo("AI Interview Mock Practice"));
        Assert.That(AIInterviewDefaults.MockPracticeProductTemplateViewPath, Is.EqualTo("~/Plugins/Misc.AIInterview/Views/ProductTemplate.MockPractice.cshtml"));
        Assert.That(AIInterviewDefaults.InterviewTypeMockPractice, Is.EqualTo("MockPractice"));
        Assert.That(AIInterviewDefaults.InterviewTypeJob, Is.EqualTo("Job"));
        Assert.That(projectText, Does.Contain("<None Remove=\"Views\\ProductTemplate.MockPractice.cshtml\" />"));
        Assert.That(projectText, Does.Contain("<Content Include=\"Views\\ProductTemplate.MockPractice.cshtml\">"));
        Assert.That(templateViewText, Does.Contain("data-practice-start-error=\"true\""));
        Assert.That(templateViewText, Does.Contain("window.location.href = result.runtimeUrl;"));
        Assert.That(templateViewText, Does.Contain("seoSettings.CanonicalUrlsEnabled"));
        Assert.That(pluginText, Does.Contain(".History.MockTitle"));
        Assert.That(pluginText, Does.Contain(".MockPractice.DifficultyRequired"));
        Assert.That(pluginText, Does.Contain(".MockPractice.SkillOrResumeRequired"));
    }
}
