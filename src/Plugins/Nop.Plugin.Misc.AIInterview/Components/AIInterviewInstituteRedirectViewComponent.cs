using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
using Nop.Services.Customers;
using Nop.Services.Vendors;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class AIInterviewInstituteRedirectViewComponent : NopViewComponent
{
    private readonly IWorkContext _workContext;
    private readonly ICustomerService _customerService;
    private readonly IVendorService _vendorService;
    private readonly AIInterviewSettings _settings;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AIInterviewInstituteRedirectViewComponent> _logger;

    public AIInterviewInstituteRedirectViewComponent(
        IWorkContext workContext,
        ICustomerService customerService,
        IVendorService vendorService,
        AIInterviewSettings settings,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AIInterviewInstituteRedirectViewComponent> logger)
    {
        _workContext = workContext;
        _customerService = customerService;
        _vendorService = vendorService;
        _settings = settings;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        var instParam = httpContext?.Request.Query[AIInterviewDefaults.InstituteRegistrationCookieName]
            .FirstOrDefault();
        if (await CanResolveInstituteRegistrationValueAsync(instParam))
        {
            httpContext.Response.Cookies.Append(
                AIInterviewDefaults.InstituteRegistrationCookieName,
                instParam,
                new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = true,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
        }

        if (!_settings.Enabled)
            return Content(string.Empty);

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null || customer.VendorId <= 0)
            return Content(string.Empty);

        var isInstitute = await _customerService.IsInCustomerRoleAsync(
            customer, "Institute", true);

        if (!isInstitute)
            return Content(string.Empty);

        return View(
            "~/Plugins/Misc.AIInterview/Views/Shared/Components/" +
            "AIInterviewInstituteRedirect/Default.cshtml");
    }

    protected virtual async Task<bool> CanResolveInstituteRegistrationValueAsync(string registrationValue)
    {
        return await InstituteRegistrationSlugService.ResolveVendorIdAsync(
            _vendorService,
            registrationValue,
            _logger,
            nameof(AIInterviewInstituteRedirectViewComponent)) > 0;
    }
}
