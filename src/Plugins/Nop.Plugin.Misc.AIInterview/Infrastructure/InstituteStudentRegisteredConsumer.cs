using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Services.Common;
using Nop.Services.Events;
using Nop.Web.Framework.Events;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class InstituteStudentRegisteredConsumer : IConsumer<CustomerRegisteredEvent>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IGenericAttributeService _genericAttributeService;

    public InstituteStudentRegisteredConsumer(
        IHttpContextAccessor httpContextAccessor,
        IGenericAttributeService genericAttributeService)
    {
        _httpContextAccessor = httpContextAccessor;
        _genericAttributeService = genericAttributeService;
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

        var parts = cookieValue.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var vendorId) || vendorId <= 0)
            return;

        await _genericAttributeService.SaveAttributeAsync(
            customer,
            AIInterviewDefaults.InstituteVendorIdAttributeKey,
            vendorId);

        httpContext.Response.Cookies.Delete(
            AIInterviewDefaults.InstituteRegistrationCookieName);
    }
}
