using System.Text.Json;
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

    public CreditPurchaseService(IRepository<CreditPurchaseGrant> grantRepository,
        IOrderService orderService,
        IProductService productService,
        ICustomerService customerService,
        ICreditService creditService,
        AIInterviewSettings settings)
    {
        _grantRepository = grantRepository;
        _orderService = orderService;
        _productService = productService;
        _customerService = customerService;
        _creditService = creditService;
        _settings = settings;
    }

    public async Task GrantCreditsForPaidOrderAsync(Order order)
    {
        if (order == null || order.Id <= 0 || order.CustomerId <= 0)
            return;

        var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
        if (customer == null || !await _customerService.IsRegisteredAsync(customer))
            return;

        var skuCreditsMap = ParseMappings();
        if (skuCreditsMap.Count == 0)
            return;

        var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
        foreach (var orderItem in orderItems ?? Array.Empty<OrderItem>())
        {
            if (orderItem == null)
                continue;

            var processed = await _grantRepository.GetAllAsync(query => query.Where(grant => grant.OrderItemId == orderItem.Id));
            if (processed.Any())
                continue;

            var product = await _orderService.GetProductByOrderItemIdAsync(orderItem.Id) ?? await _productService.GetProductByIdAsync(orderItem.ProductId);
            if (product == null || string.IsNullOrWhiteSpace(product.Sku))
                continue;

            var sku = product.Sku.Trim();
            if (!skuCreditsMap.TryGetValue(sku, out var creditsPerUnit) || creditsPerUnit <= 0)
                continue;

            var creditsToGrant = creditsPerUnit * orderItem.Quantity;
            if (creditsToGrant <= 0)
                continue;

            await _creditService.AddCreditAsync(order.CustomerId, creditsToGrant,
                $"Purchased credit pack: order #{order.Id}, SKU {sku}, credits {creditsToGrant}");

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
        }
    }

    protected virtual Dictionary<string, int> ParseMappings()
    {
        if (string.IsNullOrWhiteSpace(_settings.CreditProductSkuMappingsJson))
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var mappings = JsonSerializer.Deserialize<Dictionary<string, int>>(_settings.CreditProductSkuMappingsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return mappings == null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : mappings
                    .Where(mapping => !string.IsNullOrWhiteSpace(mapping.Key) && mapping.Value > 0)
                    .ToDictionary(mapping => mapping.Key.Trim(), mapping => mapping.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
