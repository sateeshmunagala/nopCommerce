# SQL Reports Release Evidence - 2026-08-01

Working directory:

```text
D:\nopcommerce\sateeshmunagala\nopCommerce\src
```

Command:

```text
dotnet test Plugins\Nop.Plugin.Misc.SqlReports.Tests\Nop.Plugin.Misc.SqlReports.Tests.csproj -c Debug --nologo
```

Summary:

```text
Test Run Successful.
Total tests: 47
     Passed: 47
 Total time: 14.9967 Seconds
```

Coverage notes:

```text
- Report delete preserves execution logs and sets SqlReportId to null.
- Admin report delete path detaches execution logs and records delete activity.
- CASE ... END SELECT query is allowed by the SQL validator.
- Unsafe multi-statement and non-read-only SQL remains blocked.
- Execution log report FK migration uses SQL Server metadata lookup for FK name variations.
- SQL Server integration coverage creates a disposable LocalDB database with a differently named FK,
  runs ExecutionLogReportForeignKeyMigration through FluentMigrator, verifies ON DELETE SET NULL,
  deletes the report row, and verifies the retained execution log has null SqlReportId.
- Clean-schema migration integration coverage verifies the FK update migration skips safely when
  SQL Reports tables are not present yet.
- Instant query/export disabled-toggle controller paths verify no query execution and no execution log write.
```

Build command:

```text
dotnet build NopCommerce.sln -c Debug --nologo
```

Build summary:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Manual admin UI status:

```text
Not executed in-browser in this workspace. The configured nopCommerce app data does not have
Misc.SqlReports installed in plugins.json and no admin session/credentials were available for
a browser-driven admin flow. Persisted delete integrity is covered by SQL Server integration
tests plus the admin controller delete-path test.
```
