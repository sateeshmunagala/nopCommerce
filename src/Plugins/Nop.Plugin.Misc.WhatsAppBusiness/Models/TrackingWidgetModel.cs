using System.Collections.Generic;
using Nop.Web.Framework.Models;
using Nop.Plugin.Misc.WhatsAppBusiness.Domain;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Models;

public record TrackingWidgetModel : BaseNopModel
{
	public int OrderId { get; set; }
	public string OrderNumber { get; set; } = string.Empty;
	public string OrderStatus { get; set; } = string.Empty;
	public string CarrierName { get; set; } = string.Empty;
	public string? TrackingNumber { get; set; }
	public string? TrackingUrl { get; set; }
	public string CustomerPhone { get; set; } = string.Empty;
	public bool IsOptedIn { get; set; }
	public int CurrentStep { get; set; }
	public IList<WhatsAppMessageLog> Notifications { get; set; } = new List<WhatsAppMessageLog>();
}
