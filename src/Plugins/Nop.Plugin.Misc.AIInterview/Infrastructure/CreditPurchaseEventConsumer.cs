using Nop.Core.Domain.Orders;
using Nop.Services.Events;
using Nop.Plugin.Misc.AIInterview.Services;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class CreditPurchaseEventConsumer : IConsumer<OrderPaidEvent>
{
    private readonly ICreditPurchaseService _creditPurchaseService;

    public CreditPurchaseEventConsumer(ICreditPurchaseService creditPurchaseService)
    {
        _creditPurchaseService = creditPurchaseService;
    }

    public async Task HandleEventAsync(OrderPaidEvent eventMessage)
    {
        if (eventMessage?.Order == null)
            return;

        await _creditPurchaseService.GrantCreditsForPaidOrderAsync(eventMessage.Order);
    }
}
