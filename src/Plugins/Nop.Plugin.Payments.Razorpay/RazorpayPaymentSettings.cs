using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.Razorpay;

public class RazorpayPaymentSettings : ISettings
{
    public string KeyId { get; set; } = string.Empty;
    public string KeySecret { get; set; } = string.Empty;
    public bool PaymentCapture { get; set; } = true;
    public decimal AdditionalFee { get; set; }
    public bool AdditionalFeePercentage { get; set; }
}
