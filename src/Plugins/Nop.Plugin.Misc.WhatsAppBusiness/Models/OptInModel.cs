using Nop.Web.Framework.Models;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Models;

public record OptInModel : BaseNopModel
{
	public int OrderId { get; set; }
	public string OrderNumber { get; set; } = string.Empty;
	public string CustomerPhone { get; set; } = string.Empty;
	public bool IsOptedIn { get; set; }
}
