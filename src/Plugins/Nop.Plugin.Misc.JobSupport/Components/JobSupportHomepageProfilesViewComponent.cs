using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Factories;
using Nop.Plugin.Misc.JobSupport.Models.Public;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Services.Customers;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.JobSupport.Components;

public class JobSupportHomepageProfilesViewComponent : NopViewComponent
{
    private readonly ICustomerService _customerService;
    private readonly IJobSupportProfileModelFactory _modelFactory;
    private readonly IJobSupportProfileQueryService _queryService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;
    private readonly JobSupportSettings _settings;

    public JobSupportHomepageProfilesViewComponent(ICustomerService customerService,
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

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!_settings.Enabled)
            return Content(string.Empty);
        var customer = await _workContext.GetCurrentCustomerAsync();
        var result = await _queryService.SearchProfilesAsync(new ProfileSearchRequest
        {
            CustomerId = customer.Id,
            StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
            PageIndex = 0,
            PageSize = Math.Max(1, _settings.HomepageProfileCount)
        });
        if (!result.Succeeded || !result.Items.Any())
            return Content(string.Empty);
        var model = new ProfileListModel { QuerySucceeded = true, TotalRecords = result.TotalRecords };
        var isGuest = await _customerService.IsGuestAsync(customer);
        foreach (var item in result.Items.Where(item => item.Id != customer.VendorId))
            model.Profiles.Add(await _modelFactory.PrepareProfileCardAsync(item, customer, isGuest));
        if (!model.Profiles.Any())
            return Content(string.Empty);
        return View("~/Plugins/Misc.JobSupport/Views/Shared/Components/JobSupportHomepageProfiles/Default.cshtml", model);
    }
}
