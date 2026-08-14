using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Orders;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Services;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Controllers;

[Route("WhatsApp")]
[AutoValidateAntiforgeryToken]
public class WhatsAppPublicController : Controller
{
	private readonly IWorkContext _workContext;

	private readonly IOrderService _orderService;

	private readonly ICustomerService _customerService;

	private readonly IGenericAttributeService _genericAttributeService;

	private readonly IWhatsAppBusinessService _whatsAppService;

	private readonly WhatsAppBusinessSettings _settings;

	public WhatsAppPublicController(IWorkContext workContext, IOrderService orderService, ICustomerService customerService, IGenericAttributeService genericAttributeService, IWhatsAppBusinessService whatsAppService, WhatsAppBusinessSettings settings)
	{
		_workContext = workContext;
		_orderService = orderService;
		_customerService = customerService;
		_genericAttributeService = genericAttributeService;
		_whatsAppService = whatsAppService;
		_settings = settings;
	}

	[HttpPost("OptIn")]
	public async Task<IActionResult> OptIn([FromForm] int orderId, [FromForm] string phone)
	{
		if (!_settings.IsEnabled || string.IsNullOrWhiteSpace(phone))
		{
			return Json(new
			{
				success = false
			});
		}
		Customer customer = await _workContext.GetCurrentCustomerAsync();
		if (customer == null)
		{
			return Json(new
			{
				success = false
			});
		}
		Order val = await _orderService.GetOrderByIdAsync(orderId);
		if (val == null || val.CustomerId != ((BaseEntity)customer).Id)
		{
			return Json(new
			{
				success = false
			});
		}
		await _genericAttributeService.SaveAttributeAsync<bool>((BaseEntity)(object)customer, WhatsAppBusinessDefaults.CustomerOptInAttribute, true, 0);
		await _genericAttributeService.SaveAttributeAsync<string>((BaseEntity)(object)customer, WhatsAppBusinessDefaults.CustomerPhoneAttribute, phone, 0);
		return Json(new
		{
			success = true
		});
	}

	[HttpPost("OptOut")]
	public async Task<IActionResult> OptOut()
	{
		Customer customer = await _workContext.GetCurrentCustomerAsync();
		if (customer == null)
		{
			return Json(new
			{
				success = false
			});
		}
		await _genericAttributeService.SaveAttributeAsync<bool>((BaseEntity)(object)customer, WhatsAppBusinessDefaults.CustomerOptInAttribute, false, 0);
		string? text = await _genericAttributeService.GetAttributeAsync<string>((BaseEntity)(object)customer, WhatsAppBusinessDefaults.CustomerPhoneAttribute, 0);
		if (!string.IsNullOrWhiteSpace(text))
		{
			await _whatsAppService.AddToBlacklistAsync(((BaseEntity)customer).Id, text, "Customer opted out");
		}
		return Json(new
		{
			success = true
		});
	}

	[HttpGet("TrackingStatus")]
	public async Task<IActionResult> TrackingStatus(int orderId)
	{
		Customer customer = await _workContext.GetCurrentCustomerAsync();
		if (customer == null)
		{
			return Json(new
			{
				success = false
			});
		}
		Order val = await _orderService.GetOrderByIdAsync(orderId);
		if (val == null || val.CustomerId != ((BaseEntity)customer).Id)
		{
			return Json(new
			{
				success = false
			});
		}
		return Json(new
		{
			success = true,
			notifications = await _whatsAppService.GetOrderLogsAsync(orderId)
		});
	}
}
