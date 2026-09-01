using LinqToDB;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Domain;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportSubscriptionService : IJobSupportSubscriptionService
{
    private readonly ICustomerActivityService _customerActivityService;
    private readonly ICustomerService _customerService;
    private readonly ILogger _logger;
    private readonly IOrderService _orderService;
    private readonly IRepository<JobSupportContactReveal> _contactRevealRepository;
    private readonly IRepository<JobSupportProfile> _profileRepository;
    private readonly IRepository<JobSupportSubscription> _subscriptionRepository;

    public JobSupportSubscriptionService(ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        ILogger logger,
        IOrderService orderService,
        IRepository<JobSupportContactReveal> contactRevealRepository,
        IRepository<JobSupportProfile> profileRepository,
        IRepository<JobSupportSubscription> subscriptionRepository)
    {
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _logger = logger;
        _orderService = orderService;
        _contactRevealRepository = contactRevealRepository;
        _profileRepository = profileRepository;
        _subscriptionRepository = subscriptionRepository;
    }

    public async Task ApplyPaidOrderAsync(Order order, JobSupportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled || !settings.EnableOrderPaidWorkflow ||
            settings.ExecutionMode == WorkflowExecutionMode.Disabled)
            return;

        var plans = GetPlans(settings);
        if (plans.Count == 0)
            return;

        var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
        var plan = orderItems.Select(item => item.ProductId)
            .Where(plans.ContainsKey)
            .Select(productId => plans[productId])
            .FirstOrDefault();
        if (plan == null)
            return;

        var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
        if (customer == null || customer.Deleted)
            return;

        var previous = await _subscriptionRepository.Table
            .Where(subscription => subscription.CustomerId == customer.Id)
            .OrderByDescending(subscription => subscription.StartOnUtc)
            .FirstOrDefaultAsync();
        var carriedCredits = previous != null && previous.EndOnUtc > order.CreatedOnUtc
            ? Math.Max(0, previous.AllottedCredits + previous.CarriedCredits - previous.UsedCredits)
            : 0;

        if (settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await _logger.InformationAsync(
                $"JobSupport shadow paid-order outcome: order {order.Id}, customer {customer.Id}, duration {plan.DurationMonths}, credits {plan.AllottedCredits + carriedCredits}.");
            return;
        }

        if (settings.ExecutionMode != WorkflowExecutionMode.Live)
            return;

        await UpsertSubscriptionsAsync(order, orderItems, plans, carriedCredits);

        var paidRole = await _customerService.GetCustomerRoleBySystemNameAsync(settings.PaidCustomerRoleSystemName);
        if (paidRole != null && !await _customerService.IsInCustomerRoleAsync(customer, paidRole.SystemName, false))
        {
            await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping
            {
                CustomerId = customer.Id,
                CustomerRoleId = paidRole.Id
            });
        }

        await _customerActivityService.InsertActivityAsync(customer,
            JobSupportDefaults.ActivityTypeSystemName,
            $"JobSupport subscription workflow applied for order {order.Id}.",
            order);
    }

    public Task<SubscriptionSummary> GetSubscriptionAsync(int customerId, int storeId) =>
        GetPluginSubscriptionAsync(customerId, updateExpiry: true);

    public Task<ContactRevealDecision> RevealContactAsync(int customerId, int targetProfileId, int storeId) =>
        RevealPluginContactAsync(customerId, targetProfileId);

    private static Dictionary<int, SubscriptionPlan> GetPlans(JobSupportSettings settings)
    {
        var plans = new[]
        {
            new SubscriptionPlan(settings.ThreeMonthSubscriptionProductId,
                3,
                settings.ThreeMonthSubscriptionAllottedCount),
            new SubscriptionPlan(settings.SixMonthSubscriptionProductId,
                6,
                settings.SixMonthSubscriptionAllottedCount),
            new SubscriptionPlan(settings.OneYearSubscriptionProductId,
                12,
                settings.OneYearSubscriptionAllottedCount)
        };

        return plans.Where(plan => plan.ProductId > 0)
            .GroupBy(plan => plan.ProductId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private async Task UpsertSubscriptionsAsync(Order order,
        IList<OrderItem> orderItems,
        IReadOnlyDictionary<int, SubscriptionPlan> plans,
        int carriedCredits)
    {
        var now = DateTime.UtcNow;
        foreach (var item in orderItems.Where(item => plans.ContainsKey(item.ProductId)))
        {
            var existing = await _subscriptionRepository.Table
                .AnyAsync(subscription => subscription.OrderId == order.Id &&
                                          subscription.OrderItemId == item.Id);
            if (existing)
                continue;

            var plan = plans[item.ProductId];
            await _subscriptionRepository.InsertAsync(new JobSupportSubscription
            {
                CustomerId = order.CustomerId,
                OrderId = order.Id,
                OrderItemId = item.Id,
                ProductId = item.ProductId,
                Status = (int)SubscriptionStatus.Active,
                StartOnUtc = order.CreatedOnUtc,
                EndOnUtc = order.CreatedOnUtc.AddMonths(plan.DurationMonths),
                AllottedCredits = plan.AllottedCredits,
                CarriedCredits = carriedCredits,
                UsedCredits = 0,
                CreatedOnUtc = order.CreatedOnUtc,
                UpdatedOnUtc = now
            }, false);
        }
    }

    private async Task<SubscriptionSummary> GetPluginSubscriptionAsync(int customerId, bool updateExpiry)
    {
        var subscription = await _subscriptionRepository.Table
            .Where(item => item.CustomerId == customerId)
            .OrderByDescending(item => item.StartOnUtc)
            .FirstOrDefaultAsync();
        if (subscription == null)
            return new SubscriptionSummary { Status = SubscriptionStatus.Inactive };

        var remaining = subscription.AllottedCredits + subscription.CarriedCredits - subscription.UsedCredits;
        var status = subscription.EndOnUtc <= DateTime.UtcNow
            ? SubscriptionStatus.Expired
            : remaining > 0 ? SubscriptionStatus.Active : SubscriptionStatus.Exhausted;
        if (updateExpiry && subscription.Status != (int)status)
        {
            subscription.Status = (int)status;
            subscription.UpdatedOnUtc = DateTime.UtcNow;
            await _subscriptionRepository.UpdateAsync(subscription, false);
        }

        return new SubscriptionSummary
        {
            Status = status,
            StartDate = subscription.StartOnUtc,
            ExpiryDate = subscription.EndOnUtc,
            AllottedCredits = Math.Max(0, subscription.AllottedCredits + subscription.CarriedCredits),
            UsedCredits = Math.Max(0, subscription.UsedCredits)
        };
    }

    private async Task<ContactRevealDecision> RevealPluginContactAsync(int customerId, int targetProfileId)
    {
        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item =>
            item.Id == targetProfileId || item.LegacyProductId == targetProfileId);
        if (customer == null || customer.Deleted || profile == null || !profile.IsPublished)
            return Failed("Plugins.Misc.JobSupport.Contact.Errors.NotFound");
        if (profile.CustomerId == customerId)
            return Failed("Plugins.Misc.JobSupport.Contact.Errors.SelfReveal");

        var targetCustomer = await _customerService.GetCustomerByIdAsync(profile.CustomerId);
        if (targetCustomer == null || targetCustomer.Deleted)
            return Failed("Plugins.Misc.JobSupport.Contact.Errors.NotFound");

        var existing = await _contactRevealRepository.Table.FirstOrDefaultAsync(reveal =>
            reveal.ViewerCustomerId == customerId && reveal.TargetProfileId == profile.Id);
        var summary = await GetPluginSubscriptionAsync(customerId, updateExpiry: true);
        if (existing == null && summary.Status != SubscriptionStatus.Active)
            return Failed("Plugins.Misc.JobSupport.Contact.Errors.SubscriptionRequired", summary.RemainingCredits);

        if (existing == null)
        {
            var subscription = await _subscriptionRepository.Table
                .Where(item => item.CustomerId == customerId &&
                               item.Status == (int)SubscriptionStatus.Active)
                .OrderByDescending(item => item.StartOnUtc)
                .FirstOrDefaultAsync();
            if (subscription == null)
                return Failed("Plugins.Misc.JobSupport.Contact.Errors.SubscriptionRequired", summary.RemainingCredits);

            await _contactRevealRepository.InsertAsync(new JobSupportContactReveal
            {
                SubscriptionId = subscription.Id,
                ViewerCustomerId = customerId,
                TargetCustomerId = profile.CustomerId,
                TargetProfileId = profile.Id,
                CreditCost = 1,
                RevealedOnUtc = DateTime.UtcNow
            }, false);
            subscription.UsedCredits++;
            subscription.UpdatedOnUtc = DateTime.UtcNow;
            await _subscriptionRepository.UpdateAsync(subscription, false);
            summary = summary with { UsedCredits = summary.UsedCredits + 1 };
        }

        return new ContactRevealDecision
        {
            Succeeded = true,
            AlreadyRevealed = existing != null,
            Email = targetCustomer.Email,
            Phone = targetCustomer.Phone,
            RemainingCredits = summary.RemainingCredits,
            MessageKey = "Plugins.Misc.JobSupport.Contact.Revealed"
        };
    }

    private static ContactRevealDecision Failed(string messageKey, int remainingCredits = 0) => new()
    {
        MessageKey = messageKey,
        RemainingCredits = remainingCredits
    };

    private sealed record SubscriptionPlan(int ProductId, int DurationMonths, int AllottedCredits);
}
