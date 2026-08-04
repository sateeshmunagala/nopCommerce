using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Customers;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class AIInterviewInstituteRedirectViewComponent : NopViewComponent
{
    private readonly IWorkContext _workContext;
    private readonly ICustomerService _customerService;
    private readonly AIInterviewSettings _settings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AIInterviewInstituteRedirectViewComponent(
        IWorkContext workContext,
        ICustomerService customerService,
        AIInterviewSettings settings,
        IHttpContextAccessor httpContextAccessor)
    {
        _workContext = workContext;
        _customerService = customerService;
        _settings = settings;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        var instParam = httpContext?.Request.Query[AIInterviewDefaults.InstituteRegistrationCookieName]
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(instParam) && instParam.Contains(':'))
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
}
