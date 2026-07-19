using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Security;
using Nop.Services.Catalog;
using Nop.Services.Security;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class JobProductAccessService : IJobProductAccessService
{
    private readonly CatalogSettings _catalogSettings;
    private readonly IAclService _aclService;
    private readonly IPermissionService _permissionService;
    private readonly IProductService _productService;
    private readonly IStoreMappingService _storeMappingService;

    public JobProductAccessService(CatalogSettings catalogSettings,
        IAclService aclService,
        IPermissionService permissionService,
        IProductService productService,
        IStoreMappingService storeMappingService)
    {
        _catalogSettings = catalogSettings;
        _aclService = aclService;
        _permissionService = permissionService;
        _productService = productService;
        _storeMappingService = storeMappingService;
    }

    public async Task<bool> CanViewJobProductAsync(int productId, bool allowAdminPreview = false)
    {
        var product = productId > 0 ? await _productService.GetProductByIdAsync(productId) : null;
        return await CanViewJobProductAsync(product, allowAdminPreview);
    }

    public async Task<bool> CanViewJobProductAsync(Product product, bool allowAdminPreview = false)
    {
        if (product == null || product.Deleted)
            return false;

        var notAvailable =
            (!product.Published && !_catalogSettings.AllowViewUnpublishedProductPage) ||
            !await _aclService.AuthorizeAsync(product) ||
            !await _storeMappingService.AuthorizeAsync(product) ||
            !_productService.ProductIsAvailable(product);

        if (!notAvailable)
            return true;

        return allowAdminPreview && await HasAdminPreviewAccessAsync();
    }

    public async Task<bool> CanAcceptJobApplicationsAsync(Product product)
    {
        if (product == null || product.Deleted || !product.Published)
            return false;

        return await _aclService.AuthorizeAsync(product) &&
               await _storeMappingService.AuthorizeAsync(product) &&
               _productService.ProductIsAvailable(product);
    }

    public async Task<bool> CanAppearInListingsAsync(Product product, bool allowAdminPreview = false)
    {
        if (await CanAcceptJobApplicationsAsync(product))
            return true;

        return allowAdminPreview &&
               product != null &&
               !product.Deleted &&
               await HasAdminPreviewAccessAsync();
    }

    protected virtual async Task<bool> HasAdminPreviewAccessAsync()
    {
        return await _permissionService.AuthorizeAsync(StandardPermission.Security.ACCESS_ADMIN_PANEL) &&
               await _permissionService.AuthorizeAsync(StandardPermission.Catalog.PRODUCTS_VIEW);
    }
}
