using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Data;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.ScheduleTasks;
using Nop.Services.Shipping;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Services;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Infrastructure;

public class WhatsAppScheduleTask : IScheduleTask
{
	private readonly IWhatsAppBusinessService _whatsAppService;

	private readonly IRepository<Order> _orderRepository;

	private readonly IAddressService _addressService;

	private readonly IShipmentService _shipmentService;

	private readonly ICustomerService _customerService;

	private readonly ISettingService _settingService;

	private readonly WhatsAppBusinessSettings _settings;

	private readonly Random _random = new Random();

	public WhatsAppScheduleTask(IWhatsAppBusinessService whatsAppService, IRepository<Order> orderRepository, IAddressService addressService, IShipmentService shipmentService, ICustomerService customerService, ISettingService settingService, WhatsAppBusinessSettings settings)
	{
		_whatsAppService = whatsAppService;
		_orderRepository = orderRepository;
		_addressService = addressService;
		_shipmentService = shipmentService;
		_customerService = customerService;
		_settingService = settingService;
		_settings = settings;
	}

	public async Task ExecuteAsync()
	{
		if (!_settings.IsEnabled || string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.PhoneNumberId))
		{
			return;
		}
		int num = ((_settings.LookbackWindowDays > 0) ? _settings.LookbackWindowDays : 30);
		DateTime cursor = ((_settings.LastProcessedUtcTicks > 0) ? new DateTime(_settings.LastProcessedUtcTicks, DateTimeKind.Utc).AddHours(-1.0) : DateTime.UtcNow.AddDays(-num));
		List<Order> list = await AsyncIQueryableExtensions.ToListAsync<Order>((IQueryable<Order>)_orderRepository.Table.Where((Expression<Func<Order, bool>>)((Order o) => !o.Deleted && o.CreatedOnUtc >= cursor)).OrderBy((Expression<Func<Order, DateTime>>)((Order o) => o.CreatedOnUtc)));
		int maxBatch = ((_settings.MaxMessagesPerBatch > 0) ? _settings.MaxMessagesPerBatch : 50);
		int sent = 0;
		foreach (Order order in list)
		{
			if (sent >= maxBatch)
			{
				break;
			}
			List<(string, bool)> pendingNotifications = GetPendingNotifications(order);
			foreach (var (messageType, flag) in pendingNotifications)
			{
				if (sent >= maxBatch)
				{
					break;
				}
				if (!flag || await _whatsAppService.HasBeenNotifiedAsync(((BaseEntity)order).Id, messageType))
				{
					continue;
				}
				Address val = await _addressService.GetAddressByIdAsync(order.BillingAddressId);
				if (val == null || string.IsNullOrWhiteSpace(val.PhoneNumber))
				{
					continue;
				}
				string phone = val.PhoneNumber;
				if (!(await _whatsAppService.IsBlacklistedAsync(phone)))
				{
					if (sent > 0)
					{
						await AntibanDelayAsync();
					}
					await SendNotificationAsync(order, messageType, phone);
					sent++;
				}
			}
		}
		_settings.LastProcessedUtcTicks = DateTime.UtcNow.Ticks;
		await _settingService.SaveSettingAsync<WhatsAppBusinessSettings>(_settings, 0);
	}

	private List<(string messageType, bool enabled)> GetPendingNotifications(Order order)
	{
		List<(string, bool)> list = new List<(string, bool)>();
		if (order.OrderStatus == OrderStatus.Processing)
		{
			list.Add(("Processing", _settings.EnableOrderProcessing));
		}
		if (order.OrderStatus == OrderStatus.Cancelled)
		{
			list.Add(("Cancelled", _settings.EnableOrderCancelled));
		}
		if (order.OrderStatus == OrderStatus.Complete)
		{
			list.Add(("Complete", _settings.EnableShipmentDelivered));
		}
		return list;
	}

	private async Task SendNotificationAsync(Order order, string messageType, string phone)
	{
		Customer obj = await _customerService.GetCustomerByIdAsync(order.CustomerId);
		string? text = obj?.FirstName;
		if (text == null)
		{
			text = "Cliente";
		}
		switch (messageType)
		{
		case "Processing":
			await SendProcessingAsync(order, phone, text);
			break;
		case "Cancelled":
			await SendCancelledAsync(order, phone, text);
			break;
		case "Complete":
			await SendCompleteAsync(order, phone, text);
			break;
		}
	}

	private async Task SendProcessingAsync(Order order, string phone, string firstName)
	{
		if (!_settings.UseTemplateMessages || string.IsNullOrWhiteSpace(_settings.OrderConfirmationTemplateName))
		{
			await _whatsAppService.SendMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "Processing", "Your order #" + order.CustomOrderNumber + " is being processed and will be shipped soon.");
			return;
		}
		await _whatsAppService.SendTemplateMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "Processing", _settings.OrderConfirmationTemplateName, _settings.DefaultLanguageCode, new List<string>
		{
			firstName,
			"#" + order.CustomOrderNumber,
			order.OrderTotal.ToString("N2")
		});
	}

	private async Task SendCancelledAsync(Order order, string phone, string firstName)
	{
		await _whatsAppService.SendMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "Cancelled", "Your order #" + order.CustomOrderNumber + " has been cancelled. Contact us if you have questions.");
	}

	private async Task SendCompleteAsync(Order order, string phone, string firstName)
	{
		Shipment? obj = (await _shipmentService.GetShipmentsByOrderIdAsync(((BaseEntity)order).Id, (bool?)null, (bool?)null, 0)).FirstOrDefault((Shipment s) => !string.IsNullOrWhiteSpace(s.TrackingNumber));
		string? text = obj?.TrackingNumber;
		string carrierName = order.ShippingMethod ?? "";
		string? text2 = BuildTrackingUrl(carrierName, text);
		if (_settings.UseTemplateMessages && !string.IsNullOrWhiteSpace(_settings.DeliveryConfirmationTemplateName))
		{
			await _whatsAppService.SendTemplateMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "Complete", _settings.DeliveryConfirmationTemplateName, _settings.DefaultLanguageCode, new List<string>
			{
				firstName,
				"#" + order.CustomOrderNumber,
				text ?? "N/A"
			}, text);
			return;
		}
		string text3 = (string.IsNullOrWhiteSpace(text) ? ("Your order #" + order.CustomOrderNumber + " has been delivered. Thank you for shopping with us!") : $"Your order #{order.CustomOrderNumber} has been shipped. Tracking: {text}.");
		if (!string.IsNullOrWhiteSpace(text2))
		{
			text3 = text3 + " Track at: " + text2;
		}
		await _whatsAppService.SendMessageAsync(((BaseEntity)order).Id, order.CustomerId, phone, "Complete", text3, text);
	}

	private async Task AntibanDelayAsync()
	{
		int num = Math.Max(1, _settings.MinDelayBetweenSendsSeconds);
		int maxValue = Math.Max(num + 1, _settings.MaxDelayBetweenSendsSeconds);
		await Task.Delay(TimeSpan.FromSeconds(_random.Next(num, maxValue)));
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
