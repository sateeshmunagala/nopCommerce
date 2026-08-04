using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<InstituteStudentRegisteredConsumer> _logger;

    public InstituteStudentRegisteredConsumer(
        IHttpContextAccessor httpContextAccessor,
        IGenericAttributeService genericAttributeService,
        IVendorService vendorService,
        ILogger<InstituteStudentRegisteredConsumer> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _genericAttributeService = genericAttributeService;
        _vendorService = vendorService;
        _logger = logger;
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
        return await InstituteRegistrationSlugService.ResolveVendorIdAsync(
            _vendorService,
            registrationValue,
            _logger,
            nameof(InstituteStudentRegisteredConsumer));
    }
}
