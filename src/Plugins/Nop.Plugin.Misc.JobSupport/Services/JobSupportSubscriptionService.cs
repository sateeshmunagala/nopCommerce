using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportSubscriptionService : IJobSupportSubscriptionService
{
    private readonly ICustomerActivityService _customerActivityService;
    private readonly ICustomerService _customerService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;
    private readonly IOrderService _orderService;
    private readonly IRewardPointService _rewardPointService;
    private readonly ShoppingCartSettings _shoppingCartSettings;

    public JobSupportSubscriptionService(ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ILogger logger,
        IOrderService orderService,
        IRewardPointService rewardPointService,
        ShoppingCartSettings shoppingCartSettings)
    {
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _logger = logger;
        _orderService = orderService;
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
