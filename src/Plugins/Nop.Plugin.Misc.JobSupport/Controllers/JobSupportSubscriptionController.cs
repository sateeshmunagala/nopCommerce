using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.JobSupport.Factories;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.JobSupport.Controllers;

[Authorize]
public class JobSupportSubscriptionController : BasePluginController
{
    private const string VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/Account/Subscription.cshtml";
    private readonly IJobSupportAccountModelFactory _modelFactory;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;

    public JobSupportSubscriptionController(IJobSupportAccountModelFactory modelFactory,
        IStoreContext storeContext,
        IWorkContext workContext)
    {
        _modelFactory = modelFactory;
        _storeContext = storeContext;
        _workContext = workContext;
    }

    public async Task<IActionResult> Index() => View(VIEW_PATH,
        await _modelFactory.PrepareSubscriptionAsync(await _workContext.GetCurrentCustomerAsync(),
            (await _storeContext.GetCurrentStoreAsync()).Id));
}
