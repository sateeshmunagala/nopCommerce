using FluentMigrator;

namespace Nop.Data.Migrations.Installation;

[Migration(2026052201, "Booking integration tables")]
public class BookingMigration : Migration
{
    public override void Up()
    {
        // Booking_IntegrationToken
        if (!Schema.Table("Booking_IntegrationToken").Exists())
        {
            Create.Table("Booking_IntegrationToken")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("VendorId").AsInt32().NotNullable()
                .WithColumn("GoogleAccountEmail").AsString(255).Nullable()
                .WithColumn("AccessToken").AsString(int.MaxValue).Nullable()
                .WithColumn("RefreshToken").AsString(int.MaxValue).Nullable()
                .WithColumn("TokenExpiryUtc").AsDateTime().Nullable()
                .WithColumn("IsActive").AsBoolean().WithDefaultValue(false)
                .WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
                .WithColumn("UpdatedOnUtc").AsDateTime().NotNullable();
        }

        // Booking_ProductMapping
        if (!Schema.Table("Booking_ProductMapping").Exists())
        {
            Create.Table("Booking_ProductMapping")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("ProductId").AsInt32().NotNullable()
                .WithColumn("VendorId").AsInt32().NotNullable()
                .WithColumn("GoogleBookingUrl").AsString(2000).Nullable()
                .WithColumn("IsActive").AsBoolean().WithDefaultValue(false)
                .WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
                .WithColumn("UpdatedOnUtc").AsDateTime().NotNullable();
        }

        // Booking_Appointment
        if (!Schema.Table("Booking_Appointment").Exists())
        {
            Create.Table("Booking_Appointment")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("VendorId").AsInt32().NotNullable()
                .WithColumn("ProductId").AsInt32().NotNullable()
                .WithColumn("CustomerId").AsInt32().NotNullable()
                .WithColumn("OrderId").AsInt32().NotNullable()
                .WithColumn("GoogleEventId").AsString(255).Nullable()
                .WithColumn("JoinLink").AsString(2000).Nullable()
                .WithColumn("BookingStatus").AsString(100).Nullable()
                .WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
                .WithColumn("UpdatedOnUtc").AsDateTime().NotNullable();
        }
    }

    public override void Down()
    {
        if (Schema.Table("Booking_Appointment").Exists())
            Delete.Table("Booking_Appointment");

        if (Schema.Table("Booking_ProductMapping").Exists())
            Delete.Table("Booking_ProductMapping");

        if (Schema.Table("Booking_IntegrationToken").Exists())
            Delete.Table("Booking_IntegrationToken");
    }
}
