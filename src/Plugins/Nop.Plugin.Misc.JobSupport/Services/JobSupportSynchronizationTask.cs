using LinqToDB;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Services.Common;
using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportSynchronizationTask : IScheduleTask
{
    private static int _running;

    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;
    private readonly IJobSupportProfileService _profileService;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly JobSupportSettings _settings;

    public JobSupportSynchronizationTask(IGenericAttributeService genericAttributeService,
        ILogger logger,
        IJobSupportProfileService profileService,
        IRepository<Customer> customerRepository,
        IScheduleTaskService scheduleTaskService,
        JobSupportSettings settings)
    {
        _genericAttributeService = genericAttributeService;
        _logger = logger;
        _profileService = profileService;
        _customerRepository = customerRepository;
        _scheduleTaskService = scheduleTaskService;
        _settings = settings;
    }

    public async Task ExecuteAsync()
    {
        if (!_settings.Enabled || !_settings.EnableSynchronizationTask ||
            _settings.ExecutionMode == WorkflowExecutionMode.Disabled)
            return;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        var processed = 0;
        var batchCount = 0;
        try
        {
            var task = await _scheduleTaskService.GetTaskByTypeAsync(JobSupportDefaults.SynchronizationTaskType);
            if (task == null)
                return;

            var batchSize = Math.Max(1, _settings.SynchronizationBatchSize);
            var lastCustomerId = _settings.ExecutionMode == WorkflowExecutionMode.Live
                ? await _genericAttributeService.GetAttributeAsync<int>(task, SynchronizationProgressKey)
                : 0;

            while (true)
            {
                var customers = await _customerRepository.Table
                    .Where(customer => !customer.Deleted && customer.CustomerProfileTypeId > 0 &&
                                       customer.Id > lastCustomerId)
                    .OrderBy(customer => customer.Id)
                    .Take(batchSize)
                    .ToListAsync();
                if (customers.Count == 0)
                    break;

                foreach (var customer in customers)
                    await _profileService.EnsureProfileForCustomerAsync(customer, _settings);

                lastCustomerId = customers[^1].Id;
                processed += customers.Count;
                batchCount++;
                if (_settings.ExecutionMode == WorkflowExecutionMode.Live)
                {
                    await _genericAttributeService.SaveAttributeAsync(task,
                        SynchronizationProgressKey,
                        lastCustomerId);
                }
            }

            if (_settings.ExecutionMode == WorkflowExecutionMode.Live)
                await _genericAttributeService.SaveAttributeAsync<int?>(task, SynchronizationProgressKey, null);

            await _logger.InformationAsync(
                $"JobSupport synchronization summary: batches {batchCount}, records {processed}.");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private static string SynchronizationProgressKey => "JobSupport.SynchronizationLastCustomerId";
}
