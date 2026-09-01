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
    private readonly IRepository<JobSupportContactReveal> _contactRevealRepository;
    private readonly IRepository<JobSupportProfile> _profileRepository;
    private readonly IRepository<JobSupportSubscription> _subscriptionRepository;
    private readonly JobSupportSettings _settings;

    public JobSupportSubscriptionService(ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ILogger logger,
        IOrderService orderService,
        IProductService productService,
        IRewardPointService rewardPointService,
        ShoppingCartSettings shoppingCartSettings,
        IRepository<JobSupportContactReveal> contactRevealRepository,
        IRepository<JobSupportProfile> profileRepository,
        IRepository<JobSupportSubscription> subscriptionRepository,
        JobSupportSettings settings)
    {
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _logger = logger;
        _orderService = orderService;
        _productService = productService;
        _rewardPointService = rewardPointService;
        _shoppingCartSettings = shoppingCartSettings;
        _contactRevealRepository = contactRevealRepository;
        _profileRepository = profileRepository;
        _subscriptionRepository = subscriptionRepository;
        _settings = settings;
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

        if (settings.DataWriteMode == DataAccessMode.Plugin)
        {
            var previous = await _subscriptionRepository.Table
                .Where(subscription => subscription.CustomerId == customer.Id)
                .OrderByDescending(subscription => subscription.StartOnUtc)
                .FirstOrDefaultAsync();
            var carriedPluginCredits = previous != null && previous.EndOnUtc > order.CreatedOnUtc
                ? Math.Max(0, previous.AllottedCredits + previous.CarriedCredits - previous.UsedCredits)
                : 0;
            if (settings.ExecutionMode == WorkflowExecutionMode.Shadow)
            {
                await _logger.InformationAsync(
                    $"JobSupport shadow paid-order outcome: order {order.Id}, customer {customer.Id}, duration {plan.DurationMonths}, credits {plan.AllottedCredits + carriedPluginCredits}.");
                return;
            }
            if (settings.ExecutionMode != WorkflowExecutionMode.Live)
                return;
            await UpsertPluginSubscriptionsAsync(order, orderItems, planByProductId, carriedPluginCredits);
            return;
        }

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

        if (settings.DataWriteMode == DataAccessMode.Dual)
            await ExecutePluginWriteAsync(() => UpsertPluginSubscriptionsAsync(order, orderItems, planByProductId, carriedCredits), order.Id, "subscription");
    }

    public async Task<SubscriptionSummary> GetSubscriptionAsync(int customerId, int storeId)
    {
        if (_settings.DataReadMode == DataAccessMode.Plugin)
            return await GetPluginSubscriptionAsync(customerId, updateExpiry: _settings.DataWriteMode == DataAccessMode.Plugin);

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

        var summary = new SubscriptionSummary
        {
            Status = status,
            StartDate = startDate,
            ExpiryDate = expiryDate,
            AllottedCredits = Math.Max(0, allotted),
            UsedCredits = Math.Max(0, used)
        };
        if (_settings.DataReadMode != DataAccessMode.Compare)
            return summary;

        var plugin = await GetPluginSubscriptionAsync(customerId, updateExpiry: false);
        await ComparePluginSubscriptionAsync(customerId, summary, plugin);
        return _settings.CompareReturnMode == DataAccessMode.Plugin ? plugin : summary;
    }

    public async Task<ContactRevealDecision> RevealContactAsync(int customerId, int targetProfileId, int storeId)
    {
        if (_settings.DataWriteMode == DataAccessMode.Plugin)
            return await RevealPluginContactAsync(customerId, targetProfileId);

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

        if (_settings.DataWriteMode == DataAccessMode.Dual)
            await ExecutePluginWriteAsync(() => UpsertPluginRevealAsync(customerId, targetProfileId), customerId, "contact-reveal");

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

    private async Task UpsertPluginSubscriptionsAsync(Order order, IList<OrderItem> orderItems,
        IReadOnlyDictionary<int, SubscriptionPlan> plans, int carriedCredits)
    {
        var now = DateTime.UtcNow;
        foreach (var item in orderItems.Where(item => plans.ContainsKey(item.ProductId)))
        {
            var plan = plans[item.ProductId];
            var entity = await _subscriptionRepository.Table
                .Where(subscription => subscription.OrderId == order.Id && subscription.OrderItemId == item.Id)
                .FirstOrDefaultAsync();
            if (entity != null)
                continue;
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

    private async Task UpsertPluginRevealAsync(int viewerCustomerId, int legacyProductId)
    {
        var profile = await _profileRepository.Table
            .Where(item => item.LegacyProductId == legacyProductId)
            .FirstOrDefaultAsync();
        if (profile == null)
            throw new InvalidOperationException("Plugin profile dependency is missing.");
        if (await _contactRevealRepository.Table.AnyAsync(reveal =>
                reveal.ViewerCustomerId == viewerCustomerId && reveal.TargetProfileId == profile.Id))
            return;
        var subscription = await _subscriptionRepository.Table
            .Where(item => item.CustomerId == viewerCustomerId)
            .OrderByDescending(item => item.StartOnUtc)
            .FirstOrDefaultAsync();
        await _contactRevealRepository.InsertAsync(new JobSupportContactReveal
        {
            SubscriptionId = subscription?.Id ?? 0,
            ViewerCustomerId = viewerCustomerId,
            TargetCustomerId = profile.CustomerId,
            TargetProfileId = profile.Id,
            CreditCost = 1,
            RevealedOnUtc = DateTime.UtcNow
        }, false);
        if (subscription != null)
        {
            subscription.UsedCredits++;
            subscription.UpdatedOnUtc = DateTime.UtcNow;
            await _subscriptionRepository.UpdateAsync(subscription, false);
        }
    }

    private async Task ComparePluginSubscriptionAsync(int customerId,
        SubscriptionSummary legacy,
        SubscriptionSummary plugin)
    {
        var mismatch = plugin.Status != legacy.Status || plugin.AllottedCredits != legacy.AllottedCredits ||
            plugin.UsedCredits != legacy.UsedCredits || plugin.StartDate != legacy.StartDate || plugin.ExpiryDate != legacy.ExpiryDate;
        if (mismatch)
            await _logger.WarningAsync($"JobSupport subscription comparison mismatch for source {customerId}.");
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
            item.LegacyProductId == targetProfileId || item.Id == targetProfileId);
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
                .Where(item => item.CustomerId == customerId && item.Status == (int)SubscriptionStatus.Active)
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

    private async Task ExecutePluginWriteAsync(Func<Task> operation, int sourceId, string operationName)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync($"JobSupport dual write failed for {operationName} source {sourceId}.", exception);
            throw;
        }
    }
}
