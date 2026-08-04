using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
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
    private readonly IDataProtector _instituteRegistrationProtector;

    public InstituteStudentRegisteredConsumer(
        IHttpContextAccessor httpContextAccessor,
        IGenericAttributeService genericAttributeService,
        IDataProtectionProvider dataProtectionProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _genericAttributeService = genericAttributeService;
        _instituteRegistrationProtector = InstituteRegistrationTokenHelper.CreateProtector(dataProtectionProvider);
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

        if (!InstituteRegistrationTokenHelper.TryResolveVendorId(
            _instituteRegistrationProtector,
            cookieValue,
            DateTime.UtcNow,
            out var vendorId))
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
}
