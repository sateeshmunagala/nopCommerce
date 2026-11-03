using FluentMigrator;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/29 12:00:00", "Misc.AIInterview repair completion recovery task type", MigrationProcessType.Update)]
public class CompletionRecoveryTaskTypeMigration : Migration
{
    private readonly INopDataProvider _dataProvider;

    public CompletionRecoveryTaskTypeMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        var completionTaskCandidates = _dataProvider.GetTable<ScheduleTask>()
            .Where(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType ||
                task.Type == AIInterviewDefaults.LegacyCompletionRecoveryTaskType)
            .ToList();
        var completionTasks = completionTaskCandidates
            .Where(task =>
                string.Equals(task.Type, AIInterviewDefaults.CompletionRecoveryTaskType, StringComparison.Ordinal) ||
                string.Equals(task.Type, AIInterviewDefaults.LegacyCompletionRecoveryTaskType, StringComparison.Ordinal))
            .OrderBy(task => task.Id)
            .ToList();

        var completionTask = completionTasks
            .FirstOrDefault(task => string.Equals(task.Type, AIInterviewDefaults.CompletionRecoveryTaskType, StringComparison.Ordinal));
        var retainedTask = completionTask ?? completionTasks.FirstOrDefault();
        if (retainedTask == null)
            return;

        if (string.Equals(retainedTask.Type, AIInterviewDefaults.LegacyCompletionRecoveryTaskType, StringComparison.Ordinal))
        {
            retainedTask.Type = AIInterviewDefaults.CompletionRecoveryTaskType;
            _dataProvider.UpdateEntity(retainedTask);
        }

        foreach (var duplicateTask in completionTasks.Where(task => !ReferenceEquals(task, retainedTask)))
            _dataProvider.DeleteEntity(duplicateTask);
    }

    public override void Down()
    {
    }
}
