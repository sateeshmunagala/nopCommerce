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
    private readonly AIInterviewSettings _settings;
    private readonly ILogger<CreditPurchaseService> _logger;

    public CreditPurchaseService(IRepository<CreditPurchaseGrant> grantRepository,
        IOrderService orderService,
        IProductService productService,
        ICustomerService customerService,
        ICreditService creditService,
        AIInterviewSettings settings,
        ILogger<CreditPurchaseService> logger)
    {
        _grantRepository = grantRepository;
        _orderService = orderService;
        _productService = productService;
        _customerService = customerService;
        _creditService = creditService;
        _settings = settings;
        _logger = logger;
    }

    public async Task GrantCreditsForPaidOrderAsync(Order order)
    {
        if (order == null || order.Id <= 0 || order.CustomerId <= 0)
            return;

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
                continue;

            var processed = await _grantRepository.GetAllAsync(query => query.Where(grant => grant.OrderItemId == orderItem.Id));
            if (processed.Any())
            {
                _logger.LogDebug("Skipping already-processed credit purchase grant for order {OrderId}, orderItem {OrderItemId}, customer {CustomerId}.", order.Id, orderItem.Id, order.CustomerId);
                continue;
            }

            var product = await _orderService.GetProductByOrderItemIdAsync(orderItem.Id) ?? await _productService.GetProductByIdAsync(orderItem.ProductId);
            if (product == null || string.IsNullOrWhiteSpace(product.Sku))
                continue;

            var sku = product.Sku.Trim();
            if (!skuCreditsMap.TryGetValue(sku, out var creditsPerUnit) || creditsPerUnit <= 0)
                continue;

            var creditsToGrant = creditsPerUnit * orderItem.Quantity;
            if (creditsToGrant <= 0)
                continue;

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
                    $"Purchased credit pack: order #{order.Id}, SKU {sku}, credits {creditsToGrant}");

                scope.Complete();
                _logger.LogInformation("Granted credit purchase for order {OrderId}, orderItem {OrderItemId}, customer {CustomerId}, product {ProductId}, credits {CreditsGranted}.",
                    order.Id, orderItem.Id, order.CustomerId, product.Id, creditsToGrant);
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
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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
            if (message.Contains("IX_CreditPurchaseGrant_OrderItemId", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                return true;

            exception = exception.InnerException;
        }

        return false;
    }
}
