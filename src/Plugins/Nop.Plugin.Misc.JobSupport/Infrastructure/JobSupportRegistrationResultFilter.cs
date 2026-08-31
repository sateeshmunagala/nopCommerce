using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Nop.Core;
using Nop.Services.Customers;

namespace Nop.Plugin.Misc.JobSupport.Infrastructure;

public class JobSupportRegistrationResultFilter : IAsyncResultFilter
{
    private readonly ICustomerService _customerService;
    private readonly IWorkContext _workContext;
    private readonly JobSupportSettings _settings;

    public JobSupportRegistrationResultFilter(ICustomerService customerService,
        IWorkContext workContext,
        JobSupportSettings settings)
    {
        _customerService = customerService;
        _workContext = workContext;
        _settings = settings;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (_settings.Enabled && _settings.EnableRegistrationWorkflow &&
            context.HttpContext.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            context.ActionDescriptor is ControllerActionDescriptor descriptor &&
            descriptor.ControllerName.Equals("Customer", StringComparison.OrdinalIgnoreCase) &&
            descriptor.ActionName.Equals("Register", StringComparison.OrdinalIgnoreCase) &&
            context.Result is RedirectToRouteResult or RedirectResult)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer != null && customer.Active && !customer.Deleted && !await _customerService.IsGuestAsync(customer))
                context.Result = new RedirectToRouteResult("Plugin.Misc.JobSupport.AccountProfile", null);
        }
        await next();
    }
}
