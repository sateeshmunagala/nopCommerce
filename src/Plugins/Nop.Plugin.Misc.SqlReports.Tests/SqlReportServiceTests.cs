using System.Linq.Expressions;
using Moq;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Misc.SqlReports.Domain;
using Nop.Plugin.Misc.SqlReports.Services;
using Nop.Services.Customers;
using NUnit.Framework;

namespace Nop.Plugin.Misc.SqlReports.Tests;

[TestFixture]
public class SqlReportServiceTests
{
    [Test]
    public async Task DeleteReportAsync_PreservesExecutionLogs_AndNullsReportReference()
    {
        var report = new SqlReport { Id = 10, Name = "Sales report" };
        var reports = new List<SqlReport> { report };
        var roleMappings = new List<SqlReportCustomerRoleMapping>
        {
            new() { Id = 1, SqlReportId = 10, CustomerRoleId = 3 },
            new() { Id = 2, SqlReportId = 11, CustomerRoleId = 4 }
        };
        var parameterMappings = new List<SqlReportParameterMapping>
        {
            new() { Id = 1, SqlReportId = 10, SqlReportParameterId = 5 },
            new() { Id = 2, SqlReportId = 11, SqlReportParameterId = 6 }
        };
        var executionLogs = new List<SqlReportExecutionLog>
        {
            new() { Id = 1, SqlReportId = 10, CustomerId = 1, Success = true },
            new() { Id = 2, SqlReportId = 10, CustomerId = 2, Success = false, Error = "failed" },
            new() { Id = 3, SqlReportId = 11, CustomerId = 3, Success = true }
        };

        var reportRepository = CreateRepository(reports);
        var roleMappingRepository = CreateRepository(roleMappings);
        var parameterMappingRepository = CreateRepository(parameterMappings);
        var executionLogRepository = CreateRepository(executionLogs);

        var service = new SqlReportService(
            new Mock<ICustomerService>().Object,
            new Mock<IRepository<CustomerRole>>().Object,
            reportRepository.Object,
            roleMappingRepository.Object,
            new Mock<IRepository<SqlReportParameter>>().Object,
            parameterMappingRepository.Object,
            new Mock<IRepository<SqlReportParameterOption>>().Object,
            executionLogRepository.Object);

        await service.DeleteReportAsync(report);

        Assert.That(executionLogs, Has.Count.EqualTo(3));
        Assert.That(executionLogs.Where(log => log.Id is 1 or 2).All(log => log.SqlReportId == null), Is.True);
        Assert.That(executionLogs.Single(log => log.Id == 3).SqlReportId, Is.EqualTo(11));
        Assert.That(roleMappings.Select(mapping => mapping.SqlReportId), Is.EqualTo(new[] { 11 }));
        Assert.That(parameterMappings.Select(mapping => mapping.SqlReportId), Is.EqualTo(new[] { 11 }));
        Assert.That(reports, Is.Empty);

        executionLogRepository.Verify(repository => repository.UpdateAsync(
            It.Is<IList<SqlReportExecutionLog>>(logs => logs.Count == 2 && logs.All(log => log.SqlReportId == null)),
            true), Times.Once);
    }

    private static Mock<IRepository<TEntity>> CreateRepository<TEntity>(IList<TEntity> entities)
        where TEntity : Nop.Core.BaseEntity
    {
        var repository = new Mock<IRepository<TEntity>>();

        repository.SetupGet(instance => instance.Table).Returns(() => entities.AsQueryable());
        repository.Setup(instance => instance.UpdateAsync(It.IsAny<IList<TEntity>>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        repository.Setup(instance => instance.DeleteAsync(It.IsAny<TEntity>(), It.IsAny<bool>()))
            .Returns((TEntity entity, bool _) =>
            {
                entities.Remove(entity);

                return Task.CompletedTask;
            });
        repository.Setup(instance => instance.DeleteAsync(It.IsAny<Expression<Func<TEntity, bool>>>()))
            .Returns((Expression<Func<TEntity, bool>> predicate) =>
            {
                var matches = entities.Where(predicate.Compile()).ToList();
                foreach (var match in matches)
                    entities.Remove(match);

                return Task.FromResult(matches.Count);
            });

        return repository;
    }
}
