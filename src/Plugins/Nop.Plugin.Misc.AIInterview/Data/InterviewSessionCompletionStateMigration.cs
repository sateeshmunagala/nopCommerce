using FluentMigrator;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/29 09:00:00", "Misc.AIInterview durable interview completion state", MigrationProcessType.Update)]
public class InterviewSessionCompletionStateMigration : Migration
{
    private const string TableName = nameof(InterviewSession);
    private const string CompletionIndexName = "IX_AIInterview_InterviewSession_CompletionState_ProcessingStartedOnUtc";
    private readonly INopDataProvider _dataProvider;

    public InterviewSessionCompletionStateMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        AddStringColumn(nameof(InterviewSession.CompletionState), 20);
        AddIntColumn(nameof(InterviewSession.CompletionAttemptCount));
        AddDateTimeColumn(nameof(InterviewSession.CompletionQueuedOnUtc));
        AddDateTimeColumn(nameof(InterviewSession.CompletionProcessingStartedOnUtc));
        AddDateTimeColumn(nameof(InterviewSession.CompletionFinishedOnUtc));
        AddDateTimeColumn(nameof(InterviewSession.CompletionPublishedOnUtc));
        AddStringColumn(nameof(InterviewSession.CompletionFailureMessage), 500);
        AddStringColumn(nameof(InterviewSession.CompletionReason), 500);
        AddTextColumn(nameof(InterviewSession.CompletionAiResponse));

        if (!Schema.Table(TableName).Index(CompletionIndexName).Exists())
        {
            Create.Index(CompletionIndexName)
                .OnTable(TableName)
                .OnColumn(nameof(InterviewSession.CompletionState)).Ascending()
                .OnColumn(nameof(InterviewSession.CompletionProcessingStartedOnUtc)).Ascending();
        }

        if (!_dataProvider.GetTable<ScheduleTask>().Any(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType))
        {
            _dataProvider.InsertEntity(new ScheduleTask
            {
                Enabled = true,
                StopOnError = false,
                LastEnabledUtc = DateTime.UtcNow,
                Seconds = AIInterviewDefaults.CompletionRecoveryTaskPeriodSeconds,
                Name = AIInterviewDefaults.CompletionRecoveryTaskName,
                Type = AIInterviewDefaults.CompletionRecoveryTaskType
            });
        }
    }

    public override void Down()
    {
        var completionTask = _dataProvider.GetTable<ScheduleTask>()
            .FirstOrDefault(task => task.Type == AIInterviewDefaults.CompletionRecoveryTaskType);
        if (completionTask != null)
            _dataProvider.DeleteEntity(completionTask);

        if (Schema.Table(TableName).Index(CompletionIndexName).Exists())
            Delete.Index(CompletionIndexName).OnTable(TableName);

        DeleteColumnIfExists(nameof(InterviewSession.CompletionAiResponse));
        DeleteColumnIfExists(nameof(InterviewSession.CompletionReason));
        DeleteColumnIfExists(nameof(InterviewSession.CompletionFailureMessage));
        DeleteColumnIfExists(nameof(InterviewSession.CompletionPublishedOnUtc));
        DeleteColumnIfExists(nameof(InterviewSession.CompletionFinishedOnUtc));
        DeleteColumnIfExists(nameof(InterviewSession.CompletionProcessingStartedOnUtc));
        DeleteColumnIfExists(nameof(InterviewSession.CompletionQueuedOnUtc));
        DeleteColumnIfExists(nameof(InterviewSession.CompletionAttemptCount));
        DeleteColumnIfExists(nameof(InterviewSession.CompletionState));
    }

    protected virtual void AddStringColumn(string columnName, int length)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
            Alter.Table(TableName).AddColumn(columnName).AsString(length).Nullable();
    }

    protected virtual void AddTextColumn(string columnName)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
            Alter.Table(TableName).AddColumn(columnName).AsString(int.MaxValue).Nullable();
    }

    protected virtual void AddIntColumn(string columnName)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
            Alter.Table(TableName).AddColumn(columnName).AsInt32().NotNullable().WithDefaultValue(0);
    }

    protected virtual void AddDateTimeColumn(string columnName)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
            Alter.Table(TableName).AddColumn(columnName).AsDateTime2().Nullable();
    }

    protected virtual void DeleteColumnIfExists(string columnName)
    {
        if (Schema.Table(TableName).Column(columnName).Exists())
            Delete.Column(columnName).FromTable(TableName);
    }
}

[NopMigration("2026/07/29 10:00:00", "Misc.AIInterview bounded completion retry state", MigrationProcessType.Update)]
public class InterviewSessionCompletionRetryMigration : Migration
{
    private const string TableName = nameof(InterviewSession);
    private const string CompletionRetryIndexName = "IX_AIInterview_InterviewSession_CompletionState_NextAttempt_ProcessingStarted";

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(nameof(InterviewSession.CompletionNextAttemptOnUtc)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.CompletionNextAttemptOnUtc))
                .AsDateTime2()
                .Nullable();
        }

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.CompletionFailureDiagnostic)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.CompletionFailureDiagnostic))
                .AsString(2000)
                .Nullable();
        }

        if (!Schema.Table(TableName).Index(CompletionRetryIndexName).Exists())
        {
            Create.Index(CompletionRetryIndexName)
                .OnTable(TableName)
                .OnColumn(nameof(InterviewSession.CompletionState)).Ascending()
                .OnColumn(nameof(InterviewSession.CompletionNextAttemptOnUtc)).Ascending()
                .OnColumn(nameof(InterviewSession.CompletionProcessingStartedOnUtc)).Ascending();
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Index(CompletionRetryIndexName).Exists())
            Delete.Index(CompletionRetryIndexName).OnTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.CompletionFailureDiagnostic)).Exists())
            Delete.Column(nameof(InterviewSession.CompletionFailureDiagnostic)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.CompletionNextAttemptOnUtc)).Exists())
            Delete.Column(nameof(InterviewSession.CompletionNextAttemptOnUtc)).FromTable(TableName);
    }
}
