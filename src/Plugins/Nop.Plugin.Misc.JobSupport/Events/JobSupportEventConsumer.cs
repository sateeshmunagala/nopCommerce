using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Services.Events;

namespace Nop.Plugin.Misc.JobSupport.Events;

public partial class JobSupportEventConsumer :
    IConsumer<CustomerRegisteredEvent>,
    IConsumer<CustomerActivatedEvent>,
    IConsumer<OrderPaidEvent>,
    IConsumer<EntityInsertedEvent<GenericAttribute>>,
    IConsumer<EntityUpdatedEvent<GenericAttribute>>
{
    private readonly IJobSupportProfileService _profileService;
    private readonly IJobSupportSubscriptionService _subscriptionService;
    private readonly JobSupportSettings _settings;

    public JobSupportEventConsumer(IJobSupportProfileService profileService,
        IJobSupportSubscriptionService subscriptionService,
        JobSupportSettings settings)
    {
        _profileService = profileService;
        _subscriptionService = subscriptionService;
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
