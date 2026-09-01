using System.Data;
using LinqToDB;
using LinqToDB.Data;
using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Data;

// Compatibility retained for read rollback; remove in JobSupport 2.0.0.
public static class LegacyProcedureParameterFactory
{
    public static DataParameter[] CreateProfileSearchParameters(ProfileSearchRequest request)
    {
        return new[]
        {
            new DataParameter("ProductIds", JoinIds(request.ProfileIds), DataType.NVarChar),
            new DataParameter("CustomerId", request.CustomerId, DataType.Int32),
            new DataParameter("ProfileTypeId", request.ProfileTypeId, DataType.Int32)
        };
    }

    public static DataParameter[] CreateShortListParameters(ProfileSearchRequest request,
        int shoppingCartTypeId,
        out DataParameter totalRecords)
    {
        totalRecords = new DataParameter("TotalRecords", null, DataType.Int32)
        {
            Direction = ParameterDirection.Output
        };

        return new[]
        {
            new DataParameter("ProductIds", JoinIds(request.ProfileIds), DataType.NVarChar),
            new DataParameter("ShoppingCartTypeId", shoppingCartTypeId, DataType.Int32),
            new DataParameter("CustomerId", request.CustomerId, DataType.Int32),
            new DataParameter("OrderBy", request.SortOrder, DataType.Int32),
            new DataParameter("PageIndex", request.PageIndex, DataType.Int32),
            new DataParameter("PageSize", request.PageSize, DataType.Int32),
            totalRecords
        };
    }

    private static string JoinIds(IList<int> ids)
    {
        return ids == null || !ids.Any() ? null : string.Join(",", ids);
    }
}
