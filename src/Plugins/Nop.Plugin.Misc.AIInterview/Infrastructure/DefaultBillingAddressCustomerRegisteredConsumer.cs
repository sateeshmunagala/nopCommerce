using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Events;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class DefaultBillingAddressCustomerRegisteredConsumer : IConsumer<CustomerRegisteredEvent>
{
    private readonly IAddressService _addressService;
    private readonly ICustomerService _customerService;

    public DefaultBillingAddressCustomerRegisteredConsumer(
        IAddressService addressService,
        ICustomerService customerService)
    {
        _addressService = addressService;
        _customerService = customerService;
    }

    public async Task HandleEventAsync(CustomerRegisteredEvent eventMessage)
    {
        var customer = eventMessage?.Customer;
        if (customer == null || customer.BillingAddressId.HasValue)
            return;

        var existingAddresses = await _customerService.GetAddressesByCustomerIdAsync(customer.Id);
        var existingValidAddress = existingAddresses.FirstOrDefault(address =>
            !string.IsNullOrWhiteSpace(address.FirstName) &&
            !string.IsNullOrWhiteSpace(address.LastName) &&
            !string.IsNullOrWhiteSpace(address.Email));

        if (existingValidAddress != null)
        {
            customer.BillingAddressId = existingValidAddress.Id;
            await _customerService.UpdateCustomerAsync(customer);
            return;
        }

        var address = new Address
        {
            FirstName = string.IsNullOrWhiteSpace(customer.FirstName) ? "Customer" : customer.FirstName,
            LastName = string.IsNullOrWhiteSpace(customer.LastName) ? "User" : customer.LastName,
            Email = customer.Email,
            CreatedOnUtc = DateTime.UtcNow
        };

        await _addressService.InsertAddressAsync(address);
        await _customerService.InsertCustomerAddressAsync(customer, address);

        customer.BillingAddressId = address.Id;
        await _customerService.UpdateCustomerAsync(customer);
    }
}
