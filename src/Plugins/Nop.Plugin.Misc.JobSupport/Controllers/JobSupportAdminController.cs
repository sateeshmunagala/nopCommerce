using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Models.Admin;
using Nop.Plugin.Misc.JobSupport.Services;
using Nop.Services.Localization;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.JobSupport.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class JobSupportAdminController : BasePluginController
{
    private const string VIEW_PATH = "~/Plugins/Misc.JobSupport/Views/JobSupportAdmin/LegacyParity.cshtml";

    private readonly IJobSupportProfileQueryService _profileQueryService;
    private readonly ILocalizationService _localizationService;
    private readonly IPermissionService _permissionService;
    private readonly JobSupportSettings _settings;

    public JobSupportAdminController(IJobSupportProfileQueryService profileQueryService,
        ILocalizationService localizationService,
        IPermissionService permissionService,
        JobSupportSettings settings)
    {
        _profileQueryService = profileQueryService;
        _localizationService = localizationService;
        _permissionService = permissionService;
        _settings = settings;
    }

    public async Task<IActionResult> LegacyParity()
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        return View(VIEW_PATH, new LegacyParityRequestModel
        {
            PageSize = _settings.DefaultPageSize > 0 ? _settings.DefaultPageSize : 12
        });
    }

    [HttpPost]
    public async Task<IActionResult> LegacyParity(LegacyParityRequestModel model)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        if (!TryParseProductIds(model.ProductIdentifiers, out var productIds))
        {
            ModelState.AddModelError(nameof(model.ProductIdentifiers),
                await _localizationService.GetResourceAsync(
                    "Plugins.Misc.JobSupport.Admin.LegacyParity.Validation.ProductIds"));
        }

        if (!ModelState.IsValid)
            return View(VIEW_PATH, model);

        var request = new ProfileSearchRequest
        {
            ProductIds = productIds,
            CustomerId = model.CustomerId,
            ProfileTypeId = model.ProfileTypeId,
            RelationshipType = model.RelationshipType,
            PageIndex = model.PageIndex,
            PageSize = model.PageSize,
            SortOrder = model.SortOrder
        };

        var procedureName = model.QueryType == LegacyParityQueryType.Relationship
            ? _settings.LegacyShortlistProcedureName
            : _settings.LegacyProfileSearchProcedureName;

        var stopwatch = Stopwatch.StartNew();
        var result = model.QueryType == LegacyParityQueryType.Relationship
            ? await _profileQueryService.GetProfilesByRelationshipAsync(request)
            : await _profileQueryService.SearchProfilesAsync(request);
        stopwatch.Stop();

        model.Result = new LegacyParityResultModel
        {
            Diagnostic = new ProfileQueryDiagnosticResult
            {
                ProcedureName = procedureName,
                Succeeded = result.Succeeded,
                ReturnedRowCount = result.ReturnedRowCount,
                OutputTotalRecords = result.OutputTotalRecords,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                ProfileIds = result.Items.Select(item => item.Id).ToList(),
                MappingWarnings = result.MappingWarnings.ToList(),
                ErrorCode = result.ErrorCode
            },
            Profiles = result.Items.Select(item => new LegacyParityProfilePresenceModel
            {
                ProfileId = item.Id,
                HasPhone = !string.IsNullOrWhiteSpace(item.Phone),
                HasEmail = !string.IsNullOrWhiteSpace(item.Email)
            }).ToList()
        };

        return View(VIEW_PATH, model);
    }

    private static bool TryParseProductIds(string value, out IList<int> productIds)
    {
        productIds = new List<int>();
        if (string.IsNullOrWhiteSpace(value))
            return true;

        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, out var productId) || productId <= 0)
                return false;

            productIds.Add(productId);
        }

        return true;
    }
}
