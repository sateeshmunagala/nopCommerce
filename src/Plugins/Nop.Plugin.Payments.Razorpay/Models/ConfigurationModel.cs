using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.ModelBinding.Binders;

namespace Nop.Plugin.Payments.Razorpay.Models;

public record ConfigurationModel : BaseNopModel
{
    public int ActiveStoreScopeConfiguration { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Razorpay.Fields.KeyId")]
    public string KeyId { get; set; } = string.Empty;
    public bool KeyId_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Razorpay.Fields.KeySecret")]
    [DataType(DataType.Password)]
    public string KeySecret { get; set; } = string.Empty;
    public bool KeySecret_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Razorpay.Fields.PaymentCapture")]
    public bool PaymentCapture { get; set; }
    public bool PaymentCapture_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Razorpay.Fields.AdditionalFee")]
    public decimal AdditionalFee { get; set; }
    public bool AdditionalFee_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Razorpay.Fields.AdditionalFeePercentage")]
    public bool AdditionalFeePercentage { get; set; }
    public bool AdditionalFeePercentage_OverrideForStore { get; set; }
}
