using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.JobSupport.Models.Admin;

public record ProfileAdminModel : BaseNopEntityModel
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string ProfileType { get; set; }
    public string PrimaryTechnology { get; set; }
    public string Availability { get; set; }
    public bool Premium { get; set; }
    public DateTime CreatedOn { get; set; }
    public bool Published { get; set; }
    public string CustomerEditUrl { get; set; }
    public string ProductEditUrl { get; set; }
}

public record ProfileListModel : BasePagedListModel<ProfileAdminModel>;
