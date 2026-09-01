using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Factories;
using Nop.Plugin.Misc.JobSupport.Models.Public;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Services.Customers;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.JobSupport.Components;

public class JobSupportProfileCardViewComponent : NopViewComponent
{
    private const string VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/Shared/Components/JobSupportProfileCard/Default.cshtml";
    private readonly ICustomerService _customerService;
    private readonly IJobSupportProfileModelFactory _modelFactory;
    private readonly IJobSupportProfileQueryService _queryService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;
    private readonly JobSupportSettings _settings;

    public JobSupportProfileCardViewComponent(ICustomerService customerService,
        IJobSupportProfileModelFactory modelFactory,
        IJobSupportProfileQueryService queryService,
        IStoreContext storeContext,
        IWorkContext workContext,
        JobSupportSettings settings)
    {
        _customerService = customerService;
        _modelFactory = modelFactory;
        _queryService = queryService;
        _storeContext = storeContext;
        _workContext = workContext;
        _settings = settings;
    }

    public async Task<IViewComponentResult> InvokeAsync(ProfileCardModel model = null)
    {
        if (!_settings.Enabled)
            return Content(string.Empty);
        if (model == null)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var result = await _queryService.SearchProfilesAsync(new ProfileSearchRequest
            {
                CustomerId = customer.Id,
                StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
                PageIndex = 0,
                PageSize = 1
            });
            var item = result.Items.FirstOrDefault(row => row.Id != customer.VendorId);
            if (item == null)
                return Content(string.Empty);
            model = await _modelFactory.PrepareProfileCardAsync(item, customer,
                await _customerService.IsGuestAsync(customer));
        }
        return View(VIEW_PATH, model);
    }
}
