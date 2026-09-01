using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Admin;

public sealed class LocationService(IDbContextFactory<AppDbContext> dbFactory, AuditService audit)
{
    public async Task<List<Location>> ListAsync(string? search = null, LocationType? type = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Locations.Include(l => l.Parent).AsNoTracking();

        if (type is not null)
            query = query.Where(l => l.Type == type);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(l =>
                EF.Functions.ILike(l.Name, term) ||
                EF.Functions.ILike(l.District, term) ||
                (l.NameBn != null && EF.Functions.ILike(l.NameBn, term)));
        }

        return await query
            .OrderBy(l => l.Division).ThenBy(l => l.District).ThenBy(l => l.Type).ThenBy(l => l.Name)
            .ToListAsync(ct);
    }

    /// <summary>Cities available as a parent for a terminal/counter.</summary>
    public async Task<List<Location>> CitiesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Locations.AsNoTracking()
            .Where(l => l.Type == LocationType.City)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
    }

    /// <summary>Active cities and terminals — the endpoints a route can use.</summary>
    public Task<List<Location>> RoutePointsAsync(CancellationToken ct = default) =>
        ByTypesAsync([LocationType.City, LocationType.Terminal], ct);

    /// <summary>Active terminals and counters — boarding / dropping points on a trip.</summary>
    public Task<List<Location>> BoardingPointsAsync(CancellationToken ct = default) =>
        ByTypesAsync([LocationType.Terminal, LocationType.Counter], ct);

    private async Task<List<Location>> ByTypesAsync(LocationType[] types, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Locations.AsNoTracking()
            .Where(l => l.IsActive && types.Contains(l.Type))
            .OrderBy(l => l.Division).ThenBy(l => l.District).ThenBy(l => l.Name)
            .ToListAsync(ct);
    }

    public async Task<Location?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Locations.Include(l => l.Parent).FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task<OperationResult<Guid>> CreateAsync(Location input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (input.Type != LocationType.City)
        {
            var parent = input.ParentLocationId is { } pid
                ? await db.Locations.FindAsync([pid], ct)
                : null;
            if (parent is null)
                return OperationResult<Guid>.Fail("A terminal or counter needs a parent city.");
            input.Division = parent.Division;
            input.District = parent.District;
        }

        var location = new Location
        {
            Division = input.Division.Trim(),
            District = input.District.Trim(),
            Name = input.Name.Trim(),
            NameBn = Trimmed(input.NameBn),
            Type = input.Type,
            ParentLocationId = input.Type == LocationType.City ? null : input.ParentLocationId,
            IsActive = input.IsActive
        };

        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityCreate, nameof(Location), location.Id.ToString(),
            $"Added {location.Type.ToString().ToLowerInvariant()} “{location.Name}”");
        return OperationResult<Guid>.Ok(location.Id);
    }

    public async Task<OperationResult> UpdateAsync(Location input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == input.Id, ct);
        if (location is null)
            return OperationResult.Fail("Location not found.");

        location.Name = input.Name.Trim();
        location.NameBn = Trimmed(input.NameBn);
        location.IsActive = input.IsActive;

        if (location.Type != LocationType.City)
        {
            var parent = input.ParentLocationId is { } pid
                ? await db.Locations.FindAsync([pid], ct)
                : null;
            if (parent is null)
                return OperationResult.Fail("A terminal or counter needs a parent city.");
            location.ParentLocationId = parent.Id;
            location.Division = parent.Division;
            location.District = parent.District;
        }
        else
        {
            location.Division = input.Division.Trim();
            location.District = input.District.Trim();
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityUpdate, nameof(Location), location.Id.ToString(),
            $"Updated location “{location.Name}”");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (location is null)
            return OperationResult.Fail("Location not found.");

        var referenced =
            await db.Routes.AnyAsync(r => r.OriginLocationId == id || r.DestinationLocationId == id, ct)
            || await db.Locations.AnyAsync(l => l.ParentLocationId == id, ct)
            || await db.ScheduleTemplates.AnyAsync(t => t.BoardingCounterId == id || t.DroppingCounterId == id, ct);

        if (referenced)
            return OperationResult.Fail("This location is used by a route, schedule or child stop. Deactivate it instead.");

        db.Locations.Remove(location);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityDelete, nameof(Location), id.ToString(),
            $"Deleted location “{location.Name}”");
        return OperationResult.Ok();
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var affected = await db.Locations.Where(l => l.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsActive, active).SetProperty(l => l.UpdatedAt, DateTimeOffset.UtcNow), ct);
        if (affected > 0)
            await audit.RecordAsync(AuditActions.EntityUpdate, nameof(Location), id.ToString(),
                active ? "Reactivated location" : "Deactivated location");
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
