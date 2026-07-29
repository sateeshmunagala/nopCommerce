using System.Reflection;
using Moq;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Data;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.ScheduleTasks;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class CompletionRecoveryTaskTypeTests
{
    [Test]
    public void Defaults_ExposeResolvablePlainCompletionRecoveryTaskType()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AIInterviewDefaults.CompletionRecoveryTaskType, Is.EqualTo(typeof(InterviewCompletionRecoveryTask).FullName));
            Assert.That(AIInterviewDefaults.CompletionRecoveryTaskType, Does.Not.Contain(","));
            Assert.That(AIInterviewDefaults.LegacyCompletionRecoveryTaskType,
                Is.EqualTo($"{typeof(InterviewCompletionRecoveryTask).FullName}, Nop.Plugin.Misc.AIInterview"));
            Assert.That(typeof(InterviewCompletionRecoveryTask).IsPublic, Is.True);
            Assert.That(typeof(IScheduleTask).IsAssignableFrom(typeof(InterviewCompletionRecoveryTask)), Is.True);
        });
    }

    [Test]
    public void Migration_RepairsLoneLegacyTaskInPlace()
    {
        var legacyTask = CreateTask(7, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var tasks = new List<ScheduleTask> { legacyTask };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskTypeMigration(dataProvider.Object).Up();

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EqualTo(new[] { legacyTask }));
            Assert.That(legacyTask.Id, Is.EqualTo(7));
            Assert.That(legacyTask.Type, Is.EqualTo(AIInterviewDefaults.CompletionRecoveryTaskType));
        });
        dataProvider.Verify(provider => provider.UpdateEntity(legacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public void Migration_LegacyDuplicates_RepairsLowestIdentifierAndDeletesOthers()
    {
        var higherLegacyTask = CreateTask(17, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var retainedLegacyTask = CreateTask(3, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        retainedLegacyTask.Name = "Legacy customized completion recovery";
        retainedLegacyTask.Seconds = 619;
        retainedLegacyTask.Enabled = false;
        retainedLegacyTask.StopOnError = true;
        retainedLegacyTask.LastSuccessUtc = new DateTime(2026, 7, 17, 9, 30, 0, DateTimeKind.Utc);
        var middleLegacyTask = CreateTask(9, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var tasks = new List<ScheduleTask> { higherLegacyTask, retainedLegacyTask, middleLegacyTask };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskTypeMigration(dataProvider.Object).Up();

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EqualTo(new[] { retainedLegacyTask }));
            Assert.That(retainedLegacyTask.Id, Is.EqualTo(3));
            Assert.That(retainedLegacyTask.Type, Is.EqualTo(AIInterviewDefaults.CompletionRecoveryTaskType));
            Assert.That(retainedLegacyTask.Name, Is.EqualTo("Legacy customized completion recovery"));
            Assert.That(retainedLegacyTask.Seconds, Is.EqualTo(619));
            Assert.That(retainedLegacyTask.Enabled, Is.False);
            Assert.That(retainedLegacyTask.StopOnError, Is.True);
            Assert.That(retainedLegacyTask.LastSuccessUtc, Is.EqualTo(new DateTime(2026, 7, 17, 9, 30, 0, DateTimeKind.Utc)));
        });
        dataProvider.Verify(provider => provider.UpdateEntity(retainedLegacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(higherLegacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(middleLegacyTask), Times.Once);
    }

    [Test]
    public void Migration_PreservesLonePlainTaskUnchanged()
    {
        var plainTask = CreateTask(8, AIInterviewDefaults.CompletionRecoveryTaskType);
        plainTask.Enabled = false;
        plainTask.Seconds = 913;
        plainTask.Name = "Existing completion recovery";
        plainTask.StopOnError = true;
        plainTask.LastStartUtc = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        var tasks = new List<ScheduleTask> { plainTask };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskTypeMigration(dataProvider.Object).Up();

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EqualTo(new[] { plainTask }));
            Assert.That(plainTask.Enabled, Is.False);
            Assert.That(plainTask.Seconds, Is.EqualTo(913));
            Assert.That(plainTask.Name, Is.EqualTo("Existing completion recovery"));
            Assert.That(plainTask.StopOnError, Is.True);
            Assert.That(plainTask.LastStartUtc, Is.EqualTo(new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc)));
        });
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public void Migration_SeveralPlainDuplicates_RetainsLowestIdentifier()
    {
        var highestPlainTask = CreateTask(19, AIInterviewDefaults.CompletionRecoveryTaskType);
        var retainedPlainTask = CreateTask(4, AIInterviewDefaults.CompletionRecoveryTaskType);
        var middlePlainTask = CreateTask(12, AIInterviewDefaults.CompletionRecoveryTaskType);
        var unrelatedTask = CreateTask(2, "Nop.Plugin.Other.Task");
        var caseVariantTask = CreateTask(3, AIInterviewDefaults.CompletionRecoveryTaskType.ToLowerInvariant());
        var tasks = new List<ScheduleTask>
        {
            highestPlainTask,
            unrelatedTask,
            caseVariantTask,
            retainedPlainTask,
            middlePlainTask
        };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskTypeMigration(dataProvider.Object).Up();

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EquivalentTo(new[] { unrelatedTask, caseVariantTask, retainedPlainTask }));
            Assert.That(tasks.Count(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType), Is.EqualTo(1));
            Assert.That(retainedPlainTask.Id, Is.EqualTo(4));
        });
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(highestPlainTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(middlePlainTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(unrelatedTask), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(caseVariantTask), Times.Never);
    }

    [Test]
    public void Migration_PlainAndLegacyDuplicates_RetainsLowestPlainIdentifier()
    {
        var lowerLegacyTask = CreateTask(1, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var higherLegacyTask = CreateTask(24, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var retainedPlainTask = CreateTask(7, AIInterviewDefaults.CompletionRecoveryTaskType);
        var higherPlainTask = CreateTask(18, AIInterviewDefaults.CompletionRecoveryTaskType);
        var tasks = new List<ScheduleTask>
        {
            higherPlainTask,
            higherLegacyTask,
            lowerLegacyTask,
            retainedPlainTask
        };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskTypeMigration(dataProvider.Object).Up();

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EqualTo(new[] { retainedPlainTask }));
            Assert.That(retainedPlainTask.Id, Is.EqualTo(7));
            Assert.That(retainedPlainTask.Type, Is.EqualTo(AIInterviewDefaults.CompletionRecoveryTaskType));
        });
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(lowerLegacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(higherLegacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(higherPlainTask), Times.Once);
    }

    [Test]
    public void Migration_PreservesPlainTaskAndDeletesLegacyDuplicate()
    {
        var plainTask = CreateTask(11, AIInterviewDefaults.CompletionRecoveryTaskType);
        var legacyTask = CreateTask(12, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var unrelatedTask = CreateTask(13, "Nop.Plugin.Other.Task");
        var tasks = new List<ScheduleTask> { plainTask, legacyTask, unrelatedTask };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskTypeMigration(dataProvider.Object).Up();

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Does.Contain(plainTask));
            Assert.That(tasks, Does.Contain(unrelatedTask));
            Assert.That(tasks, Does.Not.Contain(legacyTask));
            Assert.That(plainTask.Type, Is.EqualTo(AIInterviewDefaults.CompletionRecoveryTaskType));
        });
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(legacyTask), Times.Once);
    }

    [Test]
    public void Migration_IsIdempotent()
    {
        var legacyTask = CreateTask(21, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var tasks = new List<ScheduleTask> { legacyTask };
        var dataProvider = CreateDataProvider(tasks);
        var migration = new CompletionRecoveryTaskTypeMigration(dataProvider.Object);

        migration.Up();
        migration.Up();

        Assert.That(tasks.Single().Type, Is.EqualTo(AIInterviewDefaults.CompletionRecoveryTaskType));
        dataProvider.Verify(provider => provider.UpdateEntity(legacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public void Migration_WithNoSupportedRepresentation_IsNoOp()
    {
        var unrelatedTask = CreateTask(22, "Nop.Plugin.Other.Task");
        var tasks = new List<ScheduleTask> { unrelatedTask };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskTypeMigration(dataProvider.Object).Up();

        Assert.That(tasks, Is.EqualTo(new[] { unrelatedTask }));
        dataProvider.Verify(provider => provider.InsertEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public async Task Installation_CreatesOnePlainTaskWhenNeitherRepresentationExists()
    {
        var tasks = new List<ScheduleTask>();
        var scheduleTaskService = CreateScheduleTaskService(tasks);
        var plugin = CreatePlugin(scheduleTaskService.Object);

        await InvokePrivateTaskAsync(plugin, "EnsureCompletionRecoveryTaskAsync");

        Assert.That(tasks.Count(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType), Is.EqualTo(1));
        scheduleTaskService.Verify(service => service.InsertTaskAsync(It.Is<ScheduleTask>(task =>
            task.Type == AIInterviewDefaults.CompletionRecoveryTaskType &&
            task.Name == AIInterviewDefaults.CompletionRecoveryTaskName &&
            task.Seconds == AIInterviewDefaults.CompletionRecoveryTaskPeriodSeconds)), Times.Once);
        scheduleTaskService.Verify(service => service.UpdateTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public async Task Installation_PreservesPlainTaskAndRemovesLegacyDuplicate()
    {
        var plainTask = CreateTask(31, AIInterviewDefaults.CompletionRecoveryTaskType);
        var legacyTask = CreateTask(32, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var tasks = new List<ScheduleTask> { plainTask, legacyTask };
        var scheduleTaskService = CreateScheduleTaskService(tasks);
        var plugin = CreatePlugin(scheduleTaskService.Object);

        await InvokePrivateTaskAsync(plugin, "EnsureCompletionRecoveryTaskAsync");

        Assert.That(tasks, Is.EqualTo(new[] { plainTask }));
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(legacyTask), Times.Once);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(plainTask), Times.Never);
        scheduleTaskService.Verify(service => service.InsertTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
        scheduleTaskService.Verify(service => service.UpdateTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public async Task Installation_ConsolidatesPlainDuplicatesWithoutChangingRetainedSchedule()
    {
        var retainedPlainTask = CreateTask(5, AIInterviewDefaults.CompletionRecoveryTaskType);
        retainedPlainTask.Name = "Customized completion recovery";
        retainedPlainTask.Seconds = 827;
        retainedPlainTask.Enabled = false;
        retainedPlainTask.StopOnError = true;
        retainedPlainTask.LastEnabledUtc = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        retainedPlainTask.LastStartUtc = new DateTime(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc);
        retainedPlainTask.LastEndUtc = new DateTime(2026, 7, 19, 8, 2, 0, DateTimeKind.Utc);
        retainedPlainTask.LastSuccessUtc = new DateTime(2026, 7, 19, 8, 2, 0, DateTimeKind.Utc);
        var higherPlainTask = CreateTask(15, AIInterviewDefaults.CompletionRecoveryTaskType);
        var unrelatedTask = CreateTask(1, "Nop.Plugin.Other.Task");
        var tasks = new List<ScheduleTask> { higherPlainTask, unrelatedTask, retainedPlainTask };
        var scheduleTaskService = CreateScheduleTaskService(tasks);
        var plugin = CreatePlugin(scheduleTaskService.Object);

        await InvokePrivateTaskAsync(plugin, "EnsureCompletionRecoveryTaskAsync");

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EquivalentTo(new[] { unrelatedTask, retainedPlainTask }));
            Assert.That(tasks.Count(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType), Is.EqualTo(1));
            Assert.That(retainedPlainTask.Name, Is.EqualTo("Customized completion recovery"));
            Assert.That(retainedPlainTask.Seconds, Is.EqualTo(827));
            Assert.That(retainedPlainTask.Enabled, Is.False);
            Assert.That(retainedPlainTask.StopOnError, Is.True);
            Assert.That(retainedPlainTask.LastEnabledUtc, Is.EqualTo(new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc)));
            Assert.That(retainedPlainTask.LastStartUtc, Is.EqualTo(new DateTime(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc)));
            Assert.That(retainedPlainTask.LastEndUtc, Is.EqualTo(new DateTime(2026, 7, 19, 8, 2, 0, DateTimeKind.Utc)));
            Assert.That(retainedPlainTask.LastSuccessUtc, Is.EqualTo(new DateTime(2026, 7, 19, 8, 2, 0, DateTimeKind.Utc)));
        });
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(higherPlainTask), Times.Once);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(unrelatedTask), Times.Never);
        scheduleTaskService.Verify(service => service.InsertTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
        scheduleTaskService.Verify(service => service.UpdateTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public async Task Installation_RepairsLegacyTaskInsteadOfCreatingAnotherTask()
    {
        var legacyTask = CreateTask(41, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var tasks = new List<ScheduleTask> { legacyTask };
        var scheduleTaskService = CreateScheduleTaskService(tasks);
        var plugin = CreatePlugin(scheduleTaskService.Object);

        await InvokePrivateTaskAsync(plugin, "EnsureCompletionRecoveryTaskAsync");

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EqualTo(new[] { legacyTask }));
            Assert.That(legacyTask.Type, Is.EqualTo(AIInterviewDefaults.CompletionRecoveryTaskType));
        });
        scheduleTaskService.Verify(service => service.UpdateTaskAsync(legacyTask), Times.Once);
        scheduleTaskService.Verify(service => service.InsertTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public async Task Uninstallation_RemovesAllPlainAndLegacyDuplicates_AndPreservesUnrelatedTasks()
    {
        var firstPlainTask = CreateTask(51, AIInterviewDefaults.CompletionRecoveryTaskType);
        var secondPlainTask = CreateTask(52, AIInterviewDefaults.CompletionRecoveryTaskType);
        var firstLegacyTask = CreateTask(53, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var secondLegacyTask = CreateTask(54, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var unrelatedTask = CreateTask(55, "Nop.Plugin.Other.Task");
        var caseVariantTask = CreateTask(56, AIInterviewDefaults.CompletionRecoveryTaskType.ToUpperInvariant());
        var tasks = new List<ScheduleTask>
        {
            firstPlainTask,
            unrelatedTask,
            secondLegacyTask,
            secondPlainTask,
            firstLegacyTask,
            caseVariantTask
        };
        var scheduleTaskService = CreateScheduleTaskService(tasks);
        var plugin = CreatePlugin(scheduleTaskService.Object);

        await InvokePrivateTaskAsync(plugin, "DeleteCompletionRecoveryTasksAsync");

        Assert.That(tasks, Is.EqualTo(new[] { unrelatedTask, caseVariantTask }));
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(firstPlainTask), Times.Once);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(secondPlainTask), Times.Once);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(firstLegacyTask), Times.Once);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(secondLegacyTask), Times.Once);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(unrelatedTask), Times.Never);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(caseVariantTask), Times.Never);
    }

    private static Mock<INopDataProvider> CreateDataProvider(List<ScheduleTask> tasks)
    {
        var dataProvider = new Mock<INopDataProvider>();
        dataProvider
            .Setup(provider => provider.GetTable<ScheduleTask>())
            .Returns(() => tasks.AsQueryable());
        dataProvider
            .Setup(provider => provider.DeleteEntity(It.IsAny<ScheduleTask>()))
            .Callback<ScheduleTask>(task => tasks.Remove(task));
        return dataProvider;
    }

    private static Mock<IScheduleTaskService> CreateScheduleTaskService(List<ScheduleTask> tasks)
    {
        var scheduleTaskService = new Mock<IScheduleTaskService>();
        scheduleTaskService
            .Setup(service => service.GetAllTasksAsync(true))
            .ReturnsAsync(() => tasks.ToList());
        scheduleTaskService
            .Setup(service => service.DeleteTaskAsync(It.IsAny<ScheduleTask>()))
            .Callback<ScheduleTask>(task => tasks.Remove(task))
            .Returns(Task.CompletedTask);
        scheduleTaskService
            .Setup(service => service.InsertTaskAsync(It.IsAny<ScheduleTask>()))
            .Callback<ScheduleTask>(task => tasks.Add(task))
            .Returns(Task.CompletedTask);
        scheduleTaskService
            .Setup(service => service.UpdateTaskAsync(It.IsAny<ScheduleTask>()))
            .Returns(Task.CompletedTask);
        return scheduleTaskService;
    }

    private static ScheduleTask CreateTask(int id, string type)
    {
        return new ScheduleTask
        {
            Id = id,
            Name = AIInterviewDefaults.CompletionRecoveryTaskName,
            Type = type,
            Seconds = AIInterviewDefaults.CompletionRecoveryTaskPeriodSeconds,
            Enabled = true
        };
    }

    private static AIInterviewPlugin CreatePlugin(IScheduleTaskService scheduleTaskService)
    {
        return new AIInterviewPlugin(
            new Mock<ILocalizationService>().Object,
            new Mock<ISettingService>().Object,
            new Mock<IWebHelper>().Object,
            new Mock<IMessageTemplateService>().Object,
            scheduleTaskService: scheduleTaskService);
    }

    private static async Task InvokePrivateTaskAsync(AIInterviewPlugin plugin, string methodName)
    {
        var method = typeof(AIInterviewPlugin).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);
        await (Task)method.Invoke(plugin, null);
    }
}
