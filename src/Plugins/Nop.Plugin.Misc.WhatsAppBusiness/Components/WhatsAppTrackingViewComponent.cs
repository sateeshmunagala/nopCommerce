using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Services.Common;
using Nop.Services.Orders;
using Nop.Services.Shipping;
using Nop.Plugin.Misc.WhatsAppBusiness.Domain;
using Nop.Plugin.Misc.WhatsAppBusiness.Models;
using Nop.Plugin.Misc.WhatsAppBusiness.Services;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Components;

[ViewComponent(Name = "WhatsAppTracking")]
public class WhatsAppTrackingViewComponent : ViewComponent
{
	private readonly IWorkContext _workContext;

	private readonly IOrderService _orderService;

	private readonly IShipmentService _shipmentService;

	private readonly IGenericAttributeService _genericAttributeService;

	private readonly IWhatsAppBusinessService _whatsAppService;

	private readonly WhatsAppBusinessSettings _settings;

	public WhatsAppTrackingViewComponent(IWorkContext workContext, IOrderService orderService, IShipmentService shipmentService, IGenericAttributeService genericAttributeService, IWhatsAppBusinessService whatsAppService, WhatsAppBusinessSettings settings)
	{
		_workContext = workContext;
		_orderService = orderService;
		_shipmentService = shipmentService;
		_genericAttributeService = genericAttributeService;
		_whatsAppService = whatsAppService;
		_settings = settings;
	}

	public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
	{
		if (!_settings.IsEnabled || !_settings.ShowTrackingOnOrderDetails)
		{
			return Content(string.Empty);
		}
		Customer customer = await _workContext.GetCurrentCustomerAsync();
		if (customer == null)
		{
			return Content(string.Empty);
		}
		if (!(await _genericAttributeService.GetAttributeAsync<bool>((BaseEntity)(object)customer, WhatsAppBusinessDefaults.CustomerOptInAttribute, 0, false)))
		{
			return Content(string.Empty);
		}
		int orderId = GetIntProperty(additionalData, "Id");
		if (orderId == 0)
		{
			return Content(string.Empty);
		}
		Order order = await _orderService.GetOrderByIdAsync(orderId);
		if (order == null || order.CustomerId != ((BaseEntity)customer).Id)
		{
			return Content(string.Empty);
		}
		Shipment? shipment = (await _shipmentService.GetShipmentsByOrderIdAsync(((BaseEntity)order).Id, (bool?)null, (bool?)null, 0)).FirstOrDefault();
		string phone = (await _genericAttributeService.GetAttributeAsync<string>((BaseEntity)(object)customer, WhatsAppBusinessDefaults.CustomerPhoneAttribute, 0)) ?? "";
		string? trackingNumber = shipment?.TrackingNumber;
		string carrierName = order.ShippingMethod ?? "";
		string? trackingUrl = BuildTrackingUrl(carrierName, trackingNumber);
		int currentStep = order.OrderStatus switch
		{
			OrderStatus.Pending => 0,
			OrderStatus.Complete => 3,
			OrderStatus.Processing when shipment?.ShippedDateUtc.HasValue == true && !shipment.DeliveryDateUtc.HasValue => 2,
			_ => 1
		};
		IList<WhatsAppMessageLog> notifications = await _whatsAppService.GetOrderLogsAsync(orderId);
		TrackingWidgetModel model = new TrackingWidgetModel
		{
			OrderId = orderId,
			OrderNumber = order.CustomOrderNumber,
			OrderStatus = order.OrderStatus.ToString(),
			CarrierName = carrierName,
			TrackingNumber = trackingNumber,
			TrackingUrl = trackingUrl,
			CustomerPhone = phone,
			IsOptedIn = true,
			CurrentStep = currentStep,
			Notifications = notifications
		};
		return View("~/Plugins/Misc.WhatsAppBusiness/Views/Components/WhatsAppTracking/Default.cshtml", model);
	}

	private string? BuildTrackingUrl(string carrierName, string? trackingNumber)
	{
		if (string.IsNullOrWhiteSpace(trackingNumber))
		{
			return null;
		}
		if (!string.IsNullOrWhiteSpace(_settings.CarrierTrackingUrls))
		{
			try
			{
				Dictionary<string, string>? dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(_settings.CarrierTrackingUrls);
				if (dictionary != null)
				{
					KeyValuePair<string, string> keyValuePair = dictionary.FirstOrDefault((KeyValuePair<string, string> c) => !string.IsNullOrWhiteSpace(carrierName) && carrierName.Contains(c.Key, StringComparison.OrdinalIgnoreCase));
					if (!string.IsNullOrWhiteSpace(keyValuePair.Value))
					{
						return keyValuePair.Value.Replace("{tracking}", trackingNumber);
					}
				}
			}
			catch
			{
			}
		}
		if (!string.IsNullOrWhiteSpace(_settings.DefaultTrackingUrlPattern))
		{
			return _settings.DefaultTrackingUrlPattern.Replace("{tracking}", trackingNumber);
		}
		return null;
	}

	private static int GetIntProperty(object source, string propertyName)
	{
		object? obj = source?.GetType().GetProperty(propertyName)?.GetValue(source);
		if (obj is int)
		{
			return (int)obj;
		}
		return 0;
	}
}
