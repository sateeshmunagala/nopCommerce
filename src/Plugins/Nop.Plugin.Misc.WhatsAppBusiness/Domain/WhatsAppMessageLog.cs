using System;
using Nop.Core;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Domain;

public class WhatsAppMessageLog : BaseEntity
{
	public int OrderId { get; set; }

	public int CustomerId { get; set; }

	public string PhoneNumber { get; set; } = string.Empty;

	public string MessageType { get; set; } = string.Empty;

	public string Status { get; set; } = string.Empty;

	public DateTime SentAt { get; set; }

	public string? Error { get; set; }

	public string? TrackingNumber { get; set; }

	public string? WhatsAppMessageId { get; set; }

	public string? TemplateUsed { get; set; }
}
