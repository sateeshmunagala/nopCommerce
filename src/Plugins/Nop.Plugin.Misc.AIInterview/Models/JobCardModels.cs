using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AIInterview.Models;

public partial record AIInterviewJobSpecificationSnapshotModel : BaseNopModel
{
    public string WorkArrangement { get; set; }
    public string EmploymentType { get; set; }
    public string JobLocation { get; set; }
    public string SalaryRange { get; set; }
    public string ExperienceLevel { get; set; }
}

public partial record AIInterviewJobProductCardModel : BaseNopEntityModel
{
    public string JobTitle { get; set; }
    public string CompanyName { get; set; }
    public string Summary { get; set; }
    public string PreviewDescription { get; set; }
    public string SeName { get; set; }
    public string ProductUrl { get; set; }
    public string PostedDateText { get; set; }

    public string ImageUrl { get; set; }
    public string ImageAlt { get; set; }
    public bool UseImagePlaceholder { get; set; }
    public string ImagePlaceholderText { get; set; }

    public DateTime CreatedOnUtc { get; set; }
    public int AppliedCount { get; set; }

    public bool CanSaveJob { get; set; }
    public bool IsSavedJob { get; set; }
    public int WishlistItemId { get; set; }

    public AIInterviewJobSpecificationSnapshotModel Specifications { get; set; } = new();
}
