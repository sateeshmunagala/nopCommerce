namespace Nop.Plugin.Misc.SqlReports;

public static class SqlReportsDefaults
{
    public const string SystemName = "Misc.SqlReports";

    public const string MenuSystemName = "Sql reports";
    public const string ReportsMenuSystemName = "Sql reports saved reports";
    public const string ParametersMenuSystemName = "Sql reports parameters";
    public const string InstantQueryMenuSystemName = "Sql reports instant query";
    public const string ConfigureMenuSystemName = "Sql reports configure";

    public static class Permissions
    {
        public const string ManageReports = "ManageSqlReports";
        public const string RunReports = "RunSqlReports";
    }

    public static class Routes
    {
        public const string Configure = "Plugin.Misc.SqlReports.Configure";
        public const string Reports = "Plugin.Misc.SqlReports.Reports";
        public const string ReportCreate = "Plugin.Misc.SqlReports.ReportCreate";
        public const string ReportEdit = "Plugin.Misc.SqlReports.ReportEdit";
        public const string ReportRun = "Plugin.Misc.SqlReports.ReportRun";
        public const string Parameters = "Plugin.Misc.SqlReports.Parameters";
        public const string ParameterCreate = "Plugin.Misc.SqlReports.ParameterCreate";
        public const string ParameterEdit = "Plugin.Misc.SqlReports.ParameterEdit";
        public const string InstantQuery = "Plugin.Misc.SqlReports.InstantQuery";
    }

    public static class ActivityLogTypeSystemNames
    {
        public const string AddReport = "SqlReports.AddReport";
        public const string EditReport = "SqlReports.EditReport";
        public const string DeleteReport = "SqlReports.DeleteReport";
        public const string AddParameter = "SqlReports.AddParameter";
        public const string EditParameter = "SqlReports.EditParameter";
        public const string DeleteParameter = "SqlReports.DeleteParameter";
        public const string RunReport = "SqlReports.RunReport";
        public const string ExportReport = "SqlReports.ExportReport";
    }
}
