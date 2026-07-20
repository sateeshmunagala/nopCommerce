using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Data.Migrations;

[NopMigration("2026/07/19 16:00:00:0000000", "Nop.Plugin.Misc.AppointmentBooking schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    #region Utilities

    private void CreateIndexIfMissing<T>(string indexName, params string[] columns)
    {
        var tableName = NameCompatibilityManager.GetTableName(typeof(T));

        if (Schema.Table(tableName).Index(indexName).Exists())
            return;

        var index = Create.Index(indexName).OnTable(tableName);
        foreach (var column in columns)
            index.OnColumn(column).Ascending();

        index.WithOptions().NonClustered();
    }

    private void CreateIndexes()
    {
        CreateIndexIfMissing<ServiceProductMapping>("IX_AppointmentBooking_Product_Active",
            nameof(ServiceProductMapping.ProductId),
            nameof(ServiceProductMapping.IsActive));

        CreateIndexIfMissing<ServiceProductMapping>("IX_AppointmentBooking_ServiceProduct_Service",
            nameof(ServiceProductMapping.ServiceId));

        CreateIndexIfMissing<AvailabilityRule>("IX_AppointmentBooking_AvailabilityRule_Service_Day",
            nameof(AvailabilityRule.ServiceId),
            nameof(AvailabilityRule.DayOfWeek),
            nameof(AvailabilityRule.IsActive));

        CreateIndexIfMissing<AvailabilityException>("IX_AppointmentBooking_AvailabilityException_Service_Date",
            nameof(AvailabilityException.ServiceId),
            nameof(AvailabilityException.ExceptionDateUtc));

        CreateIndexIfMissing<Booking>("IX_AppointmentBooking_Booking_Service_Start",
            nameof(Booking.ServiceId),
            nameof(Booking.StartUtc));

        CreateIndexIfMissing<Booking>("IX_AppointmentBooking_Booking_OrderItem",
            nameof(Booking.OrderItemId));

        CreateIndexIfMissing<TimeSlotHold>("IX_AppointmentBooking_TimeSlotHold_Service_Start",
            nameof(TimeSlotHold.ServiceId),
            nameof(TimeSlotHold.StartUtc),
            nameof(TimeSlotHold.ExpiresOnUtc));

        CreateIndexIfMissing<TimeSlotHold>("IX_AppointmentBooking_TimeSlotHold_Token",
            nameof(TimeSlotHold.HoldToken));
    }

    #endregion

    #region Methods

    /// <summary>
    /// Collect the UP migration expressions
    /// </summary>
    public override void Up()
    {
        this.CreateTableIfNotExists<BookableService>();
        this.CreateTableIfNotExists<ServiceProductMapping>();
        this.CreateTableIfNotExists<AvailabilityRule>();
        this.CreateTableIfNotExists<AvailabilityException>();
        this.CreateTableIfNotExists<Booking>();
        this.CreateTableIfNotExists<BookingParticipant>();
        this.CreateTableIfNotExists<ServiceQuestion>();
        this.CreateTableIfNotExists<BookingAnswer>();
        this.CreateTableIfNotExists<NotificationLog>();
        this.CreateTableIfNotExists<TimeSlotHold>();

        CreateIndexes();
    }

    /// <summary>
    /// Collects the DOWN migration expressions
    /// </summary>
    public override void Down()
    {
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(TimeSlotHold)));
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(NotificationLog)));
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(BookingAnswer)));
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(ServiceQuestion)));
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(BookingParticipant)));
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(Booking)));
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(AvailabilityException)));
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(AvailabilityRule)));
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(ServiceProductMapping)));
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(BookableService)));
    }

    #endregion
}
