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
    public async Task Installation_CreatesOnePlainTaskWhenNeitherRepresentationExists()
    {
        var scheduleTaskService = new Mock<IScheduleTaskService>();
        scheduleTaskService
            .Setup(service => service.GetTaskByTypeAsync(It.IsAny<string>()))
            .ReturnsAsync((ScheduleTask)null);
        var plugin = CreatePlugin(scheduleTaskService.Object);

        await InvokePrivateTaskAsync(plugin, "EnsureCompletionRecoveryTaskAsync");

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
        var scheduleTaskService = new Mock<IScheduleTaskService>();
        scheduleTaskService
            .Setup(service => service.GetTaskByTypeAsync(AIInterviewDefaults.CompletionRecoveryTaskType))
            .ReturnsAsync(plainTask);
        scheduleTaskService
            .Setup(service => service.GetTaskByTypeAsync(AIInterviewDefaults.LegacyCompletionRecoveryTaskType))
            .ReturnsAsync(legacyTask);
        var plugin = CreatePlugin(scheduleTaskService.Object);

        await InvokePrivateTaskAsync(plugin, "EnsureCompletionRecoveryTaskAsync");

        scheduleTaskService.Verify(service => service.DeleteTaskAsync(legacyTask), Times.Once);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(plainTask), Times.Never);
        scheduleTaskService.Verify(service => service.InsertTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
        scheduleTaskService.Verify(service => service.UpdateTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public async Task Installation_RepairsLegacyTaskInsteadOfCreatingAnotherTask()
    {
        var legacyTask = CreateTask(41, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var scheduleTaskService = new Mock<IScheduleTaskService>();
        scheduleTaskService
            .Setup(service => service.GetTaskByTypeAsync(AIInterviewDefaults.CompletionRecoveryTaskType))
            .ReturnsAsync((ScheduleTask)null);
        scheduleTaskService
            .Setup(service => service.GetTaskByTypeAsync(AIInterviewDefaults.LegacyCompletionRecoveryTaskType))
            .ReturnsAsync(legacyTask);
        var plugin = CreatePlugin(scheduleTaskService.Object);

        await InvokePrivateTaskAsync(plugin, "EnsureCompletionRecoveryTaskAsync");

        Assert.That(legacyTask.Type, Is.EqualTo(AIInterviewDefaults.CompletionRecoveryTaskType));
        scheduleTaskService.Verify(service => service.UpdateTaskAsync(legacyTask), Times.Once);
        scheduleTaskService.Verify(service => service.InsertTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public async Task Uninstallation_RemovesPlainAndLegacyTaskRepresentations()
    {
        var plainTask = CreateTask(51, AIInterviewDefaults.CompletionRecoveryTaskType);
        var legacyTask = CreateTask(52, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var scheduleTaskService = new Mock<IScheduleTaskService>();
        scheduleTaskService
            .Setup(service => service.GetTaskByTypeAsync(AIInterviewDefaults.CompletionRecoveryTaskType))
            .ReturnsAsync(plainTask);
        scheduleTaskService
            .Setup(service => service.GetTaskByTypeAsync(AIInterviewDefaults.LegacyCompletionRecoveryTaskType))
            .ReturnsAsync(legacyTask);
        var plugin = CreatePlugin(scheduleTaskService.Object);

        await InvokePrivateTaskAsync(plugin, "DeleteCompletionRecoveryTasksAsync");

        scheduleTaskService.Verify(service => service.DeleteTaskAsync(plainTask), Times.Once);
        scheduleTaskService.Verify(service => service.DeleteTaskAsync(legacyTask), Times.Once);
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
