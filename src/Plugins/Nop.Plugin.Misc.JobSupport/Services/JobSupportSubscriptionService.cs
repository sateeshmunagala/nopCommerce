using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Catalog;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportSubscriptionService : IJobSupportSubscriptionService
{
    private readonly ICustomerActivityService _customerActivityService;
    private readonly ICustomerService _customerService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IRewardPointService _rewardPointService;
    private readonly ShoppingCartSettings _shoppingCartSettings;

    public JobSupportSubscriptionService(ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ILogger logger,
        IOrderService orderService,
        IProductService productService,
        IRewardPointService rewardPointService,
        ShoppingCartSettings shoppingCartSettings)
    {
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _logger = logger;
        _orderService = orderService;
        _productService = productService;
        _rewardPointService = rewardPointService;
        _shoppingCartSettings = shoppingCartSettings;
    }

    public async Task ApplyPaidOrderAsync(Order order, JobSupportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled || !settings.EnableOrderPaidWorkflow ||
            settings.ExecutionMode == WorkflowExecutionMode.Disabled)
            return;

        var planByProductId = GetPlans(settings);
        if (planByProductId.Count == 0)
            return;

        var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
        var plan = orderItems.Select(item => item.ProductId)
            .Where(planByProductId.ContainsKey)
            .Select(productId => planByProductId[productId])
            .FirstOrDefault();
        if (plan == null)
            return;

        var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
        if (customer == null || customer.Deleted)
            return;

        var originatingOrderId = await _genericAttributeService.GetAttributeAsync<int>(customer,
            JobSupportDefaults.SubscriptionOrderIdAttribute,
            order.StoreId);
        if (originatingOrderId == order.Id)
            return;

        var previousExpiry = await _genericAttributeService.GetAttributeAsync<DateTime?>(customer,
            JobSupportDefaults.SubscriptionExpiryDateAttribute,
            order.StoreId);
        var previousAllotted = await _genericAttributeService.GetAttributeAsync<int>(customer,
            JobSupportDefaults.SubscriptionAllottedCountAttribute,
            order.StoreId);
        var previousUsed = await _genericAttributeService.GetAttributeAsync<int>(customer,
            JobSupportDefaults.SubscriptionUsedCreditCountAttribute,
            order.StoreId);
        var carriedCredits = previousExpiry.HasValue && previousExpiry.Value > order.CreatedOnUtc
            ? Math.Max(0, previousAllotted - previousUsed)
            : 0;
        var newAllotted = plan.AllottedCredits + carriedCredits;
        var newExpiry = order.CreatedOnUtc.AddMonths(plan.DurationMonths);

        if (settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await _logger.InformationAsync(
                $"JobSupport shadow paid-order outcome: order {order.Id}, customer {customer.Id}, duration {plan.DurationMonths}, credits {newAllotted}.");
            return;
        }

        if (settings.ExecutionMode != WorkflowExecutionMode.Live)
            return;

        var paidRole = await _customerService.GetCustomerRoleBySystemNameAsync(settings.PaidCustomerRoleSystemName);
        if (paidRole != null && !await _customerService.IsInCustomerRoleAsync(customer, paidRole.SystemName, false))
        {
            await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping
            {
                CustomerId = customer.Id,
                CustomerRoleId = paidRole.Id
            });
        }

        await _genericAttributeService.SaveAttributeAsync(customer,
            JobSupportDefaults.SubscriptionIdAttribute,
            plan.ProductId,
            order.StoreId);
        await _genericAttributeService.SaveAttributeAsync(customer,
            JobSupportDefaults.SubscriptionDateAttribute,
            order.CreatedOnUtc,
            order.StoreId);
        await _genericAttributeService.SaveAttributeAsync(customer,
            JobSupportDefaults.SubscriptionExpiryDateAttribute,
            newExpiry,
            order.StoreId);
        await _genericAttributeService.SaveAttributeAsync(customer,
            JobSupportDefaults.SubscriptionAllottedCountAttribute,
            newAllotted,
            order.StoreId);
        await _genericAttributeService.SaveAttributeAsync(customer,
            JobSupportDefaults.SubscriptionUsedCreditCountAttribute,
            0,
            order.StoreId);
        await _genericAttributeService.SaveAttributeAsync(customer,
            JobSupportDefaults.SubscriptionOrderIdAttribute,
            order.Id,
            order.StoreId);

        if (settings.WriteLegacyRewardPointsHistory)
        {
            await _rewardPointService.AddRewardPointsHistoryEntryAsync(customer,
                plan.AllottedCredits,
                order.StoreId,
                $"JobSupport subscription grant for order {order.Id}",
                order,
                endDate: newExpiry);
        }

        await _customerActivityService.InsertActivityAsync(customer,
            JobSupportDefaults.ActivityTypeSystemName,
            $"JobSupport subscription workflow applied for order {order.Id}.",
            order);
    }

    public async Task<SubscriptionSummary> GetSubscriptionAsync(int customerId, int storeId)
    {
        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer == null || customer.Deleted)
            return new SubscriptionSummary { Status = SubscriptionStatus.Inactive };

        var startDate = await _genericAttributeService.GetAttributeAsync<DateTime?>(customer,
            JobSupportDefaults.SubscriptionDateAttribute,
            storeId);
        var expiryDate = await _genericAttributeService.GetAttributeAsync<DateTime?>(customer,
            JobSupportDefaults.SubscriptionExpiryDateAttribute,
            storeId);
        var allotted = await _genericAttributeService.GetAttributeAsync<int>(customer,
            JobSupportDefaults.SubscriptionAllottedCountAttribute,
            storeId);
        var used = await _genericAttributeService.GetAttributeAsync<int>(customer,
            JobSupportDefaults.SubscriptionUsedCreditCountAttribute,
            storeId);

        var status = !startDate.HasValue
            ? SubscriptionStatus.Inactive
            : expiryDate.HasValue && expiryDate.Value <= DateTime.UtcNow
                ? SubscriptionStatus.Expired
                : allotted - used > 0 ? SubscriptionStatus.Active : SubscriptionStatus.Exhausted;

        return new SubscriptionSummary
        {
            Status = status,
            StartDate = startDate,
            ExpiryDate = expiryDate,
            AllottedCredits = Math.Max(0, allotted),
            UsedCredits = Math.Max(0, used)
        };
    }

    public async Task<ContactRevealDecision> RevealContactAsync(int customerId, int targetProfileId, int storeId)
    {
        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        var profile = await _productService.GetProductByIdAsync(targetProfileId);
        if (customer == null || customer.Deleted || profile == null || profile.Deleted || profile.VendorId <= 0)
            return Failed("Plugins.Misc.JobSupport.Contact.Errors.NotFound");
        if (profile.VendorId == customer.Id)
            return Failed("Plugins.Misc.JobSupport.Contact.Errors.SelfReveal");

        var targetCustomer = await _customerService.GetCustomerByIdAsync(profile.VendorId);
        if (targetCustomer == null || targetCustomer.Deleted)
            return Failed("Plugins.Misc.JobSupport.Contact.Errors.NotFound");

        var revealedIds = ParseIdentifiers(await _genericAttributeService.GetAttributeAsync<string>(customer,
            JobSupportDefaults.RevealedProfileIdsAttribute,
            storeId));
        var subscription = await GetSubscriptionAsync(customerId, storeId);
        var alreadyRevealed = revealedIds.Contains(targetProfileId);
        if (!alreadyRevealed && subscription.Status != SubscriptionStatus.Active)
            return Failed("Plugins.Misc.JobSupport.Contact.Errors.SubscriptionRequired", subscription.RemainingCredits);

        if (!alreadyRevealed)
        {
            revealedIds.Add(targetProfileId);
            await _genericAttributeService.SaveAttributeAsync(customer,
                JobSupportDefaults.RevealedProfileIdsAttribute,
                string.Join(',', revealedIds.OrderBy(id => id)),
                storeId);
            await _genericAttributeService.SaveAttributeAsync(customer,
                JobSupportDefaults.SubscriptionUsedCreditCountAttribute,
                subscription.UsedCredits + 1,
                storeId);
            subscription = subscription with { UsedCredits = subscription.UsedCredits + 1 };
        }

        await _customerActivityService.InsertActivityAsync(customer,
            JobSupportDefaults.ActivityTypeSystemName,
            $"JobSupport contact entitlement used for profile {targetProfileId}.",
            profile);

        return new ContactRevealDecision
        {
            Succeeded = true,
            AlreadyRevealed = alreadyRevealed,
            Email = targetCustomer.Email,
            Phone = targetCustomer.Phone,
            RemainingCredits = subscription.RemainingCredits,
            MessageKey = "Plugins.Misc.JobSupport.Contact.Revealed"
        };
    }

    private static HashSet<int> ParseIdentifiers(string value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(item => int.TryParse(item, out var id) ? id : 0)
        .Where(id => id > 0)
        .ToHashSet();

    private static ContactRevealDecision Failed(string messageKey, int remainingCredits = 0) =>
        new() { MessageKey = messageKey, RemainingCredits = remainingCredits };

    private Dictionary<int, SubscriptionPlan> GetPlans(JobSupportSettings settings)
    {
        var plans = new[]
        {
            new SubscriptionPlan(settings.ThreeMonthSubscriptionProductId,
                3,
                _shoppingCartSettings.ThreeMonthSubscriptionAllottedCount),
            new SubscriptionPlan(settings.SixMonthSubscriptionProductId,
                6,
                _shoppingCartSettings.SixMonthSubscriptionAllottedCount),
            new SubscriptionPlan(settings.OneYearSubscriptionProductId,
                12,
                _shoppingCartSettings.OneYearSubscriptionAllottedCount)
        };

        return plans.Where(plan => plan.ProductId > 0)
            .GroupBy(plan => plan.ProductId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private sealed record SubscriptionPlan(int ProductId, int DurationMonths, int AllottedCredits);
}
