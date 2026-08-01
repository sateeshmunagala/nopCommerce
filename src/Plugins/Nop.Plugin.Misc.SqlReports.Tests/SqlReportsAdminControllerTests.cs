using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Logging;
using Nop.Plugin.Misc.SqlReports;
using Nop.Plugin.Misc.SqlReports.Admin.Controllers;
using Nop.Plugin.Misc.SqlReports.Admin.Factories;
using Nop.Plugin.Misc.SqlReports.Admin.Models;
using Nop.Plugin.Misc.SqlReports.Domain;
using Nop.Plugin.Misc.SqlReports.Services;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Factories;
using NUnit.Framework;

namespace Nop.Plugin.Misc.SqlReports.Tests;

[TestFixture]
public class SqlReportsAdminControllerTests
{
    [Test]
    public async Task Run_Post_LogsExecution_AndActivity()
    {
        var fixture = new ControllerFixture();

        var result = await fixture.Controller.Run(new SqlReportRunModel { Id = fixture.Report.Id });

        Assert.That(result, Is.TypeOf<ViewResult>());
        Assert.That(fixture.ReportService.ExecutionLogs, Has.Count.EqualTo(1));
        Assert.That(fixture.ReportService.ExecutionLogs[0].SqlReportId, Is.EqualTo(fixture.Report.Id));
        Assert.That(fixture.ReportService.ExecutionLogs[0].CustomerId, Is.EqualTo(fixture.Customer.Id));
        Assert.That(fixture.ReportService.ExecutionLogs[0].Success, Is.True);
        fixture.ActivityService.Verify(service => service.InsertActivityAsync(
            SqlReportsDefaults.ActivityLogTypeSystemNames.RunReport,
            It.Is<string>(comment => comment.Contains($"ID = {fixture.Report.Id}")),
            fixture.Report), Times.Once);
    }

