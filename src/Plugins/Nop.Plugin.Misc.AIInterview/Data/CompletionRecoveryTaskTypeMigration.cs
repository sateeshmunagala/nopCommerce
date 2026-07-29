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
        var completionTasks = _dataProvider.GetTable<ScheduleTask>()
            .Where(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType ||
                task.Type == AIInterviewDefaults.LegacyCompletionRecoveryTaskType)
            .ToList();

        var completionTask = completionTasks
            .FirstOrDefault(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType);
        var legacyCompletionTasks = completionTasks
            .Where(task => task.Type == AIInterviewDefaults.LegacyCompletionRecoveryTaskType)
            .ToList();

        if (completionTask != null)
        {
            foreach (var legacyCompletionTask in legacyCompletionTasks)
                _dataProvider.DeleteEntity(legacyCompletionTask);

            return;
        }

        var repairedCompletionTask = legacyCompletionTasks.FirstOrDefault();
        if (repairedCompletionTask == null)
            return;

        repairedCompletionTask.Type = AIInterviewDefaults.CompletionRecoveryTaskType;
        _dataProvider.UpdateEntity(repairedCompletionTask);

        foreach (var legacyCompletionTask in legacyCompletionTasks.Skip(1))
            _dataProvider.DeleteEntity(legacyCompletionTask);
    }

    public override void Down()
    {
    }
}
