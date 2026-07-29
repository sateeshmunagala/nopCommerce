using Moq;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Data;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class CompletionRecoveryTaskDuplicateMigrationTests
{
    [Test]
    public void UpgradeRepair_PreviouslyRecordedAI45PlainDuplicates_RetainsLowestPlainAndSecondRunHasNoWrites()
    {
        var retainedPlainTask = CreateTask(4, AIInterviewDefaults.CompletionRecoveryTaskType);
        retainedPlainTask.Name = "Customized completion recovery";
        retainedPlainTask.Seconds = 743;
        retainedPlainTask.Enabled = false;
        retainedPlainTask.StopOnError = true;
        retainedPlainTask.LastEnabledUtc = new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc);
        retainedPlainTask.LastStartUtc = new DateTime(2026, 7, 22, 8, 0, 0, DateTimeKind.Utc);
        retainedPlainTask.LastEndUtc = new DateTime(2026, 7, 22, 8, 3, 0, DateTimeKind.Utc);
        retainedPlainTask.LastSuccessUtc = new DateTime(2026, 7, 22, 8, 3, 0, DateTimeKind.Utc);
        var middlePlainTask = CreateTask(11, AIInterviewDefaults.CompletionRecoveryTaskType);
        var highestPlainTask = CreateTask(18, AIInterviewDefaults.CompletionRecoveryTaskType);
        var unrelatedTask = CreateTask(2, "Nop.Plugin.Other.Task");
        var tasks = new List<ScheduleTask>
        {
            highestPlainTask,
            unrelatedTask,
            retainedPlainTask,
            middlePlainTask
        };
        var dataProvider = CreateDataProvider(tasks);
        var migration = new CompletionRecoveryTaskDuplicateMigration(dataProvider.Object);

        migration.Up();

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EquivalentTo(new[] { unrelatedTask, retainedPlainTask }));
            Assert.That(tasks.Count(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType), Is.EqualTo(1));
            Assert.That(retainedPlainTask.Name, Is.EqualTo("Customized completion recovery"));
            Assert.That(retainedPlainTask.Seconds, Is.EqualTo(743));
            Assert.That(retainedPlainTask.Enabled, Is.False);
            Assert.That(retainedPlainTask.StopOnError, Is.True);
            Assert.That(retainedPlainTask.LastEnabledUtc, Is.EqualTo(new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc)));
            Assert.That(retainedPlainTask.LastStartUtc, Is.EqualTo(new DateTime(2026, 7, 22, 8, 0, 0, DateTimeKind.Utc)));
            Assert.That(retainedPlainTask.LastEndUtc, Is.EqualTo(new DateTime(2026, 7, 22, 8, 3, 0, DateTimeKind.Utc)));
            Assert.That(retainedPlainTask.LastSuccessUtc, Is.EqualTo(new DateTime(2026, 7, 22, 8, 3, 0, DateTimeKind.Utc)));
        });
        dataProvider.Verify(provider => provider.DeleteEntity(middlePlainTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(highestPlainTask), Times.Once);
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ScheduleTask>()), Times.Never);

        dataProvider.Invocations.Clear();
        migration.Up();

        dataProvider.Verify(provider => provider.InsertEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public void UpgradeRepair_MixedPlainAndLegacyDuplicates_RetainsLowestPlainAndIgnoresUnsupportedTypes()
    {
        var lowerLegacyTask = CreateTask(1, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var retainedPlainTask = CreateTask(6, AIInterviewDefaults.CompletionRecoveryTaskType);
        var higherLegacyTask = CreateTask(13, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var higherPlainTask = CreateTask(19, AIInterviewDefaults.CompletionRecoveryTaskType);
        var unrelatedTask = CreateTask(3, "Nop.Plugin.Other.Task");
        var currentCaseVariantTask = CreateTask(4, AIInterviewDefaults.CompletionRecoveryTaskType.ToLowerInvariant());
        var legacyCaseVariantTask = CreateTask(5, AIInterviewDefaults.LegacyCompletionRecoveryTaskType.ToUpperInvariant());
        var tasks = new List<ScheduleTask>
        {
            higherPlainTask,
            legacyCaseVariantTask,
            lowerLegacyTask,
            unrelatedTask,
            higherLegacyTask,
            currentCaseVariantTask,
            retainedPlainTask
        };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskDuplicateMigration(dataProvider.Object).Up();

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EquivalentTo(new[]
            {
                unrelatedTask,
                currentCaseVariantTask,
                legacyCaseVariantTask,
                retainedPlainTask
            }));
            Assert.That(tasks.Count(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType), Is.EqualTo(1));
            Assert.That(retainedPlainTask.Id, Is.EqualTo(6));
        });
        dataProvider.Verify(provider => provider.DeleteEntity(lowerLegacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(higherLegacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(higherPlainTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(unrelatedTask), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(currentCaseVariantTask), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(legacyCaseVariantTask), Times.Never);
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public void UpgradeRepair_LegacyOnlyDuplicates_ConvertsLowestIdentifierAndPreservesFields()
    {
        var higherLegacyTask = CreateTask(15, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var retainedLegacyTask = CreateTask(2, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        retainedLegacyTask.Name = "Legacy customized completion recovery";
        retainedLegacyTask.Seconds = 881;
        retainedLegacyTask.Enabled = false;
        retainedLegacyTask.StopOnError = true;
        retainedLegacyTask.LastStartUtc = new DateTime(2026, 7, 23, 9, 0, 0, DateTimeKind.Utc);
        retainedLegacyTask.LastSuccessUtc = new DateTime(2026, 7, 23, 9, 4, 0, DateTimeKind.Utc);
        var middleLegacyTask = CreateTask(8, AIInterviewDefaults.LegacyCompletionRecoveryTaskType);
        var tasks = new List<ScheduleTask> { higherLegacyTask, retainedLegacyTask, middleLegacyTask };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskDuplicateMigration(dataProvider.Object).Up();

        Assert.Multiple(() =>
        {
            Assert.That(tasks, Is.EqualTo(new[] { retainedLegacyTask }));
            Assert.That(retainedLegacyTask.Id, Is.EqualTo(2));
            Assert.That(retainedLegacyTask.Type, Is.EqualTo(AIInterviewDefaults.CompletionRecoveryTaskType));
            Assert.That(retainedLegacyTask.Name, Is.EqualTo("Legacy customized completion recovery"));
            Assert.That(retainedLegacyTask.Seconds, Is.EqualTo(881));
            Assert.That(retainedLegacyTask.Enabled, Is.False);
            Assert.That(retainedLegacyTask.StopOnError, Is.True);
            Assert.That(retainedLegacyTask.LastStartUtc, Is.EqualTo(new DateTime(2026, 7, 23, 9, 0, 0, DateTimeKind.Utc)));
            Assert.That(retainedLegacyTask.LastSuccessUtc, Is.EqualTo(new DateTime(2026, 7, 23, 9, 4, 0, DateTimeKind.Utc)));
        });
        dataProvider.Verify(provider => provider.UpdateEntity(retainedLegacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(middleLegacyTask), Times.Once);
        dataProvider.Verify(provider => provider.DeleteEntity(higherLegacyTask), Times.Once);
        dataProvider.Verify(provider => provider.InsertEntity(It.IsAny<ScheduleTask>()), Times.Never);
    }

    [Test]
    public void UpgradeRepair_UnsupportedAndCaseVariantTypes_AreNoOp()
    {
        var unrelatedTask = CreateTask(21, "Nop.Plugin.Other.Task");
        var currentCaseVariantTask = CreateTask(22, AIInterviewDefaults.CompletionRecoveryTaskType.ToUpperInvariant());
        var legacyCaseVariantTask = CreateTask(23, AIInterviewDefaults.LegacyCompletionRecoveryTaskType.ToLowerInvariant());
        var tasks = new List<ScheduleTask> { unrelatedTask, currentCaseVariantTask, legacyCaseVariantTask };
        var dataProvider = CreateDataProvider(tasks);

        new CompletionRecoveryTaskDuplicateMigration(dataProvider.Object).Up();

        Assert.That(tasks, Is.EqualTo(new[] { unrelatedTask, currentCaseVariantTask, legacyCaseVariantTask }));
        dataProvider.Verify(provider => provider.InsertEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ScheduleTask>()), Times.Never);
        dataProvider.Verify(provider => provider.DeleteEntity(It.IsAny<ScheduleTask>()), Times.Never);
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
}
