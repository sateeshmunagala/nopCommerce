using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Events;
using Nop.Services.Orders;
using Nop.Plugin.Misc.WhatsAppBusiness.Services;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Infrastructure;

public class EventConsumer : IConsumer<OrderPlacedEvent>, IConsumer<ShipmentSentEvent>, IConsumer<ShipmentDeliveredEvent>
{
	private readonly WhatsAppBusinessSettings _settings;

	private readonly IWhatsAppBusinessService _whatsAppService;

	private readonly IAddressService _addressService;

	private readonly IOrderService _orderService;

	private readonly ICustomerService _customerService;

	public EventConsumer(WhatsAppBusinessSettings settings, IWhatsAppBusinessService whatsAppService, IAddressService addressService, IOrderService orderService, ICustomerService customerService)
	{
		_settings = settings;
		_whatsAppService = whatsAppService;
		_addressService = addressService;
		_orderService = orderService;
		_customerService = customerService;
	}

	public async Task HandleEventAsync(OrderPlacedEvent eventMessage)
	{
		if (!_settings.IsEnabled || !_settings.EnableOrderPlaced)
		{
			return;
		}
		Order order = eventMessage.Order;
		string? phone = await GetPhoneAsync(order);
		if (phone != null && !(await _whatsAppService.IsBlacklistedAsync(phone)) && !(await _whatsAppService.HasBeenNotifiedAsync(((BaseEntity)order).Id, "OrderPlaced")))
		{
			Customer obj = await _customerService.GetCustomerByIdAsync(order.CustomerId);
			string? text = obj?.FirstName;
			if (_settings.UseTemplateMessages && !string.IsNullOrWhiteSpace(_settings.OrderConfirmationTemplateName))
			{
				List<string> bodyParameters = new List<string>
				{
					text ?? "Cliente",
					"#" + order.CustomOrderNumber,
					order.OrderTotal.ToString("N2")
				};
				await _whatsAppService.SendTemplateMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "OrderPlaced", _settings.OrderConfirmationTemplateName, _settings.DefaultLanguageCode, bodyParameters);
			}
			else
			{
				string messageBody = $"Your order #{order.CustomOrderNumber} has been confirmed! Total: {order.OrderTotal:N2}. Thank you!";
				await _whatsAppService.SendMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "OrderPlaced", messageBody);
			}
		}
	}

	public async Task HandleEventAsync(ShipmentSentEvent eventMessage)
	{
		if (!_settings.IsEnabled || !_settings.EnableShipmentCreated)
		{
			return;
		}
		Shipment shipment = eventMessage.Shipment;
		Order order = await _orderService.GetOrderByIdAsync(shipment.OrderId);
		if (order == null)
		{
			return;
		}
		string? phone = await GetPhoneAsync(order);
		if (phone == null || await _whatsAppService.IsBlacklistedAsync(phone) || await _whatsAppService.HasBeenNotifiedAsync(((BaseEntity)order).Id, "ShipmentCreated"))
		{
			return;
		}
		Customer obj = await _customerService.GetCustomerByIdAsync(order.CustomerId);
		string? text = obj?.FirstName;
		string? trackingNumber = shipment.TrackingNumber;
		string text2 = order.ShippingMethod ?? "";
		string? text3 = BuildTrackingUrl(text2, trackingNumber);
		if (_settings.UseTemplateMessages && !string.IsNullOrWhiteSpace(_settings.ShipmentTrackingTemplateName))
		{
			List<string> bodyParameters = new List<string>
			{
				text ?? "Cliente",
				"#" + order.CustomOrderNumber,
				text2,
				trackingNumber ?? "N/A",
				text3 ?? ""
			};
			await _whatsAppService.SendTemplateMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "ShipmentCreated", _settings.ShipmentTrackingTemplateName, _settings.DefaultLanguageCode, bodyParameters, trackingNumber);
			return;
		}
		string text4 = $"Your order #{order.CustomOrderNumber} has been shipped! Carrier: {text2}. Tracking: {trackingNumber ?? "N/A"}.";
		if (!string.IsNullOrWhiteSpace(text3))
		{
			text4 = text4 + " Track at: " + text3;
		}
		await _whatsAppService.SendMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "ShipmentCreated", text4, trackingNumber);
	}

	public async Task HandleEventAsync(ShipmentDeliveredEvent eventMessage)
	{
		if (!_settings.IsEnabled || !_settings.EnableShipmentDelivered)
		{
			return;
		}
		Shipment shipment = eventMessage.Shipment;
		Order order = await _orderService.GetOrderByIdAsync(shipment.OrderId);
		if (order == null)
		{
			return;
		}
		string? phone = await GetPhoneAsync(order);
		if (phone != null && !(await _whatsAppService.IsBlacklistedAsync(phone)) && !(await _whatsAppService.HasBeenNotifiedAsync(((BaseEntity)order).Id, "ShipmentDelivered")))
		{
			Customer obj = await _customerService.GetCustomerByIdAsync(order.CustomerId);
			string? text = obj?.FirstName;
			if (_settings.UseTemplateMessages && !string.IsNullOrWhiteSpace(_settings.DeliveryConfirmationTemplateName))
			{
				List<string> bodyParameters = new List<string>
				{
					text ?? "Cliente",
					"#" + order.CustomOrderNumber,
					shipment.DeliveryDateUtc?.ToString("dd/MM/yyyy") ?? "today"
				};
				await _whatsAppService.SendTemplateMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "ShipmentDelivered", _settings.DeliveryConfirmationTemplateName, _settings.DefaultLanguageCode, bodyParameters, shipment.TrackingNumber);
			}
			else
			{
				string messageBody = "Your order #" + order.CustomOrderNumber + " has been delivered! Thank you for shopping with us.";
				await _whatsAppService.SendMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "ShipmentDelivered", messageBody, shipment.TrackingNumber);
			}
		}
	}

	private async Task<string?> GetPhoneAsync(Order order)
	{
		Address address = await _addressService.GetAddressByIdAsync(order.BillingAddressId);
		if (string.IsNullOrWhiteSpace(address?.PhoneNumber))
			return null;

		return address.PhoneNumber;
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
}
