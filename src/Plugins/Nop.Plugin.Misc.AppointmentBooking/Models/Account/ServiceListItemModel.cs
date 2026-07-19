namespace Nop.Plugin.Misc.AppointmentBooking.Models.Account;

/// <summary>
/// Represents a vendor service card in the account area
/// </summary>
public record ServiceListItemModel
{
    public int Id { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public int DurationMinutes { get; set; }

    public string Price { get; set; }

    public bool IsPublic { get; set; }

    public int MappedProductId { get; set; }
}
