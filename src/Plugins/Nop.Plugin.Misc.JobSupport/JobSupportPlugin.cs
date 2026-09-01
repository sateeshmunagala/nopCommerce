using Nop.Core.Domain.Logging;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Data;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Services.Cms;
using Nop.Services.Security;
using Nop.Plugin.Misc.JobSupport.Components;
using Nop.Plugin.Misc.JobSupport.Infrastructure;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport;

public class JobSupportPlugin : BasePlugin, IMiscPlugin, IWidgetPlugin
{
    private readonly ILocalizationService _localizationService;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly IEmailAccountService _emailAccountService;
    private readonly IRepository<ActivityLogType> _activityLogTypeRepository;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly ISettingService _settingService;
    private readonly IPermissionService _permissionService;
    private readonly IWebHelper _webHelper;

    public JobSupportPlugin(ILocalizationService localizationService,
        IMessageTemplateService messageTemplateService,
        IEmailAccountService emailAccountService,
        IRepository<ActivityLogType> activityLogTypeRepository,
        IScheduleTaskService scheduleTaskService,
        ISettingService settingService,
        IPermissionService permissionService,
        IWebHelper webHelper)
    {
        _localizationService = localizationService;
        _messageTemplateService = messageTemplateService;
        _emailAccountService = emailAccountService;
        _activityLogTypeRepository = activityLogTypeRepository;
        _scheduleTaskService = scheduleTaskService;
        _settingService = settingService;
        _permissionService = permissionService;
        _webHelper = webHelper;
    }

    public bool HideInWidgetList => false;

    public Task<IList<string>> GetWidgetZonesAsync()
    {
        var settings = Nop.Core.Infrastructure.EngineContext.Current.Resolve<JobSupportSettings>();
        return Task.FromResult<IList<string>>(new[] { settings.HomepageWidgetZone, settings.SidebarWidgetZone }
            .Where(zone => !string.IsNullOrWhiteSpace(zone)).Distinct().ToList());
    }

    public Type GetWidgetViewComponent(string widgetZone)
    {
        var settings = Nop.Core.Infrastructure.EngineContext.Current.Resolve<JobSupportSettings>();
        return string.Equals(widgetZone, settings.HomepageWidgetZone, StringComparison.OrdinalIgnoreCase)
            ? typeof(JobSupportHomepageProfilesViewComponent)
            : typeof(JobSupportProfileCardViewComponent);
    }

