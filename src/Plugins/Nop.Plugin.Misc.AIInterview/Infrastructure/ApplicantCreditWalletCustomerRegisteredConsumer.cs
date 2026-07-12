using Nop.Core.Domain.Customers;
using Nop.Services.Customers;
using Nop.Services.Events;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class ApplicantCreditWalletCustomerRegisteredConsumer : IConsumer<CustomerRegisteredEvent>
{
    private readonly Services.ICreditService _creditService;

    public ApplicantCreditWalletCustomerRegisteredConsumer(Services.ICreditService creditService)
    {
        _creditService = creditService;
    }

    public async Task HandleEventAsync(CustomerRegisteredEvent eventMessage)
    {
        var customer = eventMessage?.Customer;
        if (customer == null || customer.Deleted || customer.VendorId > 0)
            return;

        await _creditService.GetOrCreateWalletAsync(customer.Id);
    }
}
