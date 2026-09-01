using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Scheduling;

public sealed class ScheduleTemplateService(IDbContextFactory<AppDbContext> dbFactory, AuditService audit)
{
    public async Task<List<ScheduleTemplate>> ListAsync(bool includeInactive = true, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.ScheduleTemplates
            .Include(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .Include(t => t.Bus)
            .AsNoTracking();

        if (!includeInactive)
            query = query.Where(t => t.IsActive);

        return await query
            .OrderBy(t => t.Route.OriginLocation.Name).ThenBy(t => t.StartTime)
            .ToListAsync(ct);
    }

    public async Task<ScheduleTemplate?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ScheduleTemplates
            .Include(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .Include(t => t.Bus)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<OperationResult<Guid>> CreateAsync(ScheduleTemplate input, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(input, ct);
        if (validation is not null)
            return OperationResult<Guid>.Fail(validation);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var template = new ScheduleTemplate
        {
            Name = input.Name.Trim(),
            RouteId = input.RouteId,
            BusId = input.BusId,
            StartTime = input.StartTime,
            EndTime = input.EndTime,
            IntervalMinutes = input.IntervalMinutes,
            Fare = input.Fare,
            OperatingDays = input.OperatingDays,
            BoardingCounterId = input.BoardingCounterId,
            DroppingCounterId = input.DroppingCounterId,
            IsActive = input.IsActive
        };
        db.ScheduleTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityCreate, nameof(ScheduleTemplate), template.Id.ToString(),
            $"Added schedule “{template.Name}”");
        return OperationResult<Guid>.Ok(template.Id);
    }

    public async Task<OperationResult> UpdateAsync(ScheduleTemplate input, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(input, ct);
        if (validation is not null)
            return OperationResult.Fail(validation);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var template = await db.ScheduleTemplates.FirstOrDefaultAsync(t => t.Id == input.Id, ct);
        if (template is null)
            return OperationResult.Fail("Schedule not found.");

        template.Name = input.Name.Trim();
        template.RouteId = input.RouteId;
        template.BusId = input.BusId;
        template.StartTime = input.StartTime;
        template.EndTime = input.EndTime;
        template.IntervalMinutes = input.IntervalMinutes;
        template.Fare = input.Fare;
        template.OperatingDays = input.OperatingDays;
        template.BoardingCounterId = input.BoardingCounterId;
        template.DroppingCounterId = input.DroppingCounterId;
        template.IsActive = input.IsActive;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityUpdate, nameof(ScheduleTemplate), template.Id.ToString(),
            $"Updated schedule “{template.Name}” — affects trips generated from now on");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var template = await db.ScheduleTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            return OperationResult.Fail("Schedule not found.");

        var hasTrips = await db.Trips.AnyAsync(t => t.ScheduleTemplateId == id, ct);
        if (hasTrips)
            return OperationResult.Fail(
                "Trips have already been generated from this schedule. Deactivate it — the generated trips stay bookable, and no new ones are added.");

        db.ScheduleTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityDelete, nameof(ScheduleTemplate), id.ToString(),
            $"Deleted schedule “{template.Name}”");
        return OperationResult.Ok();
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var affected = await db.ScheduleTemplates.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsActive, active)
                .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow), ct);
        if (affected > 0)
            await audit.RecordAsync(AuditActions.EntityUpdate, nameof(ScheduleTemplate), id.ToString(),
                active ? "Resumed schedule" : "Paused schedule");
    }

    private async Task<string?> ValidateAsync(ScheduleTemplate t, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) return "Name the schedule.";
        if (t.RouteId == Guid.Empty) return "Pick a route.";
        if (t.BusId == Guid.Empty) return "Assign a bus.";
        if (t.EndTime < t.StartTime) return "The last departure can't be before the first.";
        if (t.IntervalMinutes is < 15 or > 24 * 60) return "Interval should be between 15 minutes and 24 hours.";
        if (t.Fare is <= 0 or > 100000) return "Enter a sensible fare.";
        if (t.OperatingDays == WeekDays.None) return "Pick at least one operating day.";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Routes.AnyAsync(r => r.Id == t.RouteId && r.IsActive, ct))
            return "That route is inactive or missing.";
        if (!await db.Buses.AnyAsync(b => b.Id == t.BusId && b.IsActive && b.TotalSeats > 0, ct))
            return "That bus is inactive or has no seats.";
        return null;
    }
}
