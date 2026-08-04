namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public static class InstituteRegistrationSlugHelper
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

    public static string NormalizeRegistrationValue(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    public static bool TryResolveLegacyVendorId(string value, out int vendorId)
    {
        vendorId = 0;
        var parts = (value ?? string.Empty).Split(':', 2);
        return parts.Length == 2 &&
            int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out vendorId) &&
            vendorId > 0;
    }
}
