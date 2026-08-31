using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Web.Framework.Events;
using Nop.Web.Models.Customer;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Events;

public partial class JobSupportEventConsumer :
    IConsumer<CustomerRegisteredEvent>,
    IConsumer<CustomerActivatedEvent>,
    IConsumer<OrderPaidEvent>,
    IConsumer<EntityInsertedEvent<GenericAttribute>>,
    IConsumer<EntityUpdatedEvent<GenericAttribute>>,
    IConsumer<ModelPreparedEvent<CustomerNavigationModel>>,
    IConsumer<ModelPreparedEvent<BaseNopModel>>
{
    private readonly IJobSupportProfileService _profileService;
    private readonly IJobSupportSubscriptionService _subscriptionService;
    private readonly JobSupportSettings _settings;
    private readonly ILocalizationService _localizationService;

    public JobSupportEventConsumer(IJobSupportProfileService profileService,
        IJobSupportSubscriptionService subscriptionService,
        ILocalizationService localizationService,
        JobSupportSettings settings)
    {
        _profileService = profileService;
        _subscriptionService = subscriptionService;
        _localizationService = localizationService;
        _settings = settings;
    }

    public async Task HandleEventAsync(CustomerRegisteredEvent eventMessage)
    {
        if (!Allowed(_settings.EnableRegistrationWorkflow))
            return;

        await _profileService.EnsureProfileForCustomerAsync(eventMessage.Customer, _settings);
    }

    public async Task HandleEventAsync(CustomerActivatedEvent eventMessage)
    {
        if (!Allowed(_settings.EnableActivationWorkflow))
            return;

        await _profileService.ActivateProfileAsync(eventMessage.Customer, _settings);
    }

    public async Task HandleEventAsync(OrderPaidEvent eventMessage)
    {
        if (!Allowed(_settings.EnableOrderPaidWorkflow))
            return;

        await _subscriptionService.ApplyPaidOrderAsync(eventMessage.Order, _settings);
    }

    public Task HandleEventAsync(EntityInsertedEvent<GenericAttribute> eventMessage)
    {
        return HandleAvatarAttributeAsync(eventMessage.Entity);
    }

    public Task HandleEventAsync(EntityUpdatedEvent<GenericAttribute> eventMessage)
    {
        return HandleAvatarAttributeAsync(eventMessage.Entity);
    }

    public async Task HandleEventAsync(ModelPreparedEvent<CustomerNavigationModel> eventMessage)
    {
        if (!_settings.Enabled)
            return;
        var items = new[]
        {
            ("Plugin.Misc.JobSupport.AccountProfile", "Plugins.Misc.JobSupport.Navigation.Profile", 501, "customer-job-support-profile"),
            ("Plugin.Misc.JobSupport.AccountShortlisted", "Plugins.Misc.JobSupport.Navigation.Shortlisted", 502, "customer-job-support-shortlisted"),
            ("Plugin.Misc.JobSupport.AccountRelationships", "Plugins.Misc.JobSupport.Navigation.Relationships", 503, "customer-job-support-relationships"),
            ("Plugin.Misc.JobSupport.AccountSubscription", "Plugins.Misc.JobSupport.Navigation.Subscription", 504, "customer-job-support-subscription"),
            ("Plugin.Misc.JobSupport.AccountAffiliations", "Plugins.Misc.JobSupport.Navigation.Affiliations", 505, "customer-job-support-affiliations")
        };
        foreach (var item in items)
        {
            if (eventMessage.Model.CustomerNavigationItems.Any(existing => existing.RouteName == item.Item1))
                continue;
            eventMessage.Model.CustomerNavigationItems.Add(new CustomerNavigationItemModel
            {
                RouteName = item.Item1,
                Title = await _localizationService.GetResourceAsync(item.Item2),
                Tab = item.Item3,
                ItemClass = item.Item4
            });
        }
    }

    public Task HandleEventAsync(ModelPreparedEvent<BaseNopModel> eventMessage)
    {
        return eventMessage.Model is CustomerNavigationModel navigation
            ? HandleEventAsync(new ModelPreparedEvent<CustomerNavigationModel>(navigation))
            : Task.CompletedTask;
    }

    private async Task HandleAvatarAttributeAsync(GenericAttribute attribute)
    {
        if (!string.Equals(attribute?.Key, NopCustomerDefaults.AvatarPictureIdAttribute, StringComparison.Ordinal))
            return;
        if (!Allowed(_settings.EnableAvatarSyncWorkflow))
            return;

        await _profileService.SynchronizeAvatarAsync(attribute, _settings.ExecutionMode);
    }

    private bool Allowed(bool workflowFlag) =>
        _settings.Enabled && _settings.EnablePluginEventConsumers && workflowFlag &&
        _settings.ExecutionMode != WorkflowExecutionMode.Disabled;
}
