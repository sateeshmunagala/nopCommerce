using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Plugin.Misc.AppointmentBooking.Domains;
using Nop.Services.Cms;
using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Services.Security;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Misc.AppointmentBooking.Services;

/// <summary>
/// Represents appointment booking event consumer
/// </summary>
public class EventConsumer : IConsumer<AdminMenuCreatedEvent>,
    IConsumer<ShoppingCartItemMovedToOrderItemEvent>,
    IConsumer<OrderPaidEvent>,
    IConsumer<OrderStatusChangedEvent>
{
    #region Fields

    private readonly IAppointmentBookingService _appointmentBookingService;
    private readonly ILocalizationService _localizationService;
    private readonly IWidgetPluginManager _pluginManager;

    #endregion

    #region Ctor

    public EventConsumer(IAppointmentBookingService appointmentBookingService,
        ILocalizationService localizationService,
        IWidgetPluginManager pluginManager)
    {
        _appointmentBookingService = appointmentBookingService;
        _localizationService = localizationService;
        _pluginManager = pluginManager;
    }

    #endregion

    #region Methods

    public async Task HandleEventAsync(AdminMenuCreatedEvent eventMessage)
    {
        var plugin = await _pluginManager.LoadPluginBySystemNameAsync(AppointmentBookingDefaults.SystemName);
        if (plugin == null || !_pluginManager.IsPluginActive(plugin))
            return;

        if (eventMessage.RootMenuItem.ContainsSystemName(AppointmentBookingDefaults.AppointmentsAdminMenuSystemName))
            return;

        eventMessage.RootMenuItem.ChildNodes.Add(new AdminMenuItem
        {
            Visible = true,
            SystemName = AppointmentBookingDefaults.AppointmentsAdminMenuSystemName,
            Title = await _localizationService.GetResourceAsync("Plugins.Misc.AppointmentBooking.Appointments"),
            IconClass = "far fa-calendar-alt",
            ChildNodes = new List<AdminMenuItem>
            {
                new()
                {
                    Visible = true,
                    SystemName = AppointmentBookingDefaults.ServicesAdminMenuSystemName,
                    Title = await _localizationService.GetResourceAsync("Plugins.Misc.AppointmentBooking.Services"),
                    Url = eventMessage.GetMenuItemUrl("AppointmentBooking", "Services"),
                    IconClass = "far fa-calendar-alt",
                    PermissionNames = new List<string> { StandardPermission.Configuration.MANAGE_PLUGINS }
                },
                new()
                {
                    Visible = true,
                    SystemName = AppointmentBookingDefaults.BookingsAdminMenuSystemName,
                    Title = await _localizationService.GetResourceAsync("Plugins.Misc.AppointmentBooking.Bookings"),
                    Url = eventMessage.GetMenuItemUrl("AppointmentBooking", "Bookings"),
                    IconClass = "far fa-calendar-check",
                    PermissionNames = new List<string> { StandardPermission.Configuration.MANAGE_PLUGINS }
                }
            }
        });
    }

    public async Task HandleEventAsync(ShoppingCartItemMovedToOrderItemEvent eventMessage)
    {
        var hold = await _appointmentBookingService.GetActiveHoldForCustomerProductAsync(eventMessage.ShoppingCartItem.CustomerId, eventMessage.OrderItem.ProductId);
        if (hold == null)
            return;

        var order = new Order { Id = eventMessage.OrderItem.OrderId, CustomerId = eventMessage.ShoppingCartItem.CustomerId };
        await _appointmentBookingService.ConvertHoldToBookingAsync(hold, order, eventMessage.OrderItem);
    }

    public async Task HandleEventAsync(OrderPaidEvent eventMessage)
    {
        await _appointmentBookingService.ConfirmBookingsForOrderAsync(eventMessage.Order);
        await _appointmentBookingService.ReleaseExpiredHoldsAsync();
    }

    public async Task HandleEventAsync(OrderStatusChangedEvent eventMessage)
    {
        if (eventMessage.Order.OrderStatus != OrderStatus.Cancelled)
            return;

        var bookings = await _appointmentBookingService.GetAllBookingsAsync();
        foreach (var booking in bookings.Where(booking => booking.OrderId == eventMessage.Order.Id && booking.Status != BookingStatus.Cancelled))
            await _appointmentBookingService.CancelBookingAsync(booking.Id, "Order was cancelled.");
    }

    #endregion
}
