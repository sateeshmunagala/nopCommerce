using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Booking;
using Nop.Services.Booking;
using Nop.Services.Configuration;
using Nop.Core;
using Nop.Web.Framework.Mvc;
using Microsoft.AspNetCore.Mvc;

namespace Nop.Web.Areas.Booking.Controllers
{
[Area("Booking")]
public class GoogleController : Nop.Web.Controllers.BasePublicController
{
    private readonly IBookingService _bookingService;
    private readonly ISettingService _settingService;
    private readonly IWorkContext _workContext;

    public GoogleController(IBookingService bookingService, ISettingService settingService, IWorkContext workContext)
    {
        _bookingService = bookingService;
        _settingService = settingService;
        _workContext = workContext;
    }

    // GET: /booking/google/start
    public IActionResult Start()
    {
        // Build Google OAuth URL using BookingSettings
        var settings = _settingService.LoadSettingAsync<BookingSettings>(0).GetAwaiter().GetResult();
        if (settings == null || string.IsNullOrEmpty(settings.GoogleClientId))
            return BadRequest("Google client id not configured.");

        var redirectUri = Url.Action("Callback", "Google", new { area = "Booking" }, Request.Scheme);
        var state = Guid.NewGuid().ToString("N");
        var url = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(settings.GoogleClientId)}&response_type=code&scope={Uri.EscapeDataString("openid email profile https://www.googleapis.com/auth/calendar.events")}&redirect_uri={Uri.EscapeDataString(redirectUri)}&access_type=offline&prompt=consent&state={state}";
        return Redirect(url);
    }

    // GET: /booking/google/callback
    private readonly IGoogleCalendarService _googleCalendarService;

    public GoogleController(IBookingService bookingService, ISettingService settingService, IWorkContext workContext, IGoogleCalendarService googleCalendarService)
    {
        _bookingService = bookingService;
        _settingService = settingService;
        _workContext = workContext;
        _googleCalendarService = googleCalendarService;
    }

    public async Task<IActionResult> Callback(string code, string state, string error)
    {
        if (!string.IsNullOrEmpty(error))
            return BadRequest(error);

        if (string.IsNullOrEmpty(code))
            return BadRequest("Code missing");

        // For POC: exchange code for tokens via HttpClient inside service would be better, but keep minimal
        var settings = await _settingService.LoadSettingAsync<BookingSettings>(0);
        if (settings == null || string.IsNullOrEmpty(settings.GoogleClientSecret))
            return BadRequest("Google client secret not configured.");

        var vendor = await _workContext.GetCurrentVendorAsync();
        var vendorId = vendor?.Id ?? 0;
        if (vendorId == 0)
            return Forbid();

        var redirectUri = Url.Action("Callback", "Google", new { area = "Booking" }, Request.Scheme);
        var (accessToken, refreshToken, email, expiryUtc) = await _googleCalendarService.ExchangeCodeForTokensAsync(code, redirectUri, settings.GoogleClientId, settings.GoogleClientSecret);

        var token = new BookingIntegrationToken
        {
            VendorId = vendorId,
            GoogleAccountEmail = email,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenExpiryUtc = expiryUtc,
            IsActive = true,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow
        };

        await _bookingService.SaveTokenAsync(token);

        return RedirectToAction("Info", "Vendor");
    }

    // GET: /booking/google/disconnect
    public async Task<IActionResult> Disconnect()
    {
        var vendor = await _workContext.GetCurrentVendorAsync();
        var vendorId = vendor?.Id ?? 0;
        if (vendorId == 0)
            return Forbid();

        var existing = await _bookingService.GetTokenByVendorIdAsync(vendorId);
        if (existing != null)
            await _bookingService.DeleteTokenAsync(existing.Id);

        return RedirectToAction("VendorInfo", "Customer", new { area = "" });
}
    }
}