    [Test]
    public async Task Export_Post_LogsExecution_AndActivity_AndReturnsXlsx()
    {
        var fixture = new ControllerFixture();

        var result = await fixture.Controller.Export(fixture.Report.Id, new SqlReportRunModel { Id = fixture.Report.Id });

        var file = (FileContentResult)result;
        Assert.That(file.ContentType, Is.EqualTo("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        Assert.That(file.FileContents, Is.EqualTo(new byte[] { (byte)'P', (byte)'K' }));
        Assert.That(fixture.ReportService.ExecutionLogs, Has.Count.EqualTo(1));
        Assert.That(fixture.ExecutionService.ExportedResult.RowsReturned, Is.EqualTo(1));
        fixture.ActivityService.Verify(service => service.InsertActivityAsync(
            SqlReportsDefaults.ActivityLogTypeSystemNames.ExportReport,
            It.Is<string>(comment => comment.Contains($"ID = {fixture.Report.Id}")),
            fixture.Report), Times.Once);
    }

    [Test]
    public async Task Export_Post_WhenDisabled_DoesNotExecuteOrLog()
    {
        var fixture = new ControllerFixture(settings => settings.AllowExport = false);

        var result = await fixture.Controller.Export(fixture.Report.Id, new SqlReportRunModel { Id = fixture.Report.Id });

        Assert.That(result, Is.Not.TypeOf<FileContentResult>());
        Assert.That(fixture.ExecutionService.ExecuteCallCount, Is.EqualTo(0));
        Assert.That(fixture.ReportService.ExecutionLogs, Is.Empty);
    }

    [Test]
    public async Task ReportDelete_Post_WithExecutionLogs_DetachesLogsAndLogsActivity()
    {
        var fixture = new ControllerFixture();
        fixture.ReportService.ExecutionLogs.Add(new SqlReportExecutionLog { Id = 1, SqlReportId = fixture.Report.Id, CustomerId = fixture.Customer.Id, Success = true });

        var result = await fixture.Controller.ReportDelete(fixture.Report.Id);

        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
        Assert.That(fixture.ReportService.ReportDeleted, Is.True);
        Assert.That(fixture.ReportService.ExecutionLogs.Single().SqlReportId, Is.Null);
        fixture.ActivityService.Verify(service => service.InsertActivityAsync(
            SqlReportsDefaults.ActivityLogTypeSystemNames.DeleteReport,
            It.Is<string>(comment => comment.Contains($"ID = {fixture.Report.Id}")),
            fixture.Report), Times.Once);
    }

    [Test]
    public void InstantQuery_Get_WhenDisabled_ReturnsAccessDenied()
    {
        var fixture = new ControllerFixture(settings => settings.EnableInstantQuery = false);

        var result = fixture.Controller.InstantQuery();

        Assert.That(result, Is.TypeOf<ForbidResult>());
    }

    [Test]
    public async Task InstantQuery_Post_WhenDisabled_DoesNotExecuteOrLog()
    {
        var fixture = new ControllerFixture(settings => settings.EnableInstantQuery = false);

        var result = await fixture.Controller.InstantQuery(new InstantQueryModel { SqlQuery = "select 1" });

        Assert.That(result, Is.TypeOf<ForbidResult>());
        Assert.That(fixture.ExecutionService.ExecuteCallCount, Is.EqualTo(0));
        Assert.That(fixture.ReportService.ExecutionLogs, Is.Empty);
    }

    [Test]
    public async Task InstantQuery_Post_LogsExecution_WithNullableReportId()
    {
        var fixture = new ControllerFixture();

        var result = await fixture.Controller.InstantQuery(new InstantQueryModel { SqlQuery = "select 1" });

        Assert.That(result, Is.TypeOf<ViewResult>());
        Assert.That(fixture.ReportService.ExecutionLogs, Has.Count.EqualTo(1));
        Assert.That(fixture.ReportService.ExecutionLogs[0].SqlReportId, Is.Null);
        Assert.That(fixture.ExecutionService.LastMaxRows, Is.EqualTo(200));
    }

    [Test]
    public async Task InstantExport_WhenInstantQueryDisabled_DoesNotExecuteOrLog()
    {
        var fixture = new ControllerFixture(settings => settings.EnableInstantQuery = false);

        var result = await fixture.Controller.InstantExport(new InstantQueryModel { SqlQuery = "select 1" });

        Assert.That(result, Is.Not.TypeOf<FileContentResult>());
        Assert.That(fixture.ExecutionService.ExecuteCallCount, Is.EqualTo(0));
        Assert.That(fixture.ReportService.ExecutionLogs, Is.Empty);
    }

    [Test]
    public async Task InstantExport_WhenExportDisabled_DoesNotExecuteOrLog()
    {
        var fixture = new ControllerFixture(settings => settings.AllowExport = false);

        var result = await fixture.Controller.InstantExport(new InstantQueryModel { SqlQuery = "select 1" });

        Assert.That(result, Is.TypeOf<ForbidResult>());
        Assert.That(fixture.ExecutionService.ExecuteCallCount, Is.EqualTo(0));
        Assert.That(fixture.ReportService.ExecutionLogs, Is.Empty);
    }

    [Test]
    public async Task ParameterCreate_Post_LogsActivity_AfterSave()
    {
        var fixture = new ControllerFixture();

        var result = await fixture.Controller.ParameterCreate(new SqlReportParameterModel
        {
            Name = "Customer",
            ParameterName = "@CustomerId",
            DataType = SqlReportDataType.Text
        }, false);

        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
        Assert.That(fixture.ReportService.Parameter.Id, Is.GreaterThan(0));
        fixture.ActivityService.Verify(service => service.InsertActivityAsync(
            SqlReportsDefaults.ActivityLogTypeSystemNames.AddParameter,
            It.Is<string>(comment => comment.Contains($"ID = {fixture.ReportService.Parameter.Id}")),
            fixture.ReportService.Parameter), Times.Once);
    }

    [Test]
    public async Task ParameterEdit_Post_LogsActivity_AfterSave()
    {
        var fixture = new ControllerFixture();
        fixture.ReportService.Parameter = new SqlReportParameter { Id = 12, Name = "Customer", ParameterName = "CustomerId", DataType = SqlReportDataType.Text };

        var result = await fixture.Controller.ParameterEdit(new SqlReportParameterModel
        {
            Id = 12,
            Name = "Customer updated",
            ParameterName = "@CustomerId",
            DataType = SqlReportDataType.Number
        }, false);

        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
        Assert.That(fixture.ReportService.Parameter.DataType, Is.EqualTo(SqlReportDataType.Number));
        fixture.ActivityService.Verify(service => service.InsertActivityAsync(
            SqlReportsDefaults.ActivityLogTypeSystemNames.EditParameter,
            It.Is<string>(comment => comment.Contains("ID = 12")),
            fixture.ReportService.Parameter), Times.Once);
    }

    [Test]
    public async Task ParameterDelete_Post_LogsActivity_AfterDelete()
    {
        var fixture = new ControllerFixture();
        fixture.ReportService.Parameter = new SqlReportParameter { Id = 12, Name = "Customer", ParameterName = "CustomerId", DataType = SqlReportDataType.Text };

        var result = await fixture.Controller.ParameterDelete(12);

        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
        Assert.That(fixture.ReportService.DeletedParameter, Is.SameAs(fixture.ReportService.Parameter));
        fixture.ActivityService.Verify(service => service.InsertActivityAsync(
            SqlReportsDefaults.ActivityLogTypeSystemNames.DeleteParameter,
            It.Is<string>(comment => comment.Contains("ID = 12")),
            fixture.ReportService.Parameter), Times.Once);
    }

    private sealed class ControllerFixture
    {
        public ControllerFixture(Action<SqlReportsSettings> configureSettings = null)
        {
            Settings = new SqlReportsSettings
            {
                MaxRowsPerQuery = 10,
                CommandTimeoutSeconds = 5,
                MaxCellLength = 100,
                EnableInstantQuery = true,
                AllowExport = true
            };
            configureSettings?.Invoke(Settings);

            Customer = new Customer { Id = 42 };
            Report = new SqlReport
            {
                Id = 7,
                Name = "Sales report",
                SqlQuery = "select 1",
                IsActive = true
            };

            ReportService = new FakeSqlReportService { Report = Report };
            ExecutionService = new FakeExecutionService(Settings);
            ActivityService = new Mock<ICustomerActivityService>();
            ActivityService.Setup(service => service.InsertActivityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Nop.Core.BaseEntity>()))
                .ReturnsAsync(new ActivityLog());

            var workContext = new Mock<IWorkContext>();
            workContext.Setup(context => context.GetCurrentCustomerAsync()).ReturnsAsync(Customer);

            var modelFactory = new FakeSqlReportModelFactory(ReportService, Settings);

            Controller = new TestSqlReportsAdminController(
                new Mock<ILocalizationService>().Object,
                ActivityService.Object,
                new Mock<INotificationService>().Object,
                new Mock<ISettingService>().Object,
                workContext.Object,
                ExecutionService,
                modelFactory,
                ReportService,
                Settings);
        }

        public Mock<ICustomerActivityService> ActivityService { get; }

        public SqlReportsAdminController Controller { get; }

        public Customer Customer { get; }

        public FakeExecutionService ExecutionService { get; }

        public SqlReport Report { get; }

        public FakeSqlReportService ReportService { get; }

        public SqlReportsSettings Settings { get; }
    }

    private sealed class FakeSqlReportService : SqlReportService
    {
        public FakeSqlReportService()
            : base(null, null, null, null, null, null, null, null)
        {
        }

        public List<SqlReportExecutionLog> ExecutionLogs { get; } = new();

        public SqlReportParameter DeletedParameter { get; private set; }

        public SqlReportParameter Parameter { get; set; }

        public SqlReport Report { get; set; }

        public bool ReportDeleted { get; private set; }

        public override Task<SqlReport> GetReportByIdAsync(int reportId)
        {
            return Task.FromResult(Report?.Id == reportId ? Report : null);
        }

        public override Task<IList<SqlReportParameter>> GetReportParametersAsync(int reportId)
        {
            return Task.FromResult<IList<SqlReportParameter>>(new List<SqlReportParameter>());
        }

        public override Task<bool> CanRunReportAsync(SqlReport report, Customer customer)
        {
            return Task.FromResult(report != null && customer != null);
        }

        public override Task InsertExecutionLogAsync(SqlReportExecutionLog executionLog)
        {
            ExecutionLogs.Add(executionLog);

            return Task.CompletedTask;
        }

        public override Task DeleteReportAsync(SqlReport report)
        {
            foreach (var executionLog in ExecutionLogs.Where(log => log.SqlReportId == report.Id))
                executionLog.SqlReportId = null;

            ReportDeleted = true;

            return Task.CompletedTask;
        }

        public override Task<SqlReportParameter> GetParameterByIdAsync(int parameterId)
        {
            return Task.FromResult(Parameter?.Id == parameterId ? Parameter : null);
        }

        public override Task InsertParameterAsync(SqlReportParameter parameter)
        {
            parameter.Id = 11;
            Parameter = parameter;

            return Task.CompletedTask;
        }

        public override Task UpdateParameterAsync(SqlReportParameter parameter)
        {
            Parameter = parameter;

            return Task.CompletedTask;
        }

        public override Task DeleteParameterAsync(SqlReportParameter parameter)
        {
            DeletedParameter = parameter;

            return Task.CompletedTask;
        }

        public override Task SaveParameterOptionsAsync(int parameterId, IList<SqlReportParameterOption> options)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestSqlReportsAdminController : SqlReportsAdminController
    {
        public TestSqlReportsAdminController(ILocalizationService localizationService,
            ICustomerActivityService customerActivityService,
            INotificationService notificationService,
            ISettingService settingService,
            IWorkContext workContext,
            SqlReportExecutionService executionService,
            SqlReportModelFactory modelFactory,
            SqlReportService sqlReportService,
            SqlReportsSettings settings)
            : base(localizationService,
                customerActivityService,
                notificationService,
                settingService,
                workContext,
                executionService,
                modelFactory,
                sqlReportService,
                settings)
        {
        }

        protected override IActionResult AccessDeniedView()
        {
            return new ForbidResult();
        }
    }

    private sealed class FakeExecutionService : SqlReportExecutionService
    {
        public FakeExecutionService(SqlReportsSettings settings)
            : base(settings)
        {
        }

        public int ExecuteCallCount { get; private set; }

        public SqlReportExecutionResult ExportedResult { get; private set; }

        public int? LastMaxRows { get; private set; }

        public override Task<SqlReportExecutionResult> ExecuteAsync(string sql,
            IEnumerable<SqlReportParameter> knownParameters,
            IDictionary<string, string> values,
            int? maxRows = null)
        {
            ExecuteCallCount++;
            LastMaxRows = maxRows;

            return Task.FromResult(new SqlReportExecutionResult
            {
                Columns = { "Value" },
                Rows =
                {
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Value"] = 1
                    }
                },
                RowsReturned = 1,
                ElapsedMilliseconds = 25
            });
        }

        public override byte[] ExportToXlsx(SqlReportExecutionResult result)
        {
            ExportedResult = result;

            return new[] { (byte)'P', (byte)'K' };
        }
    }

    private sealed class FakeSqlReportModelFactory : SqlReportModelFactory
    {
        public FakeSqlReportModelFactory(SqlReportService sqlReportService, SqlReportsSettings settings)
            : base(new Mock<IBaseAdminModelFactory>().Object,
                new Mock<ICustomerService>().Object,
                new Mock<IPermissionService>().Object,
                new Mock<IWorkContext>().Object,
                sqlReportService,
                settings)
        {
        }

        public override Task<SqlReportRunModel> PrepareRunModelAsync(SqlReport report, SqlReportRunModel model = null)
        {
            model ??= new SqlReportRunModel();
            model.Id = report.Id;
            model.Name = report.Name;
            model.AllowExport = true;

            return Task.FromResult(model);
        }
    }
}
