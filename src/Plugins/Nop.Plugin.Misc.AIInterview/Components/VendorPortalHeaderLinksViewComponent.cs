using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Customers;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class VendorPortalHeaderLinksViewComponent : NopViewComponent
{
    private readonly IWorkContext _workContext;
    private readonly ICreditService _creditService;
    private readonly ICustomerService _customerService;

    public VendorPortalHeaderLinksViewComponent(
        IWorkContext workContext,
        ICreditService creditService,
        ICustomerService customerService)
    {
        _workContext = workContext;
        _creditService = creditService;
        _customerService = customerService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null || await _customerService.IsGuestAsync(customer))
            return Content(string.Empty);

        var wallet = await _creditService.GetOrCreateWalletAsync(customer.Id);
        var showName = HttpContext.Items.ContainsKey(AIInterviewDefaults.IsVendorPortalPageKey);
        var model = new VendorPortalHeaderLinksModel
        {
            FirstName = customer.FirstName ?? string.Empty,
            LastName = customer.LastName ?? string.Empty,
            Balance = wallet.Balance,
            ShowName = showName
        };

        return View(
            "~/Plugins/Misc.AIInterview/Views/Shared/Components/VendorPortalHeaderLinks/Default.cshtml",
            model);
    }
}

public record VendorPortalHeaderLinksModel
{
    public string FirstName { get; init; }
    public string LastName { get; init; }
    public decimal Balance { get; init; }
    public bool ShowName { get; init; }
}
