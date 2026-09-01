namespace BusTicketing.Services.Bookings;

/// <summary>
/// Frees abandoned seat holds and expires unpaid reservations so seat counts
/// stay honest. Runs on a short cycle because holds are only minutes long.
/// </summary>
public sealed class BookingMaintenanceBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<BookingMaintenanceBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var holds = scope.ServiceProvider.GetRequiredService<SeatHoldService>();
                var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

                await holds.SweepExpiredAsync(ct: stoppingToken);
                await bookings.ExpireStaleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Booking maintenance pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
