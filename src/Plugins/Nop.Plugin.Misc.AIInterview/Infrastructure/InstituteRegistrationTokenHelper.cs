using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public static class InstituteRegistrationTokenHelper
{
    private const string Purpose = "Nop.Plugin.Misc.AIInterview.InstituteRegistration.v1";
    private const string Version = "v1";
    private const int TokenLifetimeDays = 90;

    public static IDataProtector CreateProtector(IDataProtectionProvider dataProtectionProvider)
    {
        return dataProtectionProvider?.CreateProtector(Purpose);
    }

    public static string CreateToken(IDataProtector protector, int vendorId, DateTime utcNow)
    {
        if (protector == null || vendorId <= 0)
            return string.Empty;

        var expiresUtcTicks = utcNow.AddDays(TokenLifetimeDays).Ticks;
        var payload = string.Join("|",
            Version,
            vendorId.ToString(CultureInfo.InvariantCulture),
            expiresUtcTicks.ToString(CultureInfo.InvariantCulture));

        return protector.Protect(payload);
    }

    public static bool TryResolveVendorId(IDataProtector protector, string value, DateTime utcNow, out int vendorId)
    {
        vendorId = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (TryResolveLegacyVendorId(value, out vendorId))
            return true;

        if (protector == null)
            return false;

        var protectedValue = value.Trim();
        var tokenSeparatorIndex = protectedValue.IndexOf('.');
        if (tokenSeparatorIndex >= 0 && tokenSeparatorIndex < protectedValue.Length - 1)
            protectedValue = protectedValue[(tokenSeparatorIndex + 1)..];

        string payload;
        try
        {
            payload = protector.Unprotect(protectedValue);
        }
        catch
        {
            return false;
        }

        var parts = payload.Split('|', 3);
        if (parts.Length != 3 ||
            !string.Equals(parts[0], Version, StringComparison.Ordinal) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out vendorId) ||
            vendorId <= 0 ||
            !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresUtcTicks))
        {
            vendorId = 0;
            return false;
        }

        if (expiresUtcTicks < utcNow.Ticks)
        {
            vendorId = 0;
            return false;
        }

        return true;
    }

    private static bool TryResolveLegacyVendorId(string value, out int vendorId)
    {
        vendorId = 0;
        var parts = value.Split(':', 2);
        return parts.Length == 2 &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out vendorId) &&
            vendorId > 0;
    }
}
