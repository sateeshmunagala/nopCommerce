using System.Text.Json;
using System.Transactions;
using Microsoft.Extensions.Logging;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class CreditPurchaseService : ICreditPurchaseService
{
    private readonly IRepository<CreditPurchaseGrant> _grantRepository;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly ICreditService _creditService;
    private readonly ICreditDepositNotificationService _creditDepositNotificationService;
    private readonly AIInterviewSettings _settings;
    private readonly ILogger<CreditPurchaseService> _logger;

    public CreditPurchaseService(IRepository<CreditPurchaseGrant> grantRepository,
        IOrderService orderService,
        IProductService productService,
        ICustomerService customerService,
        ICreditService creditService,
        AIInterviewSettings settings,
        ILogger<CreditPurchaseService> logger,
        ICreditDepositNotificationService creditDepositNotificationService = null)
    {
        _grantRepository = grantRepository;
        _orderService = orderService;
        _productService = productService;
        _customerService = customerService;
        _creditService = creditService;
        _settings = settings;
        _logger = logger;
        _creditDepositNotificationService = creditDepositNotificationService;
    }

    public async Task GrantCreditsForPaidOrderAsync(Order order)
    {
        if (order == null)
        {
            _logger.LogDebug("Skipping credit purchase grant because the paid order payload was null.");
            return;
        }

        if (order.Id <= 0 || order.CustomerId <= 0)
        {
            _logger.LogDebug("Skipping credit purchase grant because the order payload was invalid. OrderId: {OrderId}, CustomerId: {CustomerId}.", order.Id, order.CustomerId);
            return;
        }

        var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
        if (customer == null || !await _customerService.IsRegisteredAsync(customer))
        {
            _logger.LogInformation("Skipping credit purchase grant for order {OrderId} and customer {CustomerId}: customer is missing or not registered.", order.Id, order.CustomerId);
            return;
        }

        var skuCreditsMap = ParseMappings();
        if (skuCreditsMap.Count == 0)
        {
            _logger.LogWarning("Skipping credit purchase grant for order {OrderId}: no valid SKU mapping is configured.", order.Id);
            return;
        }

        var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
        foreach (var orderItem in orderItems ?? Array.Empty<OrderItem>())
        {
            if (orderItem == null)
            {
                _logger.LogDebug("Skipping null order item while granting credits for order {OrderId}.", order.Id);
                continue;
            }

            var processed = await _grantRepository.GetAllAsync(query => query.Where(grant => grant.OrderItemId == orderItem.Id));
            if (processed.Any())
            {
                _logger.LogDebug("Skipping already-processed credit purchase grant for order {OrderId}, orderItem {OrderItemId}, customer {CustomerId}.", order.Id, orderItem.Id, order.CustomerId);
                continue;
            }

            var product = await _orderService.GetProductByOrderItemIdAsync(orderItem.Id) ?? await _productService.GetProductByIdAsync(orderItem.ProductId);
            if (product == null)
            {
                _logger.LogWarning("Skipping credit purchase grant for order {OrderId}, orderItem {OrderItemId}: product {ProductId} could not be loaded.", order.Id, orderItem.Id, orderItem.ProductId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(product.Sku))
            {
                _logger.LogDebug("Skipping credit purchase grant for order {OrderId}, orderItem {OrderItemId}: product {ProductId} has a blank SKU.", order.Id, orderItem.Id, product.Id);
                continue;
            }

            var sku = product.Sku.Trim();
            if (!skuCreditsMap.TryGetValue(sku, out var creditsPerUnit))
            {
                _logger.LogDebug("Skipping credit purchase grant for order {OrderId}, orderItem {OrderItemId}: SKU {Sku} is not mapped for AI interview credits.", order.Id, orderItem.Id, sku);
                continue;
            }

            if (creditsPerUnit <= 0)
            {
                _logger.LogWarning("Skipping credit purchase grant for order {OrderId}, orderItem {OrderItemId}: SKU {Sku} mapped to invalid credit amount {CreditsPerUnit}.", order.Id, orderItem.Id, sku, creditsPerUnit);
                continue;
            }

            var creditsToGrant = creditsPerUnit * orderItem.Quantity;
            if (creditsToGrant <= 0)
            {
                _logger.LogWarning("Skipping credit purchase grant for order {OrderId}, orderItem {OrderItemId}: computed credit amount {CreditsToGrant} was not positive.", order.Id, orderItem.Id, creditsToGrant);
                continue;
            }

            try
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

                var existingGrant = await _grantRepository.GetAllAsync(query => query.Where(grant => grant.OrderItemId == orderItem.Id));
                if (existingGrant.Any())
                {
                    _logger.LogDebug("Skipping stale duplicate grant for order {OrderId}, orderItem {OrderItemId}, customer {CustomerId}.", order.Id, orderItem.Id, order.CustomerId);
                    scope.Complete();
                    continue;
                }

                await _grantRepository.InsertAsync(new CreditPurchaseGrant
                {
                    OrderId = order.Id,
                    OrderItemId = orderItem.Id,
                    CustomerId = order.CustomerId,
                    ProductId = product.Id,
                    Sku = sku,
                    Quantity = orderItem.Quantity,
                    CreditsPerUnit = creditsPerUnit,
                    CreditsGranted = creditsToGrant,
                    CreatedOnUtc = DateTime.UtcNow
                });

                await _creditService.AddCreditAsync(order.CustomerId, creditsToGrant,
                    $"Purchased credit pack: order #{order.Id}, SKU {sku}, credits {creditsToGrant}",
                    CreditLedgerSources.Order,
                    product.Id,
                    order.Id);

                scope.Complete();
                _logger.LogInformation("Granted credit purchase for order {OrderId}, orderItem {OrderItemId}, customer {CustomerId}, product {ProductId}, credits {CreditsGranted}.",
                    order.Id, orderItem.Id, order.CustomerId, product.Id, creditsToGrant);

                if (_creditDepositNotificationService != null)
                {
                    await _creditDepositNotificationService.SendCreditDepositedNotificationAsync(new CreditDepositNotificationRequest
                    {
                        CustomerId = order.CustomerId,
                        CreditsDeposited = creditsToGrant,
                        DepositSource = CreditDepositSources.ViaOrder,
                        OrderId = order.Id,
                        Remarks = $"Purchased credit pack: order #{order.Id}, SKU {sku}, credits {creditsToGrant}"
                    });
                }
            }
            catch (Exception ex) when (IsDuplicateGrantException(ex))
            {
                _logger.LogInformation("Skipped duplicate credit purchase grant for order {OrderId}, orderItem {OrderItemId}, customer {CustomerId}.", order.Id, orderItem.Id, order.CustomerId);
            }
        }
    }

    public static bool TryParseSkuMappings(string json, out Dictionary<string, int> mappings, out string errorMessage)
    {
        mappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "The credit product SKU mappings JSON is invalid. Use a JSON object such as {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10}.";
                return false;
            }

            foreach (var mapping in document.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(mapping.Name) || !mapping.Value.TryGetInt32(out var credits) || credits <= 0)
                {
                    errorMessage = "The credit product SKU mappings JSON is invalid. Use a JSON object such as {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10}.";
                    return false;
                }

                mappings[mapping.Name.Trim()] = credits;
            }

            return true;
        }
        catch (JsonException)
        {
            errorMessage = "The credit product SKU mappings JSON is invalid. Use a JSON object such as {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10}.";
            return false;
        }
    }

    protected virtual Dictionary<string, int> ParseMappings()
    {
        if (string.IsNullOrWhiteSpace(_settings.CreditProductSkuMappingsJson))
        {
            _logger.LogDebug("Credit product SKU mappings JSON is empty. Paid orders will not grant AI interview credits until mappings are configured.");
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        if (!TryParseSkuMappings(_settings.CreditProductSkuMappingsJson, out var mappings, out var errorMessage))
        {
            _logger.LogWarning("Invalid credit product SKU mappings JSON: {ErrorMessage}", errorMessage);
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        if (mappings.Count == 0)
        {
            _logger.LogWarning("Credit product SKU mappings JSON parsed successfully but contained no usable mappings.");
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return mappings;
    }

    protected virtual bool IsDuplicateGrantException(Exception exception)
    {
        while (exception != null)
        {
            var message = exception.Message ?? string.Empty;
            if (message.Contains("IX_AIInterview_CreditPurchaseGrant_OrderItemId", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("IX_CreditPurchaseGrant_OrderItemId", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                return true;

            exception = exception.InnerException;
        }

        return false;
    }
}
