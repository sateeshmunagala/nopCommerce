using System;
using Nop.Core;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Domain;

public class WhatsAppBlacklist : BaseEntity
{
	public int CustomerId { get; set; }

	public string PhoneNumber { get; set; } = string.Empty;

	public DateTime FailedAt { get; set; }

	public string? Reason { get; set; }
}
