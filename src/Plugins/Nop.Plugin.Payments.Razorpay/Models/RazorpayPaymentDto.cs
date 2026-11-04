namespace Nop.Plugin.Payments.Razorpay.Models;

public class RazorpayPaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
