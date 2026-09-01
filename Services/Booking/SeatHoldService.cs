using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Bookings;

public sealed class SeatHoldService(
    IDbContextFactory<AppDbContext> dbFactory,
    SettingsService settings,
    IAppClock clock,
    SeatMapBroadcaster broadcaster)
{
    private const int MaxSeatsPerBooking = 6;

    public async Task<TripBookingContext?> GetContextAsync(Guid tripId, Guid holdToken, CancellationToken ct = default)
    {
        await SweepExpiredAsync(tripId, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var trip = await db.Trips.AsNoTracking()
            .Include(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .Include(t => t.Bus)
            .Include(t => t.BoardingCounter)
            .Include(t => t.DroppingCounter)
            .Include(t => t.Seats)
            .FirstOrDefaultAsync(t => t.Id == tripId, ct);

        if (trip is null)
            return null;

        var now = clock.UtcNow;
        var byNumber = trip.Bus.SeatMap.Seats.ToDictionary(s => s.Number);

        var myHoldExpiry = trip.Seats
            .Where(s => s.Status == SeatStatus.Held && s.HoldToken == holdToken && s.HoldExpiresAt > now)
            .Select(s => s.HoldExpiresAt)
            .DefaultIfEmpty(null)
            .Min();

        var views = trip.Seats
            .Select(s =>
            {
                byNumber.TryGetValue(s.SeatNumber, out var cell);
                return new SeatView(
                    s.SeatNumber,
                    cell?.Row ?? 0,
                    cell?.Column ?? 0,
                    cell?.Deck ?? 1,
                    s.SeatType,
                    Classify(s, holdToken, now));
            })
            .OrderBy(v => v.Deck).ThenBy(v => v.Row).ThenBy(v => v.Column)
            .ToList();

        return new TripBookingContext(
            trip.Id,
            trip.Route.OriginLocation.Name,
            trip.Route.DestinationLocation.Name,
            trip.DepartureTime,
            trip.ArrivalTime,
            trip.Bus.Name,
            trip.Bus.Operator,
            trip.Bus.ClassLabel,
            trip.Fare,
            trip.Bus.SeatMap.Rows,
            trip.Bus.SeatMap.Columns,
            trip.Bus.SeatMap.Decks,
            trip.BoardingCounter?.Name,
            trip.DroppingCounter?.Name,
            trip.Status)
        {
            Seats = views,
            MyHoldExpiresAt = myHoldExpiry
        };
    }

    public async Task<HoldResult> ToggleAsync(Guid tripId, string seatNumber, Guid holdToken, CancellationToken ct = default)
    {
        await SweepExpiredAsync(tripId, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId, ct);
        if (trip is null || trip.Status != TripStatus.Scheduled)
            return HoldResult.Fail("This trip is no longer open for booking.", await MySeatsAsync(db, tripId, holdToken, ct));
        if (trip.DepartureTime <= clock.UtcNow)
            return HoldResult.Fail("This departure has already left.", []);

        var seat = await db.TripSeats.FirstOrDefaultAsync(s => s.TripId == tripId && s.SeatNumber == seatNumber, ct);
        if (seat is null)
            return HoldResult.Fail("That seat doesn't exist on this bus.", await MySeatsAsync(db, tripId, holdToken, ct));

        var now = clock.UtcNow;
        var mine = seat.Status == SeatStatus.Held && seat.HoldToken == holdToken;

        if (mine)
        {
            seat.Status = SeatStatus.Available;
            seat.HoldToken = null;
            seat.HoldExpiresAt = null;
        }
        else if (seat.Status == SeatStatus.Available || (seat.Status == SeatStatus.Held && seat.HoldExpiresAt <= now))
        {
            var held = await db.TripSeats.CountAsync(s => s.TripId == tripId
                && s.Status == SeatStatus.Held && s.HoldToken == holdToken && s.HoldExpiresAt > now, ct);
            if (held >= MaxSeatsPerBooking)
                return HoldResult.Fail($"You can hold at most {MaxSeatsPerBooking} seats in one booking.",
                    await MySeatsAsync(db, tripId, holdToken, ct));

            seat.Status = SeatStatus.Held;
            seat.HoldToken = holdToken;
            seat.HoldExpiresAt = now.AddMinutes(settings.Current.SeatHoldMinutes);
        }
        else
        {
            return HoldResult.Fail("Someone just took that seat — pick another.",
                await MySeatsAsync(db, tripId, holdToken, ct));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HoldResult.Fail("That seat changed hands a moment ago — try again.",
                await MySeatsAsync(db, tripId, holdToken, ct));
        }

        broadcaster.Notify(tripId);
        return HoldResult.Ok(await MySeatsAsync(db, tripId, holdToken, ct));
    }

    /// <summary>Push all of this session's live holds out by the full hold window.</summary>
    public async Task ExtendAsync(Guid tripId, Guid holdToken, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var expiry = clock.UtcNow.AddMinutes(settings.Current.SeatHoldMinutes);
        await db.TripSeats
            .Where(s => s.TripId == tripId && s.Status == SeatStatus.Held && s.HoldToken == holdToken)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.HoldExpiresAt, expiry), ct);
    }

    public async Task ReleaseAllAsync(Guid tripId, Guid holdToken, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var freed = await db.TripSeats
            .Where(s => s.TripId == tripId && s.Status == SeatStatus.Held && s.HoldToken == holdToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SeatStatus.Available)
                .SetProperty(x => x.HoldToken, (Guid?)null)
                .SetProperty(x => x.HoldExpiresAt, (DateTimeOffset?)null), ct);

        if (freed > 0)
            broadcaster.Notify(tripId);
    }

    public async Task<int> SweepExpiredAsync(Guid? tripId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        var query = db.TripSeats.Where(s => s.Status == SeatStatus.Held && s.HoldExpiresAt < now);
        if (tripId is not null)
            query = query.Where(s => s.TripId == tripId);

        var freed = await query.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, SeatStatus.Available)
            .SetProperty(x => x.HoldToken, (Guid?)null)
            .SetProperty(x => x.HoldExpiresAt, (DateTimeOffset?)null), ct);

        if (freed > 0 && tripId is not null)
            broadcaster.Notify(tripId.Value);

        return freed;
    }

    private async Task<IReadOnlyList<string>> MySeatsAsync(AppDbContext db, Guid tripId, Guid holdToken, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return await db.TripSeats.AsNoTracking()
            .Where(s => s.TripId == tripId && s.Status == SeatStatus.Held
                && s.HoldToken == holdToken && s.HoldExpiresAt > now)
            .Select(s => s.SeatNumber)
            .ToListAsync(ct);
    }

    private static SeatViewStatus Classify(TripSeat seat, Guid holdToken, DateTimeOffset now) => seat.Status switch
    {
        SeatStatus.Booked => SeatViewStatus.Booked,
        SeatStatus.Blocked => SeatViewStatus.Blocked,
        SeatStatus.Held when seat.HoldToken == holdToken && seat.HoldExpiresAt > now => SeatViewStatus.Mine,
        SeatStatus.Held when seat.HoldExpiresAt > now => SeatViewStatus.Taken,
        _ => SeatViewStatus.Available
    };
}
