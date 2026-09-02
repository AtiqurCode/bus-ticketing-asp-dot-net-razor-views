using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Notifications;

public static class SmsPurpose
{
    public const string BookingCreated = "booking.created";
    public const string PaymentVerified = "payment.verified";
    public const string PaymentRejected = "payment.rejected";
    public const string BookingCancelled = "booking.cancelled";
}

public sealed class SmsService(
    IDbContextFactory<AppDbContext> dbFactory,
    ISmsSender sender,
    ILogger<SmsService> logger)
{
    public async Task SendAsync(string toPhone, string message, string purpose, Guid? bookingId = null, CancellationToken ct = default)
    {
        var log = new SmsLog
        {
            ToPhone = toPhone,
            Message = message,
            Purpose = purpose,
            BookingId = bookingId
        };

        try
        {
            var result = await sender.SendAsync(toPhone, message, ct);
            log.Sent = result.Succeeded;
            log.ProviderResponse = result.ProviderResponse;
            log.SentAt = result.Succeeded ? DateTimeOffset.UtcNow : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMS send threw for {Phone} ({Purpose}).", toPhone, purpose);
            log.Sent = false;
            log.ProviderResponse = $"Exception: {ex.Message}";
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.SmsLogs.Add(log);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist SMS log for {Phone}.", toPhone);
        }
    }

    public async Task<List<SmsLog>> RecentAsync(int take = 100, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SmsLogs.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }
}
