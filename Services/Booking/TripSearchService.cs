using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Bookings;

public sealed record TripSearchResult(
    Guid TripId,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    string OriginName,
    string DestinationName,
    string BusName,
    string Operator,
    BusClass Class,
    string ClassLabel,
    decimal Fare,
    int SeatsAvailable,
    int TotalSeats,
    string? BoardingPoint,
    string? DroppingPoint)
{
    public TimeSpan Duration => ArrivalTime - DepartureTime;
    public bool SoldOut => SeatsAvailable == 0;
}

public sealed record LocationOption(Guid Id, string Name, string District, LocationType Type);

public sealed class TripSearchService(IDbContextFactory<AppDbContext> dbFactory, IAppClock clock)
{
    /// <summary>Locations that match a free-text place name, for the search box.</summary>
    public async Task<List<LocationOption>> ResolvePlacesAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var term = $"%{text.Trim()}%";
        return await db.Locations.AsNoTracking()
            .Where(l => l.IsActive && (
                EF.Functions.ILike(l.Name, term) ||
                EF.Functions.ILike(l.District, term) ||
                (l.NameBn != null && EF.Functions.ILike(l.NameBn, term))))
            .OrderBy(l => l.Type).ThenBy(l => l.Name)
            .Take(12)
            .Select(l => new LocationOption(l.Id, l.Name, l.District, l.Type))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TripSearchResult>> SearchAsync(
        string from, string to, DateOnly date, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var originIds = await MatchLocationIdsAsync(db, from, ct);
        var destIds = await MatchLocationIdsAsync(db, to, ct);
        if (originIds.Count == 0 || destIds.Count == 0)
            return [];

        var now = clock.UtcNow;
        var dayStart = clock.ToInstant(date, TimeOnly.MinValue);
        var dayEnd = clock.ToInstant(date.AddDays(1), TimeOnly.MinValue);

        var results = await db.Trips.AsNoTracking()
            .Where(t => t.Status == TripStatus.Scheduled
                && t.DepartureTime >= dayStart && t.DepartureTime < dayEnd
                && t.DepartureTime > now
                && originIds.Contains(t.Route.OriginLocationId)
                && destIds.Contains(t.Route.DestinationLocationId))
            .OrderBy(t => t.DepartureTime)
            .Select(t => new TripSearchResult(
                t.Id,
                t.DepartureTime,
                t.ArrivalTime,
                t.Route.OriginLocation.Name,
                t.Route.DestinationLocation.Name,
                t.Bus.Name,
                t.Bus.Operator,
                t.Bus.Class,
                t.Bus.ClassLabel,
                t.Fare,
                t.Seats.Count(s => s.Status == SeatStatus.Available),
                t.Seats.Count,
                t.BoardingCounter != null ? t.BoardingCounter.Name : null,
                t.DroppingCounter != null ? t.DroppingCounter.Name : null))
            .ToListAsync(ct);

        return results;
    }

    /// <summary>The route endpoints that could be meant by a place name — the
    /// matched location itself, and (for a city) its terminals/counters.</summary>
    private static async Task<HashSet<Guid>> MatchLocationIdsAsync(AppDbContext db, string text, CancellationToken ct)
    {
        var term = $"%{text.Trim()}%";
        var matched = await db.Locations.AsNoTracking()
            .Where(l => l.IsActive && (
                EF.Functions.ILike(l.Name, term) ||
                (l.NameBn != null && EF.Functions.ILike(l.NameBn, term))))
            .Select(l => new { l.Id, l.Type })
            .ToListAsync(ct);

        var ids = matched.Select(m => m.Id).ToHashSet();

        var cityIds = matched.Where(m => m.Type == LocationType.City).Select(m => m.Id).ToList();
        if (cityIds.Count > 0)
        {
            var children = await db.Locations.AsNoTracking()
                .Where(l => l.IsActive && l.ParentLocationId != null && cityIds.Contains(l.ParentLocationId!.Value))
                .Select(l => l.Id)
                .ToListAsync(ct);
            ids.UnionWith(children);
        }

        return ids;
    }
}
