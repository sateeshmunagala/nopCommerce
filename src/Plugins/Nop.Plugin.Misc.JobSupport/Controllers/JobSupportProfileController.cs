using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Factories;
using Nop.Plugin.Misc.JobSupport.Models.Public;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Seo;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.JobSupport.Controllers;

public class JobSupportProfileController : BasePluginController
{
    private const string LIST_VIEW = "~/Plugins/Misc.JobSupport/Views/Profile/List.cshtml";
    private const string DETAIL_VIEW = "~/Plugins/Misc.JobSupport/Views/Profile/Detail.cshtml";
    private readonly ICustomerService _customerService;
    private readonly IJobSupportProfileModelFactory _modelFactory;
    private readonly IJobSupportProfileQueryService _queryService;
    private readonly IProductService _productService;
    private readonly IStoreContext _storeContext;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IUrlRecordService _urlRecordService;
    private readonly IWorkContext _workContext;
    private readonly JobSupportSettings _settings;

    public JobSupportProfileController(ICustomerService customerService,
        IJobSupportProfileModelFactory modelFactory,
        IJobSupportProfileQueryService queryService,
        IProductService productService,
        ISpecificationAttributeService specificationAttributeService,
        IStoreContext storeContext,
        IUrlRecordService urlRecordService,
        IWorkContext workContext,
        JobSupportSettings settings)
    {
        _customerService = customerService;
        _modelFactory = modelFactory;
        _queryService = queryService;
        _productService = productService;
        _specificationAttributeService = specificationAttributeService;
        _storeContext = storeContext;
        _urlRecordService = urlRecordService;
        _workContext = workContext;
        _settings = settings;
    }

    public async Task<IActionResult> List(ProfileFilterModel filter)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var isGuest = await _customerService.IsGuestAsync(customer);
        if (isGuest && !_settings.AllowGuestProfileBrowsing)
            return Challenge();

        filter.PageNumber = Math.Max(1, filter.PageNumber);
        var request = new ProfileSearchRequest
        {
            CustomerId = customer.Id,
            StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
            ProfileTypeId = filter.ProfileTypeId ?? await GetOppositeProfileTypeIdAsync(customer),
            PageIndex = filter.PageNumber - 1,
            PageSize = Math.Max(1, _settings.DefaultPageSize),
            SortOrder = filter.SortOrder
        };
        var result = await _queryService.SearchProfilesAsync(request);
        return View(LIST_VIEW, await _modelFactory.PrepareProfileListAsync(filter, result, customer, isGuest));
    }

    public async Task<IActionResult> Detail(string slug)
    {
        var urlRecord = await _urlRecordService.GetBySlugAsync(slug);
        if (urlRecord == null || !urlRecord.IsActive || !urlRecord.EntityName.Equals(nameof(Nop.Core.Domain.Catalog.Product), StringComparison.OrdinalIgnoreCase))
            return NotFound();
        var profile = await _productService.GetProductByIdAsync(urlRecord.EntityId);
        if (profile == null || profile.Deleted || !profile.Published)
            return NotFound();
        var customer = await _workContext.GetCurrentCustomerAsync();
        var isGuest = await _customerService.IsGuestAsync(customer);
        if (isGuest && !_settings.AllowGuestProfileBrowsing)
            return Challenge();

        var result = await _queryService.SearchProfilesAsync(new ProfileSearchRequest
        {
            ProductIds = new[] { profile.Id },
            CustomerId = customer.Id,
            StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
            PageIndex = 0,
            PageSize = 1
        });
        var row = result.Items.FirstOrDefault(item => item.Id == profile.Id);
        if (row == null)
            return NotFound();
        return View(DETAIL_VIEW, await _modelFactory.PrepareProfileDetailAsync(profile, row, customer, isGuest,
            $"{Request.PathBase}{Request.Path}{Request.QueryString}"));
    }

    private async Task<int?> GetOppositeProfileTypeIdAsync(Nop.Core.Domain.Customers.Customer customer)
    {
        if (customer.CustomerProfileTypeId <= 0 || _settings.ProfileTypeSpecificationAttributeId <= 0)
            return null;
        var options = await _specificationAttributeService
            .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(_settings.ProfileTypeSpecificationAttributeId);
        return options.FirstOrDefault(option => option.Id != customer.CustomerProfileTypeId)?.Id;
    }
}