    public override string GetConfigurationPageUrl() =>
        $"{_webHelper.GetStoreLocation()}Admin/JobSupport/Configure";

    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new JobSupportSettings
        {
            Enabled = false,
            UseLegacyStoredProcedures = false,
            LegacyProfileSearchProcedureName = string.Empty,
            LegacyShortlistProcedureName = string.Empty,
            DataReadMode = DataAccessMode.Plugin,
            DataWriteMode = DataAccessMode.Plugin,
            AllowLegacyReadRollback = true,
            GiveSupportRoleSystemName = JobSupportDefaults.GiveSupportRoleSystemName,
            TakeSupportRoleSystemName = JobSupportDefaults.TakeSupportRoleSystemName,
            PaidCustomerRoleSystemName = JobSupportDefaults.PaidCustomerRoleSystemName,
            DefaultPageSize = 12,
            AllowGuestProfileBrowsing = true,
            HomepageWidgetZone = "home_page_before_products",
            SidebarWidgetZone = "left_side_column_after",
            HomepageProfileCount = 4
        });

        await _permissionService.InstallPermissionsAsync(new JobSupportPermissionConfigManager());

        if (!_activityLogTypeRepository.Table.Any(type =>
                type.SystemKeyword == JobSupportDefaults.ActivityTypeSystemName))
        {
            await _activityLogTypeRepository.InsertAsync(new ActivityLogType
            {
                SystemKeyword = JobSupportDefaults.ActivityTypeSystemName,
                Name = JobSupportDefaults.ActivityTypeName,
                Enabled = true
            });
        }

        if (!(await _messageTemplateService.GetMessageTemplatesByNameAsync(
                JobSupportDefaults.CustomerAvailableMessageTemplateSystemName)).Any())
        {
            var emailAccount = (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
            if (emailAccount != null)
            {
                await _messageTemplateService.InsertMessageTemplateAsync(new MessageTemplate
                {
                    Name = JobSupportDefaults.CustomerAvailableMessageTemplateSystemName,
                    Subject = "%Store.Name%. A matching Job Support profile is available",
                    Body = "Hello %JobSupport.CustomerFullName%,<br /><br />" +
                           "<a href=\"%JobSupport.ProfileUrl%\">%JobSupport.ProfileName%</a> is available.<br />" +
                           "%JobSupport.ProfileShortDescription%<br />Skills: %JobSupport.ProfileSkills%<br />" +
                           "Availability: %JobSupport.Availability%",
                    IsActive = true,
                    EmailAccountId = emailAccount.Id
                });
            }
        }

        if (await _scheduleTaskService.GetTaskByTypeAsync(JobSupportDefaults.SynchronizationTaskType) == null)
        {
            await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
            {
                Name = JobSupportDefaults.SynchronizationTaskName,
                Seconds = 3600,
                Type = JobSupportDefaults.SynchronizationTaskType,
                Enabled = false,
                StopOnError = false
            });
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.JobSupport.FriendlyName"] = "Job Support",
            ["Plugins.Misc.JobSupport.Configuration"] = "Job Support configuration",
            ["Plugins.Misc.JobSupport.Fields.Enabled"] = "Enabled",
            ["Plugins.Misc.JobSupport.Disabled"] = "Job Support is currently disabled.",
            ["Plugins.Misc.JobSupport.MessageTemplates.CustomerAvailable.Subject"] = "%Store.Name%. A matching Job Support profile is available",
            ["Plugins.Misc.JobSupport.MessageTemplates.CustomerAvailable.Body"] = "A matching Job Support profile is available.",
            ["Plugins.Misc.JobSupport.Admin.WorkflowDiagnostics.Title"] = "Job Support workflow diagnostics",
            ["Plugins.Misc.JobSupport.Admin.WorkflowDiagnostics.Configuration"] = "Effective configuration",
            ["Plugins.Misc.JobSupport.Admin.WorkflowDiagnostics.Activity"] = "Recent workflow activity",
            ["Plugins.Misc.JobSupport.Admin.WorkflowDiagnostics.CreatedOn"] = "Created on (UTC)",
            ["Plugins.Misc.JobSupport.Admin.WorkflowDiagnostics.ActivityType"] = "Activity type",
            ["Plugins.Misc.JobSupport.Admin.WorkflowDiagnostics.Entity"] = "Entity",
            ["Plugins.Misc.JobSupport.Admin.WorkflowDiagnostics.EntityId"] = "Entity identifier",
            ["Plugins.Misc.JobSupport.Admin.WorkflowDiagnostics.None"] = "No workflow activity was found.",
            ["Plugins.Misc.JobSupport.Relationship.Applied"] = "The relationship action was applied.",
            ["Plugins.Misc.JobSupport.Relationship.Removed"] = "The relationship action was removed.",
            ["Plugins.Misc.JobSupport.Relationship.AlreadyApplied"] = "The relationship action was already applied.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.WorkflowDisabled"] = "The relationship workflow is disabled.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.SourceCustomerNotFound"] = "The source customer was not found.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.ProfileNotFound"] = "The profile was not found.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.ProfileCustomerNotFound"] = "The profile customer was not found.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.SourceProfileNotFound"] = "The source profile was not found.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.SelfRelationship"] = "A profile cannot be related to itself.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.RelationshipBlocked"] = "The relationship action is blocked.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.CompatibilityWriteRejected"] = "The relationship could not be stored.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.CompatibilityMirrorWriteRejected"] = "The mirrored relationship could not be stored.",
            ["Plugins.Misc.JobSupport.Profile.List.Title"] = "Job Support profiles",
            ["Plugins.Misc.JobSupport.Profile.Filters"] = "Profile filters",
            ["Plugins.Misc.JobSupport.Profile.Filter.All"] = "All",
            ["Plugins.Misc.JobSupport.Profile.ApplyFilters"] = "Apply filters",
            ["Plugins.Misc.JobSupport.Profile.Sort"] = "Sort",
            ["Plugins.Misc.JobSupport.Profile.Sort.Default"] = "Default",
            ["Plugins.Misc.JobSupport.Profile.Sort.Recent"] = "Most recent",
            ["Plugins.Misc.JobSupport.Profile.Paging"] = "Profile pages",
            ["Plugins.Misc.JobSupport.Profile.Empty"] = "No matching profiles were found.",
            ["Plugins.Misc.JobSupport.Profile.Error"] = "Profiles could not be loaded.",
            ["Plugins.Misc.JobSupport.Profile.Premium"] = "Premium",
            ["Plugins.Misc.JobSupport.Profile.ProfileType"] = "Profile type",
            ["Plugins.Misc.JobSupport.Profile.PrimaryTechnology"] = "Primary technology",
            ["Plugins.Misc.JobSupport.Profile.SecondaryTechnology"] = "Secondary technology",
            ["Plugins.Misc.JobSupport.Profile.Availability"] = "Availability",
            ["Plugins.Misc.JobSupport.Profile.Experience"] = "Relevant experience",
            ["Plugins.Misc.JobSupport.Profile.Language"] = "Mother tongue",
            ["Plugins.Misc.JobSupport.Profile.Gender"] = "Gender",
            ["Plugins.Misc.JobSupport.Profile.Description"] = "Description",
            ["Plugins.Misc.JobSupport.Profile.Reviews"] = "Reviews",
            ["Plugins.Misc.JobSupport.Profile.View"] = "View profile",
            ["Plugins.Misc.JobSupport.Profile.Shortlist"] = "Shortlist",
            ["Plugins.Misc.JobSupport.Profile.RemoveShortlist"] = "Remove shortlist",
            ["Plugins.Misc.JobSupport.Profile.SendInterest"] = "Send interest",
            ["Plugins.Misc.JobSupport.Profile.InterestSent"] = "Interest sent",
            ["Plugins.Misc.JobSupport.Profile.Block"] = "Block profile",
            ["Plugins.Misc.JobSupport.Profile.RevealContact"] = "Reveal contact",
            ["Plugins.Misc.JobSupport.Profile.LoginToAct"] = "Log in to use profile actions",
            ["Plugins.Misc.JobSupport.Profile.OwnProfile"] = "This is your profile.",
            ["Plugins.Misc.JobSupport.Errors.Request"] = "The request could not be completed.",
            ["Plugins.Misc.JobSupport.Contact.Email"] = "Email",
            ["Plugins.Misc.JobSupport.Contact.Phone"] = "Phone",
            ["Plugins.Misc.JobSupport.Contact.Revealed"] = "Contact details are available.",
            ["Plugins.Misc.JobSupport.Contact.Errors.NotFound"] = "The profile could not be found.",
            ["Plugins.Misc.JobSupport.Contact.Errors.SelfReveal"] = "You cannot reveal your own contact details.",
            ["Plugins.Misc.JobSupport.Contact.Errors.SubscriptionRequired"] = "An active subscription with remaining credits is required.",
            ["Plugins.Misc.JobSupport.Relationship.Errors.InterestNotFound"] = "The incoming interest could not be found.",
            ["Plugins.Misc.JobSupport.Relationship.Accept"] = "Accept",
            ["Plugins.Misc.JobSupport.Relationship.Decline"] = "Decline",
            ["Plugins.Misc.JobSupport.Navigation.Title"] = "Job Support account navigation",
            ["Plugins.Misc.JobSupport.Navigation.Profile"] = "My JobSupport Profile",
            ["Plugins.Misc.JobSupport.Navigation.Shortlisted"] = "Shortlisted Profiles",
            ["Plugins.Misc.JobSupport.Navigation.Relationships"] = "Relationships",
            ["Plugins.Misc.JobSupport.Navigation.Subscription"] = "Subscription",
            ["Plugins.Misc.JobSupport.Navigation.Affiliations"] = "Affiliations",
            ["Plugins.Misc.JobSupport.Account.Profile.Title"] = "My JobSupport Profile",
            ["Plugins.Misc.JobSupport.Account.Profile.Saved"] = "Your JobSupport profile was saved.",
            ["Plugins.Misc.JobSupport.Account.Profile.Fields.ProfileType"] = "Profile type",
            ["Plugins.Misc.JobSupport.Account.Profile.Fields.PrimaryTechnology"] = "Primary technologies",
            ["Plugins.Misc.JobSupport.Account.Profile.Fields.SecondaryTechnology"] = "Secondary technologies",
            ["Plugins.Misc.JobSupport.Account.Profile.Fields.Availability"] = "Availability",
            ["Plugins.Misc.JobSupport.Account.Profile.Fields.Experience"] = "Relevant experience",
            ["Plugins.Misc.JobSupport.Account.Profile.Fields.Language"] = "Mother tongue",
            ["Plugins.Misc.JobSupport.Account.Profile.Fields.ShortDescription"] = "Short description",
            ["Plugins.Misc.JobSupport.Account.Profile.Fields.Description"] = "Description",
            ["Plugins.Misc.JobSupport.Account.Relationships.Title"] = "JobSupport relationships",
            ["Plugins.Misc.JobSupport.Account.Relationships.Empty"] = "No relationships were found.",
            ["Plugins.Misc.JobSupport.Account.Affiliations.Title"] = "Affiliations",
            ["Plugins.Misc.JobSupport.Account.Affiliations.Link"] = "Affiliate link",
            ["Plugins.Misc.JobSupport.Account.Affiliations.Customer"] = "Customer",
            ["Plugins.Misc.JobSupport.Account.Affiliations.CreatedOn"] = "Created on",
            ["Plugins.Misc.JobSupport.Account.Affiliations.Empty"] = "No affiliated customers were found.",
            ["Plugins.Misc.JobSupport.Homepage.Title"] = "Job Support profiles",
            ["Plugins.Misc.JobSupport.Homepage.ViewAll"] = "View all profiles",
            ["Plugins.Misc.JobSupport.Subscription.Title"] = "JobSupport subscription",
            ["Plugins.Misc.JobSupport.Subscription.Status"] = "Status",
            ["Plugins.Misc.JobSupport.Subscription.StartDate"] = "Start date",
            ["Plugins.Misc.JobSupport.Subscription.ExpiryDate"] = "Expiry date",
            ["Plugins.Misc.JobSupport.Subscription.AllottedCredits"] = "Allotted credits",
            ["Plugins.Misc.JobSupport.Subscription.UsedCredits"] = "Used credits",
            ["Plugins.Misc.JobSupport.Subscription.RemainingCredits"] = "Remaining credits",
            ["Plugins.Misc.JobSupport.Subscription.Plans"] = "Subscription plans",
            ["Plugins.Misc.JobSupport.Subscription.NoPlans"] = "No subscription plans are configured.",
            ["Plugins.Misc.JobSupport.Subscription.Status.Inactive"] = "Inactive",
            ["Plugins.Misc.JobSupport.Subscription.Status.Active"] = "Active",
            ["Plugins.Misc.JobSupport.Subscription.Status.Expired"] = "Expired",
            ["Plugins.Misc.JobSupport.Subscription.Status.Exhausted"] = "Credits exhausted",
            ["Plugins.Misc.JobSupport.Subscription.Status.Cancelled"] = "Cancelled",
            ["Plugins.Misc.JobSupport.Admin.Configuration.Title"] = "JobSupport configuration",
            ["Plugins.Misc.JobSupport.Admin.Configuration.Saved"] = "JobSupport configuration was saved.",
            ["Plugins.Misc.JobSupport.Admin.Profiles.Title"] = "JobSupport profiles",
            ["Plugins.Misc.JobSupport.Admin.Profiles.Search.Customer"] = "Customer",
            ["Plugins.Misc.JobSupport.Admin.Profiles.Search.ProfileType"] = "Profile type identifier",
            ["Plugins.Misc.JobSupport.Admin.Profiles.Customer"] = "Customer",
            ["Plugins.Misc.JobSupport.Admin.Profiles.Product"] = "Profile product",
            ["Plugins.Misc.JobSupport.Admin.Profiles.ProfileType"] = "Profile type",
            ["Plugins.Misc.JobSupport.Admin.Profiles.PrimaryTechnology"] = "Primary technology",
            ["Plugins.Misc.JobSupport.Admin.Profiles.Availability"] = "Availability",
            ["Plugins.Misc.JobSupport.Admin.Profiles.Premium"] = "Premium",
            ["Plugins.Misc.JobSupport.Admin.Profiles.CreatedOn"] = "Created on",
            ["Plugins.Misc.JobSupport.Admin.Profiles.Published"] = "Published",
            ["Plugins.Misc.JobSupport.Admin.Profiles.CustomerEdit"] = "Customer",
            ["Plugins.Misc.JobSupport.Admin.Profiles.ProductEdit"] = "Product",
            ["Plugins.Misc.JobSupport.Admin.Relationships.Title"] = "JobSupport relationships",
            ["Plugins.Misc.JobSupport.Admin.Relationships.Description"] = "Relationship administration uses the plugin relationship services.",
            ["Plugins.Misc.JobSupport.Admin.Subscriptions.Title"] = "JobSupport subscriptions",
            ["Plugins.Misc.JobSupport.Admin.Subscriptions.Description"] = "Subscription administration uses the plugin subscription services.",
            ["Plugins.Misc.JobSupport.Relationship.Types.ShortlistedByMe"] = "Shortlisted by me",
            ["Plugins.Misc.JobSupport.Relationship.Types.ShortlistedMe"] = "Shortlisted me",
            ["Plugins.Misc.JobSupport.Relationship.Types.InterestSent"] = "Interest sent",
            ["Plugins.Misc.JobSupport.Relationship.Types.InterestReceived"] = "Interest received",
            ["Plugins.Misc.JobSupport.Relationship.Types.AcceptedByMe"] = "Accepted by me",
            ["Plugins.Misc.JobSupport.Relationship.Types.AcceptedMe"] = "Accepted me",
            ["Plugins.Misc.JobSupport.Relationship.Types.DeclinedByMe"] = "Declined by me",
            ["Plugins.Misc.JobSupport.Relationship.Types.DeclinedMe"] = "Declined me",
            ["Plugins.Misc.JobSupport.Relationship.Types.BlockedByMe"] = "Blocked by me",
            ["Plugins.Misc.JobSupport.Relationship.Types.BlockedMe"] = "Blocked me",
            ["Plugins.Misc.JobSupport.Relationship.Types.ViewedByMe"] = "Viewed by me",
            ["Plugins.Misc.JobSupport.Relationship.Types.ViewedMe"] = "Viewed me",
            ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.Enabled"] = "Enabled",
            ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.UseLegacyStoredProcedures"] = "Use legacy stored procedures"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.LegacyProfileSearchProcedureName"] = "Legacy profile search procedure"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.LegacyShortlistProcedureName"] = "Legacy relationship procedure"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.GiveSupportRoleSystemName"] = "Give support role system name"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.TakeSupportRoleSystemName"] = "Take support role system name"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.PaidCustomerRoleSystemName"] = "Paid customer role system name"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.ProfileTypeSpecificationAttributeId"] = "Profile type specification attribute"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.CurrentAvailabilitySpecificationAttributeId"] = "Availability specification attribute"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.RelevantExperienceSpecificationAttributeId"] = "Relevant experience specification attribute"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.MotherTongueSpecificationAttributeId"] = "Mother tongue specification attribute"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.PrimaryTechnologySpecificationAttributeId"] = "Primary technology specification attribute"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.SecondaryTechnologySpecificationAttributeId"] = "Secondary technology specification attribute"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.ShortDescriptionCustomerAttributeId"] = "Short description customer attribute"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.FullDescriptionCustomerAttributeId"] = "Description customer attribute"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.ThreeMonthSubscriptionProductId"] = "Three month subscription product"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.SixMonthSubscriptionProductId"] = "Six month subscription product"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.OneYearSubscriptionProductId"] = "One year subscription product"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.ThreeMonthSubscriptionAllottedCount"] = "Three month subscription credits"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.SixMonthSubscriptionAllottedCount"] = "Six month subscription credits"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.OneYearSubscriptionAllottedCount"] = "One year subscription credits"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.DefaultPageSize"] = "Default page size"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.AllowGuestProfileBrowsing"] = "Allow guest profile browsing"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.ShowGender"] = "Show gender"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.HomepageWidgetZone"] = "Homepage widget zone"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.SidebarWidgetZone"] = "Sidebar widget zone"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.HomepageProfileCount"] = "Homepage profile count"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.EnablePluginEventConsumers"] = "Enable plugin event consumers"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.EnableRegistrationWorkflow"] = "Enable registration workflow"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.EnableActivationWorkflow"] = "Enable activation workflow"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.EnableOrderPaidWorkflow"] = "Enable paid order workflow"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.EnableAvailabilityWorkflow"] = "Enable availability workflow"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.EnableAvatarSyncWorkflow"] = "Enable avatar synchronization"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.EnableRelationshipNotifications"] = "Enable relationship notifications"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.EnableSynchronizationTask"] = "Enable synchronization task"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.ExecutionMode"] = "Execution mode"
            , ["Plugins.Misc.JobSupport.Admin.Configuration.Fields.SynchronizationBatchSize"] = "Synchronization batch size"
        });
        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        var task = await _scheduleTaskService.GetTaskByTypeAsync(JobSupportDefaults.SynchronizationTaskType);
        if (task != null)
            await _scheduleTaskService.DeleteTaskAsync(task);

        foreach (var template in await _messageTemplateService.GetMessageTemplatesByNameAsync(
                     JobSupportDefaults.CustomerAvailableMessageTemplateSystemName))
        {
            await _messageTemplateService.DeleteMessageTemplateAsync(template);
        }

        await _settingService.DeleteSettingAsync<JobSupportSettings>();
        await _permissionService.UninstallPermissionsAsync(new JobSupportPermissionConfigManager());
        await _localizationService.DeleteLocaleResourcesAsync(JobSupportDefaults.LocaleResourcePrefix);
        await base.UninstallAsync();
    }
}
