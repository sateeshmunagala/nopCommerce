using Nop.Web.Framework.Models;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.Skillfinder.InlineFilter.Models;

public record InlineFilterCategoryModel : BaseNopModel
{
    public string Name { get; set; }
    public string SeName { get; set; }
    public bool IsSelected { get; set; }
}

public record InlineFilterProductModel : BaseNopModel
{
    public ProductOverviewModel ProductOverview { get; set; }
    public string ProductUrl { get; set; }
    public string Summary { get; set; }
    public string PictureUrl { get; set; }
    public string PictureAlt { get; set; }
}

public record FilteredProductsGridModel : BaseNopModel
{
    public IList<InlineFilterProductModel> Products { get; set; } = new List<InlineFilterProductModel>();
    public string SelectedCategorySeName { get; set; }
    public string ViewMoreUrl { get; set; }
    public bool UseAiInterviewCards { get; set; }
}

public record PublicInfoModel : BaseNopModel
{
    public IList<InlineFilterCategoryModel> Categories { get; set; } = new List<InlineFilterCategoryModel>();
    public FilteredProductsGridModel Results { get; set; } = new();
}
