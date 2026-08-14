using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Orders;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Models;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Components;

[ViewComponent(Name = "WhatsAppOptIn")]
public class WhatsAppOptInViewComponent : ViewComponent
{
	private readonly IWorkContext _workContext;

	private readonly IOrderService _orderService;

	private readonly IAddressService _addressService;

	private readonly ICustomerService _customerService;

	private readonly IGenericAttributeService _genericAttributeService;

	private readonly WhatsAppBusinessSettings _settings;

	public WhatsAppOptInViewComponent(IWorkContext workContext, IOrderService orderService, IAddressService addressService, ICustomerService customerService, IGenericAttributeService genericAttributeService, WhatsAppBusinessSettings settings)
	{
		_workContext = workContext;
		_orderService = orderService;
		_addressService = addressService;
		_customerService = customerService;
		_genericAttributeService = genericAttributeService;
		_settings = settings;
	}

	public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
	{
		if (!_settings.IsEnabled || !_settings.ShowOptInOnCheckoutCompleted)
		{
			return Content(string.Empty);
		}
		Customer customer = await _workContext.GetCurrentCustomerAsync();
		if (customer == null)
		{
			return Content(string.Empty);
		}
		bool flag = _settings.RequireCustomerAccount;
		if (flag)
		{
			flag = await _customerService.IsGuestAsync(customer, true);
		}
		if (flag)
		{
			return Content(string.Empty);
		}
		int orderId = GetIntProperty(additionalData, "OrderId");
		if (orderId == 0)
		{
			return Content(string.Empty);
		}
		Order order = await _orderService.GetOrderByIdAsync(orderId);
		if (order == null || order.CustomerId != ((BaseEntity)customer).Id)
		{
			return Content(string.Empty);
		}
		bool isOptedIn = await _genericAttributeService.GetAttributeAsync<bool>((BaseEntity)(object)customer, WhatsAppBusinessDefaults.CustomerOptInAttribute, 0, false);
		Address obj = await _addressService.GetAddressByIdAsync(order.BillingAddressId);
		string customerPhone = ((obj != null) ? obj.PhoneNumber : null) ?? "";
		OptInModel model = new OptInModel
		{
			OrderId = orderId,
			OrderNumber = order.CustomOrderNumber,
			CustomerPhone = customerPhone,
			IsOptedIn = isOptedIn
		};
		return View("~/Plugins/Misc.WhatsAppBusiness/Views/Components/WhatsAppOptIn/Default.cshtml", model);
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
