using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.JobSupport.Factories;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.JobSupport.Components;

public class JobSupportSubscriptionViewComponent : NopViewComponent
{
    private readonly IJobSupportAccountModelFactory _modelFactory;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;
    private readonly JobSupportSettings _settings;

    public JobSupportSubscriptionViewComponent(IJobSupportAccountModelFactory modelFactory,
        IStoreContext storeContext,
        IWorkContext workContext,
        JobSupportSettings settings)
    {
        _modelFactory = modelFactory;
        _storeContext = storeContext;
        _workContext = workContext;
        _settings = settings;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!_settings.Enabled)
            return Content(string.Empty);
        return View("~/Plugins/Misc.JobSupport/Views/Shared/Components/JobSupportSubscription/Default.cshtml",
            await _modelFactory.PrepareSubscriptionAsync(await _workContext.GetCurrentCustomerAsync(),
                (await _storeContext.GetCurrentStoreAsync()).Id));
    }
}
