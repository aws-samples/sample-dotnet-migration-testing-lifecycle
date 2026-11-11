using PayorContracts.Host.Contracts;

namespace PayorContracts.Scheduler;

/// <summary>
/// Replaces: the original terminate-contract scheduler Windows Service / Task Scheduler job.
/// Runs on a 1-hour interval. Calls CoreWCF service using Windows auth via Channel Factory.
/// </summary>
public class TerminateContractWorker : BackgroundService
{
    private readonly IPayorInformation _payorService;
    private readonly ILogger<TerminateContractWorker> _logger;

    public TerminateContractWorker(
        IPayorInformation payorService,
        ILogger<TerminateContractWorker> logger)
    {
        _payorService = payorService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("TerminateContract job starting");

                var result = _payorService.GetAllPayors();

                _logger.LogInformation(
                    "TerminateContract job completed. Processed {Count} records", result.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TerminateContract job failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
