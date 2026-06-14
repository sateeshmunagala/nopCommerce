using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.AIInterview.Models;

public record JobRequirementsModel : BaseNopModel
{
    public int ProductId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.ProductRequirements.ResumeRequired")]
    public bool ResumeRequired { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.ProductRequirements.InterviewRequired")]
    public bool InterviewRequired { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.ProductRequirements.MinimumScore")]
    public decimal MinimumScore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.ProductRequirements.QuestionCount")]
    public int QuestionCount { get; set; } = 3;

    public bool IsJobProduct { get; set; }
}

public record AiServiceSettingsModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.AIInterview.UseMockResponses")]
    public bool UseMockResponses { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Provider")]
    public string Provider { get; set; }

    public IList<SelectListItem> AvailableProviders { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.ApiKey")]
    public string ApiKey { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Model")]
    public string Model { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Prompt")]
    public string Prompt { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.ServiceSettings")]
    public string ServiceSettings { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson")]
    public string CreditProductSkuMappingsJson { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.CreditPurchasePageUrl")]
    public string CreditPurchasePageUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiEndpointUrl")]
    public string AzureOpenAiEndpointUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiApiKey")]
    public string AzureOpenAiApiKey { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiDeploymentOrModel")]
    public string AzureOpenAiDeploymentOrModel { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechKey")]
    public string AzureSpeechKey { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechRegion")]
    public string AzureSpeechRegion { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl")]
    public string AzureBlobStorageContainerUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken")]
    public string AzureBlobStorageSasToken { get; set; }
}

public record SponsorInviteAdminModel : BaseNopModel
{
    public SponsorInviteAdminModel()
    {
        Invites = new List<SponsorInviteRowModel>();
    }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.BulkEmails")]
    public string BulkEmails { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.ProductId")]
    public int ProductId { get; set; }

    public IList<SelectListItem> AvailableProducts { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.MaxAttempts")]
    public int MaxAttempts { get; set; } = 1;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.ExpiryDateUtc")]
    public DateTime? ExpiryDateUtc { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.SponsorId")]
    public int? SponsorId { get; set; }

    public IList<SelectListItem> AvailableSponsors { get; set; } = new List<SelectListItem>();

    public string Message { get; set; }

    public IList<SponsorInviteRowModel> Invites { get; set; }
}

public record SponsorInviteRowModel : BaseNopModel
{
    public int Id { get; set; }
    public int SponsorId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string ProductAdminUrl { get; set; }
    public string VendorName { get; set; }
    public string VendorAdminUrl { get; set; }
    public string Email { get; set; }
    public string InviteCode { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? ExpiryDateUtc { get; set; }
    public bool IsActive { get; set; }
    public bool IsAccepted { get; set; }
    public bool IsExpired { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public string Status { get; set; }
    public string StatusText { get; set; }
}

public record CreditManagementModel : BaseNopModel
{
    public CreditManagementModel()
    {
        LedgerEntries = new List<CreditLedgerRowModel>();
        ActivityCustomers = new List<ApplicantCreditActivityRowModel>();
    }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Credits.CustomerId")]
    public int CustomerId { get; set; }

    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public string CustomerAdminUrl { get; set; }

    public IList<SelectListItem> AvailableCustomers { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Credits.Amount")]
    public decimal Amount { get; set; }

    public decimal WalletBalance { get; set; }
    public string ScopeTitle { get; set; }
    public IList<CreditLedgerRowModel> LedgerEntries { get; set; }
    public IList<ApplicantCreditActivityRowModel> ActivityCustomers { get; set; }
}

public record CreditLedgerRowModel : BaseNopModel
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; }
    public string CustomerAdminUrl { get; set; }
    public decimal Amount { get; set; }
    public string TransactionType { get; set; }
    public string Remarks { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}

public record ApplicantCreditActivityRowModel : BaseNopModel
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public string CustomerAdminUrl { get; set; }
    public decimal WalletBalance { get; set; }
    public decimal TotalDeposited { get; set; }
    public decimal TotalWithdrawn { get; set; }
    public DateTime? LastCreditActivityUtc { get; set; }
}

public record ScoreboardFilterModel : BaseNopModel
{
    public ScoreboardFilterModel()
    {
        Rows = new List<ScoreboardRowModel>();
    }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.Candidate")]
    public string Candidate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.Vendor")]
    public string Vendor { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.JobPosting")]
    public string JobPosting { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.Status")]
    public string Status { get; set; }

    public IList<SelectListItem> AvailableStatuses { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.MinScore")]
    public decimal? MinScore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.MaxScore")]
    public decimal? MaxScore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.StartDate")]
    public DateTime? StartDate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.EndDate")]
    public DateTime? EndDate { get; set; }

    public IList<ScoreboardRowModel> Rows { get; set; }
}

public record ScoreboardRowModel : BaseNopModel
{
    public int SessionId { get; set; }
    public int ApplicationId { get; set; }
    public int ProductId { get; set; }
    public int VendorId { get; set; }
    public int CandidateCustomerId { get; set; }
    public string CandidateName { get; set; }
    public string CandidateEmail { get; set; }
    public string CandidateAdminUrl { get; set; }
    public string VendorName { get; set; }
    public string VendorAdminUrl { get; set; }
    public string JobTitle { get; set; }
    public string ProductAdminUrl { get; set; }
    public string Status { get; set; }
    public decimal Score { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
    public string ReportUrl { get; set; }
}
