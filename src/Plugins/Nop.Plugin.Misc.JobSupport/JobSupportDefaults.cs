namespace Nop.Plugin.Misc.JobSupport;

public static partial class JobSupportDefaults
{
    public static string SystemName => "Misc.JobSupport";
    public static string ConfigurationRouteName => "Plugin.Misc.JobSupport.Configure";
    public static string PublicRoutePrefix => "job-support";
    public static string AccountRoutePrefix => "customer/job-support";
    public static string AdminRoutePrefix => "Admin/JobSupport";
    public static string PaidCustomerRoleSystemName => "JobSupportPaidCustomer";
    public static string GiveSupportRoleSystemName => "JobSupportGiveSupport";
    public static string TakeSupportRoleSystemName => "JobSupportTakeSupport";
    public static string CustomerAvailableMessageTemplateSystemName => "JobSupport.CustomerAvailable";
    public static string SynchronizationTaskType => "Nop.Plugin.Misc.JobSupport.Services.JobSupportSynchronizationTask, Nop.Plugin.Misc.JobSupport";
    public static string SynchronizationTaskName => "JobSupport profile synchronization";
    public static string ProfileTypeAttribute => "JobSupport.ProfileType";
    public static string SubscriptionIdAttribute => "JobSupport.SubscriptionId";
    public static string SubscriptionDateAttribute => "JobSupport.SubscriptionDate";
    public static string SubscriptionAllottedCountAttribute => "JobSupport.SubscriptionAllottedCount";
    public static string SubscriptionUsedCreditCountAttribute => "JobSupport.SubscriptionUsedCreditCount";
    public static string NotifiedAboutAvailabilityAttribute => "JobSupport.NotifiedAboutAvailability";
    public static string LocaleResourcePrefix => "Plugins.Misc.JobSupport";
}
