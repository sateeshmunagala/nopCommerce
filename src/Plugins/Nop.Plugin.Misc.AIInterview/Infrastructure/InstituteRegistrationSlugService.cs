using Microsoft.Extensions.Logging;
using Nop.Services.Vendors;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public static class InstituteRegistrationSlugService
{
    public static string BuildSlug(string vendorName)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
            return string.Empty;

        var slug = vendorName.Trim().ToLowerInvariant().Replace(' ', '-');
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);
        slug = System.Text.RegularExpressions.Regex.Replace(slug, "-{2,}", "-");
        return slug.Trim('-');
    }

    public static async Task<int> ResolveVendorIdAsync(
        IVendorService vendorService,
        string registrationValue,
        ILogger logger,
        string context)
    {
        if (string.IsNullOrWhiteSpace(registrationValue) || vendorService == null)
            return 0;

        if (TryParseLegacyRegistrationValue(registrationValue, out var legacySlug, out var legacyVendorId))
        {
            var legacyVendor = await vendorService.GetVendorByIdAsync(legacyVendorId);
            if (legacyVendor != null &&
                legacyVendor.Active &&
                !legacyVendor.Deleted &&
                string.Equals(BuildSlug(legacyVendor.Name), legacySlug, StringComparison.OrdinalIgnoreCase))
            {
                return legacyVendor.Id;
            }

            logger?.LogWarning(
                "Legacy institute registration value could not be resolved to a matching active vendor. Value={RegistrationValue}; Context={Context}",
                registrationValue,
                context ?? string.Empty);

            return 0;
        }

        var slug = NormalizeRegistrationValue(registrationValue);
        if (string.IsNullOrWhiteSpace(slug))
            return 0;

        var vendors = await vendorService.GetAllVendorsAsync(showHidden: false);
        var matches = vendors
            .Where(vendor => string.Equals(BuildSlug(vendor.Name), slug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(vendor => vendor.Id)
            .ToList();

        if (matches.Count == 1)
            return matches[0].Id;

        if (matches.Count > 1)
        {
            logger?.LogWarning(
                "Institute registration slug resolution failed due to duplicate active vendor slugs. Slug={Slug}; VendorIds={VendorIds}; Context={Context}",
                slug,
                string.Join(",", matches.Select(vendor => vendor.Id)),
                context ?? string.Empty);
        }

        return 0;
    }

    public static async Task<bool> IsSlugUniqueForVendorAsync(
        IVendorService vendorService,
        string slug,
        int vendorId,
        ILogger logger,
        string context)
    {
        slug = NormalizeRegistrationValue(slug);
        if (string.IsNullOrWhiteSpace(slug) || vendorService == null || vendorId <= 0)
            return false;

        var vendors = await vendorService.GetAllVendorsAsync(showHidden: false);
        var matches = vendors
            .Where(vendor => string.Equals(BuildSlug(vendor.Name), slug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(vendor => vendor.Id)
            .ToList();

        if (matches.Count == 1 && matches[0].Id == vendorId)
            return true;

        logger?.LogWarning(
            "Institute registration link generation skipped because slug is not uniquely mapped to the current active vendor. Slug={Slug}; CurrentVendorId={CurrentVendorId}; MatchingVendorIds={VendorIds}; Context={Context}",
            slug,
            vendorId,
            string.Join(",", matches.Select(vendor => vendor.Id)),
            context ?? string.Empty);

        return false;
    }

    public static string NormalizeRegistrationValue(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static bool TryParseLegacyRegistrationValue(string value, out string slug, out int vendorId)
    {
        slug = string.Empty;
        vendorId = 0;

        var parts = (value ?? string.Empty).Split(':', 2);
        if (parts.Length != 2 ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out vendorId) ||
            vendorId <= 0)
        {
            return false;
        }

        slug = NormalizeRegistrationValue(parts[0]);
        return !string.IsNullOrWhiteSpace(slug);
    }
}
