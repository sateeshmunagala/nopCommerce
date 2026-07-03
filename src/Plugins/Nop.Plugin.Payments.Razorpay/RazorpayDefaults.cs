namespace Nop.Plugin.Payments.Razorpay;

public static class RazorpayDefaults
{
    public static string SystemName => "Payments.Razorpay";

    public static string RazorpayOrderIdAttribute => "RazorpayOrderId";
    public static string RazorpayPaymentIdAttribute => "RazorpayPaymentId";
    public static string RazorpaySignatureAttribute => "RazorpaySignature";
    public static string RazorpayOrderAmountAttribute => "RazorpayOrderAmount";
    public static string RazorpayOrderCurrencyAttribute => "RazorpayOrderCurrency";
}
