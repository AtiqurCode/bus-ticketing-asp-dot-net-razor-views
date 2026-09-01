namespace BusTicketing.Services.Scheduling;

/// <summary>
/// Keeps the rolling trip window topped up and trip statuses current. Status
/// advancement runs every tick; the heavier top-up runs a few times a day.
/// </summary>
public sealed class TripGenerationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<TripGenerationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TopUpInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting before the first pass.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var lastTopUp = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TickInterval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var generator = scope.ServiceProvider.GetRequiredService<TripGenerationService>();

                await generator.AdvanceStatusesAsync(stoppingToken);

                if (DateTimeOffset.UtcNow - lastTopUp >= TopUpInterval)
                {
                    await generator.TopUpAsync(stoppingToken);
                    lastTopUp = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Trip generation background pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
