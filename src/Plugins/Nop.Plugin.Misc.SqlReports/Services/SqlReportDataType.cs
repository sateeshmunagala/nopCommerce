namespace Nop.Plugin.Misc.SqlReports.Services;

public static class SqlReportDataType
{
    public const string String = "String";
    public const string Int32 = "Int32";
    public const string Decimal = "Decimal";
    public const string Boolean = "Boolean";
    public const string DateTime = "DateTime";

    public static IList<string> All => new List<string> { String, Int32, Decimal, Boolean, DateTime };
}
