# SQL Reports Test Evidence - 2026-08-01

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
Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 3 s - Nop.Plugin.Misc.SqlReports.Tests.dll (net10.0)
```

Coverage notes:

```text
- Report delete preserves execution logs and sets SqlReportId to null.
- Admin report delete path detaches execution logs and records delete activity.
- CASE ... END SELECT query is allowed by the SQL validator.
- Unsafe multi-statement and non-read-only SQL remains blocked.
- Execution log report FK migration uses SQL Server metadata lookup for FK name variations.
```
