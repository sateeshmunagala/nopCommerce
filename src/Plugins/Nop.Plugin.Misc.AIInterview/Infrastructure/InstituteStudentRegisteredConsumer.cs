using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Services.Common;
using Nop.Services.Events;
using Nop.Services.Vendors;
using Nop.Web.Framework.Events;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class InstituteStudentRegisteredConsumer : IConsumer<CustomerRegisteredEvent>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IVendorService _vendorService;

    public InstituteStudentRegisteredConsumer(
        IHttpContextAccessor httpContextAccessor,
        IGenericAttributeService genericAttributeService,
        IVendorService vendorService)
    {
        _httpContextAccessor = httpContextAccessor;
        _genericAttributeService = genericAttributeService;
        _vendorService = vendorService;
    }

    public async Task HandleEventAsync(CustomerRegisteredEvent eventMessage)
    {
        var customer = eventMessage?.Customer;
        if (customer == null)
            return;

        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null)
            return;

        var cookieValue = httpContext.Request.Cookies[
            AIInterviewDefaults.InstituteRegistrationCookieName];
        if (string.IsNullOrWhiteSpace(cookieValue))
            return;

        var vendorId = await ResolveInstituteVendorIdAsync(cookieValue);
        if (vendorId <= 0)
        {
            return;
        }

        await _genericAttributeService.SaveAttributeAsync(
            customer,
            AIInterviewDefaults.InstituteVendorIdAttributeKey,
            vendorId);

        httpContext.Response.Cookies.Delete(
            AIInterviewDefaults.InstituteRegistrationCookieName);
    }

    protected virtual async Task<int> ResolveInstituteVendorIdAsync(string registrationValue)
    {
        if (InstituteRegistrationSlugHelper.TryResolveLegacyVendorId(registrationValue, out var legacyVendorId))
            return legacyVendorId;

        var slug = InstituteRegistrationSlugHelper.NormalizeRegistrationValue(registrationValue);
        if (string.IsNullOrWhiteSpace(slug) || _vendorService == null)
            return 0;

        var vendors = await _vendorService.GetAllVendorsAsync(showHidden: true);
        var vendor = vendors.FirstOrDefault(vendor =>
            string.Equals(InstituteRegistrationSlugHelper.BuildSlug(vendor.Name), slug, StringComparison.OrdinalIgnoreCase));

        return vendor?.Id ?? 0;
    }
}
