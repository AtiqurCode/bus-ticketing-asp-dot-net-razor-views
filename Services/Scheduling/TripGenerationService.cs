using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Scheduling;

public sealed record GenerationSummary(int TemplatesProcessed, int TripsCreated, int SeatsCreated)
{
    public static readonly GenerationSummary Empty = new(0, 0, 0);
    public GenerationSummary Add(int trips, int seats) => this with
    {
        TripsCreated = TripsCreated + trips,
        SeatsCreated = SeatsCreated + seats
    };
}

/// <summary>
/// The recurring-schedule engine. <see cref="TopUpAsync"/> walks every active
/// template and materialises real trips across the rolling booking window,
/// never touching a departure that already has a row (generated, manually
/// overridden, or cancelled).
/// </summary>
public sealed class TripGenerationService(
    IDbContextFactory<AppDbContext> dbFactory,
    SettingsService settings,
    IAppClock clock,
    AuditService audit,
    ILogger<TripGenerationService> logger)
{
    public async Task<GenerationSummary> TopUpAsync(CancellationToken ct = default)
    {
        var config = await settings.GetAsync(ct);
        if (config.AutoGenerationPaused)
        {
            logger.LogInformation("Trip generation is paused — skipping top-up.");
            return GenerationSummary.Empty;
        }

        var firstDate = clock.LocalToday;
        var lastDate = firstDate.AddDays(config.GenerationWindowDays);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var templates = await db.ScheduleTemplates
            .Where(t => t.IsActive)
            .Include(t => t.Route)
            .Include(t => t.Bus)
            .ToListAsync(ct);

        var summary = GenerationSummary.Empty with { TemplatesProcessed = templates.Count };

        foreach (var template in templates)
        {
            if (!template.Route.IsActive || !template.Bus.IsActive || template.Bus.SeatMap.SeatCount == 0)
                continue;

            summary = await GenerateForTemplateAsync(db, template, firstDate, lastDate, summary, ct);
        }

        if (summary.TripsCreated > 0)
        {
            logger.LogInformation("Trip top-up created {Trips} trips ({Seats} seats) from {Templates} templates.",
                summary.TripsCreated, summary.SeatsCreated, summary.TemplatesProcessed);
            await audit.RecordAsync(AuditActions.TripGenerate, nameof(ScheduleTemplate), null,
                $"Auto-generated {summary.TripsCreated} trips across the {config.GenerationWindowDays}-day window",
                actorId: null, actorName: "system", ct: ct);
        }

        return summary;
    }

    public async Task<GenerationSummary> RegenerateAsync(Guid templateId, CancellationToken ct = default)
    {
        var config = await settings.GetAsync(ct);
        var firstDate = clock.LocalToday;
        var lastDate = firstDate.AddDays(config.GenerationWindowDays);
        var now = clock.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var template = await db.ScheduleTemplates
            .Include(t => t.Route)
            .Include(t => t.Bus)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return GenerationSummary.Empty;

        // Drop future, unbooked, still-generator-owned trips so the new pattern can take over.
        var removable = await db.Trips
            .Where(t => t.ScheduleTemplateId == templateId
                && t.DepartureTime > now
                && !t.IsManualOverride
                && t.Status == TripStatus.Scheduled
                && !t.Bookings.Any())
            .ToListAsync(ct);

        if (removable.Count > 0)
        {
            db.Trips.RemoveRange(removable);
            await db.SaveChangesAsync(ct);
        }

        var summary = await GenerateForTemplateAsync(db, template, firstDate, lastDate,
            GenerationSummary.Empty with { TemplatesProcessed = 1 }, ct);

        await audit.RecordAsync(AuditActions.TripGenerate, nameof(ScheduleTemplate), templateId.ToString(),
            $"Regenerated schedule “{template.Name}” — removed {removable.Count} unbooked trips, added {summary.TripsCreated}");
        return summary;
    }

    /// <summary>Move trips through Scheduled → Departed → Completed as their clock passes.</summary>
    public async Task AdvanceStatusesAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        await db.Trips
            .Where(t => t.Status == TripStatus.Scheduled && t.DepartureTime <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TripStatus.Departed)
                .SetProperty(t => t.UpdatedAt, now), ct);

        await db.Trips
            .Where(t => t.Status == TripStatus.Departed && t.ArrivalTime <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TripStatus.Completed)
                .SetProperty(t => t.UpdatedAt, now), ct);
    }

    private async Task<GenerationSummary> GenerateForTemplateAsync(
        AppDbContext db, ScheduleTemplate template,
        DateOnly firstDate, DateOnly lastDate,
        GenerationSummary summary, CancellationToken ct, bool isRetry = false)
    {
        var now = clock.UtcNow;
        var rangeStart = clock.ToInstant(firstDate, TimeOnly.MinValue);
        var rangeEnd = clock.ToInstant(lastDate.AddDays(1), TimeOnly.MinValue);

        // Any existing trip on this route in the window blocks that minute — covers
        // generated rows, manual cancellations and one-off manual trips alike.
        var taken = await db.Trips
            .Where(t => t.RouteId == template.RouteId
                && t.DepartureTime >= rangeStart && t.DepartureTime < rangeEnd)
            .Select(t => t.DepartureTime)
            .ToListAsync(ct);
        var takenTicks = taken.Select(d => d.UtcTicks).ToHashSet();

        var fresh = new List<Trip>();

        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            if (!template.OperatingDays.Includes(date.DayOfWeek))
                continue;

            foreach (var time in template.DepartureTimesOfDay())
            {
                var departure = clock.ToInstant(date, time);

                if (departure <= now || takenTicks.Contains(departure.UtcTicks))
                    continue;

                takenTicks.Add(departure.UtcTicks);
                fresh.Add(TripFactory.Create(
                    template.Route, template.Bus, departure, template.Fare,
                    template.Id, manual: false,
                    template.BoardingCounterId, template.DroppingCounterId, date));
            }
        }

        if (fresh.Count == 0)
            return summary;

        db.Trips.AddRange(fresh);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (!isRetry)
        {
            // A concurrent run raced us to the same (template, departure). Retry once,
            // filtering out whatever now exists.
            logger.LogWarning(ex, "Trip generation hit a unique-key clash for template {Template}; retrying.", template.Id);
            db.ChangeTracker.Clear();
            return await GenerateForTemplateAsync(db, template, firstDate, lastDate, summary, ct, isRetry: true);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Trip generation failed for template {Template} after a retry.", template.Id);
            db.ChangeTracker.Clear();
            return summary;
        }

        var seats = fresh.Sum(t => t.Seats.Count);
        db.ChangeTracker.Clear();
        return summary.Add(fresh.Count, seats);
    }
}
