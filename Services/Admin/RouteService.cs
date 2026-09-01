using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Admin;

public sealed class RouteService(IDbContextFactory<AppDbContext> dbFactory, AuditService audit)
{
    public async Task<List<BusRoute>> ListAsync(string? search = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Routes
            .Include(r => r.OriginLocation)
            .Include(r => r.DestinationLocation)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.OriginLocation.Name, term) ||
                EF.Functions.ILike(r.DestinationLocation.Name, term));
        }

        return await query
            .OrderBy(r => r.OriginLocation.Name).ThenBy(r => r.DestinationLocation.Name)
            .ToListAsync(ct);
    }

    public async Task<BusRoute?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Routes
            .Include(r => r.OriginLocation)
            .Include(r => r.DestinationLocation)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<List<BusRoute>> ActiveAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Routes
            .Include(r => r.OriginLocation)
            .Include(r => r.DestinationLocation)
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.OriginLocation.Name).ThenBy(r => r.DestinationLocation.Name)
            .ToListAsync(ct);
    }

    public async Task<OperationResult<Guid>> CreateAsync(BusRoute input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
            return OperationResult<Guid>.Fail(validation);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (await db.Routes.AnyAsync(r =>
                r.OriginLocationId == input.OriginLocationId &&
                r.DestinationLocationId == input.DestinationLocationId, ct))
            return OperationResult<Guid>.Fail("That origin–destination route already exists.");

        var route = new BusRoute
        {
            OriginLocationId = input.OriginLocationId,
            DestinationLocationId = input.DestinationLocationId,
            DistanceKm = input.DistanceKm,
            ApproxDurationMinutes = input.ApproxDurationMinutes,
            IsActive = input.IsActive
        };
        db.Routes.Add(route);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityCreate, nameof(BusRoute), route.Id.ToString(), "Added a route");
        return OperationResult<Guid>.Ok(route.Id);
    }

    public async Task<OperationResult> UpdateAsync(BusRoute input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
            return OperationResult.Fail(validation);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var route = await db.Routes.FirstOrDefaultAsync(r => r.Id == input.Id, ct);
        if (route is null)
            return OperationResult.Fail("Route not found.");

        if (await db.Routes.AnyAsync(r => r.Id != route.Id &&
                r.OriginLocationId == input.OriginLocationId &&
                r.DestinationLocationId == input.DestinationLocationId, ct))
            return OperationResult.Fail("Another route already covers that origin and destination.");

        route.OriginLocationId = input.OriginLocationId;
        route.DestinationLocationId = input.DestinationLocationId;
        route.DistanceKm = input.DistanceKm;
        route.ApproxDurationMinutes = input.ApproxDurationMinutes;
        route.IsActive = input.IsActive;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityUpdate, nameof(BusRoute), route.Id.ToString(), "Updated a route");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var route = await db.Routes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (route is null)
            return OperationResult.Fail("Route not found.");

        if (await db.ScheduleTemplates.AnyAsync(t => t.RouteId == id, ct) ||
            await db.Trips.AnyAsync(t => t.RouteId == id, ct))
            return OperationResult.Fail("This route has schedules or trips. Deactivate it instead.");

        db.Routes.Remove(route);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityDelete, nameof(BusRoute), id.ToString(), "Deleted a route");
        return OperationResult.Ok();
    }

    private static string? Validate(BusRoute route)
    {
        if (route.OriginLocationId == Guid.Empty || route.DestinationLocationId == Guid.Empty)
            return "Pick both an origin and a destination.";
        if (route.OriginLocationId == route.DestinationLocationId)
            return "Origin and destination must be different.";
        if (route.DistanceKm is < 0 or > 2000)
            return "Distance looks wrong — enter kilometres between 0 and 2000.";
        if (route.ApproxDurationMinutes is < 0 or > 3000)
            return "Duration looks wrong — enter minutes between 0 and 3000.";
        return null;
    }
}
