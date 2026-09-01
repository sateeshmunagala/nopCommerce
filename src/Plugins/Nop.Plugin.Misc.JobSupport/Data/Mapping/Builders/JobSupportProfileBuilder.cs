using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Media;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Mapping.Builders;

public class JobSupportProfileBuilder : NopEntityBuilder<JobSupportProfile>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportProfile.CustomerId)).AsInt32().NotNullable().ForeignKey<Customer>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfile.LegacyProductId)).AsInt32().Nullable()
            .WithColumn(nameof(JobSupportProfile.ProfileType)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfile.DisplayName)).AsString(400).Nullable()
            .WithColumn(nameof(JobSupportProfile.ShortDescription)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(JobSupportProfile.FullDescription)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(JobSupportProfile.CurrentAvailability)).AsString(400).Nullable()
            .WithColumn(nameof(JobSupportProfile.MotherTongue)).AsString(400).Nullable()
            .WithColumn(nameof(JobSupportProfile.RelevantExperience)).AsString(400).Nullable()
            .WithColumn(nameof(JobSupportProfile.AvatarPictureId)).AsInt32().Nullable().ForeignKey<Picture>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfile.CountryId)).AsInt32().Nullable().ForeignKey<Country>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfile.StateProvinceId)).AsInt32().Nullable().ForeignKey<StateProvince>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfile.City)).AsString(200).Nullable()
            .WithColumn(nameof(JobSupportProfile.IsPublished)).AsBoolean().NotNullable()
            .WithColumn(nameof(JobSupportProfile.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportProfile.UpdatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportProfile.MigrationSource)).AsString(100).Nullable()
            .WithColumn(nameof(JobSupportProfile.LegacySourceId)).AsInt32().Nullable();
    }
}
