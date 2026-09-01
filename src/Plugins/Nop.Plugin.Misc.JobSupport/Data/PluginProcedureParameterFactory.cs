using System.Data;
using LinqToDB;
using LinqToDB.Data;
using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Data;

public static class PluginProcedureParameterFactory
{
    public static DataParameter[] CreateProfileSearchParameters(ProfileSearchRequest request,
        out DataParameter totalRecords)
    {
        totalRecords = OutputTotalRecords();
        return new[]
        {
            new DataParameter("CustomerId", request.CustomerId, DataType.Int32),
            new DataParameter("StoreId", request.StoreId, DataType.Int32),
            new DataParameter("ProfileType", request.ProfileTypeId, DataType.Int32),
            new DataParameter("PrimarySkillIds", JoinIds(request.PrimarySkillIds), DataType.NVarChar),
            new DataParameter("SecondarySkillIds", JoinIds(request.SecondarySkillIds), DataType.NVarChar),
            new DataParameter("Availability", NullIfWhiteSpace(request.Availability), DataType.NVarChar),
            new DataParameter("Keywords", NullIfWhiteSpace(request.Keywords), DataType.NVarChar),
            new DataParameter("ExcludeOwnProfile", request.ExcludeOwnProfile, DataType.Boolean),
            new DataParameter("OrderBy", request.SortOrder, DataType.Int32),
            new DataParameter("PageIndex", request.PageIndex, DataType.Int32),
            new DataParameter("PageSize", request.PageSize, DataType.Int32),
            totalRecords
        };
    }

    public static DataParameter[] CreateRelationshipParameters(ProfileSearchRequest request,
        int direction,
        int? relationshipType,
        int? relationshipStatus,
        out DataParameter totalRecords)
    {
        totalRecords = OutputTotalRecords();
        return new[]
        {
            new DataParameter("CustomerId", request.CustomerId, DataType.Int32),
            new DataParameter("Direction", direction, DataType.Int32),
            new DataParameter("RelationshipType", relationshipType, DataType.Int32),
            new DataParameter("RelationshipStatus", relationshipStatus, DataType.Int32),
            new DataParameter("OrderBy", request.SortOrder, DataType.Int32),
            new DataParameter("PageIndex", request.PageIndex, DataType.Int32),
            new DataParameter("PageSize", request.PageSize, DataType.Int32),
            totalRecords
        };
    }

    public static int? ReadTotalRecords(DataParameter parameter) =>
        parameter.Value == null || parameter.Value == DBNull.Value ? null : Convert.ToInt32(parameter.Value);

    private static DataParameter OutputTotalRecords() => new("TotalRecords", null, DataType.Int32)
    {
        Direction = ParameterDirection.Output
    };

    private static string JoinIds(IList<int> ids) => ids == null || ids.Count == 0
        ? null
        : string.Join(',', ids.Where(id => id > 0).Distinct());

    private static string NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
