using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Scheduling;

public sealed record TripRow(
    Guid Id, DateTimeOffset DepartureTime, string RouteName, string BusName, string Operator,
    decimal Fare, int TotalSeats, int SeatsBooked, int SeatsHeld,
    TripStatus Status, bool IsManualOverride);

public sealed record TripPage(IReadOnlyList<TripRow> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
}

public sealed record ManualTripInput
{
    public Guid? Id { get; init; }
    public Guid RouteId { get; set; }
    public Guid BusId { get; set; }
    public DateTimeOffset DepartureTime { get; set; }
    public decimal Fare { get; set; }
    public Guid? BoardingCounterId { get; set; }
    public Guid? DroppingCounterId { get; set; }
}

public sealed class TripService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAppClock clock,
    AuditService audit)
{
    public async Task<TripPage> QueryAsync(
        Guid? routeId = null, DateOnly? serviceDate = null, TripStatus? status = null,
        bool upcomingOnly = false, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Trips
            .Include(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .Include(t => t.Bus)
            .AsNoTracking()
            .AsQueryable();

        if (routeId is not null) query = query.Where(t => t.RouteId == routeId);
        if (serviceDate is not null) query = query.Where(t => t.ServiceDate == serviceDate);
        if (status is not null) query = query.Where(t => t.Status == status);
        if (upcomingOnly) query = query.Where(t => t.DepartureTime >= clock.UtcNow);

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(t => t.DepartureTime)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TripRow(
                t.Id,
                t.DepartureTime,
                t.Route.OriginLocation.Name + " → " + t.Route.DestinationLocation.Name,
                t.Bus.Name,
                t.Bus.Operator,
                t.Fare,
                t.Seats.Count,
                t.Seats.Count(s => s.Status == SeatStatus.Booked),
                t.Seats.Count(s => s.Status == SeatStatus.Held),
                t.Status,
                t.IsManualOverride))
            .ToListAsync(ct);

        return new TripPage(rows, total, page, pageSize);
    }

    public async Task<Trip?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Trips
            .Include(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .Include(t => t.Bus)
            .Include(t => t.Seats)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<OperationResult<Guid>> CreateManualAsync(ManualTripInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var route = await db.Routes
            .Include(r => r.OriginLocation).Include(r => r.DestinationLocation)
            .FirstOrDefaultAsync(r => r.Id == input.RouteId, ct);
        var bus = await db.Buses.FirstOrDefaultAsync(b => b.Id == input.BusId, ct);
        var validation = Validate(route, bus, input);
        if (validation is not null)
            return OperationResult<Guid>.Fail(validation);

        if (await db.Trips.AnyAsync(t => t.RouteId == input.RouteId && t.DepartureTime == input.DepartureTime, ct))
            return OperationResult<Guid>.Fail("A trip on that route already departs at that time.");

        var serviceDate = DateOnly.FromDateTime(clock.ToLocal(input.DepartureTime).DateTime);
        var trip = TripFactory.Create(route!, bus!, input.DepartureTime, input.Fare,
            scheduleTemplateId: null, manual: true,
            input.BoardingCounterId, input.DroppingCounterId, serviceDate);

        db.Trips.Add(trip);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.TripOverride, nameof(Trip), trip.Id.ToString(),
            $"Added one-off trip {trip.DepartureTime:g} on {route!.OriginLocation.Name} → {route.DestinationLocation.Name}");
        return OperationResult<Guid>.Ok(trip.Id);
    }

    public async Task<OperationResult> UpdateAsync(ManualTripInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var trip = await db.Trips.Include(t => t.Seats).FirstOrDefaultAsync(t => t.Id == input.Id, ct);
        if (trip is null)
            return OperationResult.Fail("Trip not found.");
        if (trip.Status is TripStatus.Departed or TripStatus.Completed)
            return OperationResult.Fail("This trip has already run.");

        var route = await db.Routes.FirstOrDefaultAsync(r => r.Id == input.RouteId, ct);
        var bus = await db.Buses.FirstOrDefaultAsync(b => b.Id == input.BusId, ct);
        var validation = Validate(route, bus, input);
        if (validation is not null)
            return OperationResult.Fail(validation);

        var hasBookings = await db.Bookings.AnyAsync(b => b.TripId == trip.Id
            && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired, ct);
        var busChanged = trip.BusId != input.BusId;
        if (hasBookings && busChanged)
            return OperationResult.Fail("There are live bookings on this trip — the bus can't be swapped.");

        trip.RouteId = input.RouteId;
        trip.Fare = input.Fare;
        trip.DepartureTime = input.DepartureTime;
        trip.ArrivalTime = input.DepartureTime.AddMinutes(route!.ApproxDurationMinutes);
        trip.ServiceDate = DateOnly.FromDateTime(clock.ToLocal(input.DepartureTime).DateTime);
        trip.BoardingCounterId = input.BoardingCounterId;
        trip.DroppingCounterId = input.DroppingCounterId;
        trip.IsManualOverride = true; // hands-off for the generator from now on

        if (busChanged)
        {
            trip.BusId = input.BusId;

            // trip is already tracked, so a plain trip.Seats.Add(new …) would have EF's
            // fixup mistake these client-keyed rows for existing ones and try to UPDATE
            // them (0 rows affected). AddRange on the DbSet forces them Added.
            db.TripSeats.RemoveRange(trip.Seats);
            trip.Seats.Clear();
            db.TripSeats.AddRange(bus!.SeatMap.Seats.Select(seat => new TripSeat
            {
                TripId = trip.Id,
                SeatNumber = seat.Number,
                SeatType = seat.Type,
                Status = SeatStatus.Available
            }));
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.TripOverride, nameof(Trip), trip.Id.ToString(),
            $"Edited trip {trip.DepartureTime:g} (now a manual override)");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> CancelAsync(Guid id, string reason, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trip is null)
            return OperationResult.Fail("Trip not found.");
        if (trip.Status == TripStatus.Cancelled)
            return OperationResult.Fail("This trip is already cancelled.");
        if (trip.Status is TripStatus.Departed or TripStatus.Completed)
            return OperationResult.Fail("This trip has already run.");

        trip.Status = TripStatus.Cancelled;
        trip.IsManualOverride = true;

        // Free the seats; the booking side is settled in the cancellation flow.
        await db.TripSeats.Where(s => s.TripId == id && s.Status != SeatStatus.Booked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SeatStatus.Blocked)
                .SetProperty(x => x.HoldToken, (Guid?)null)
                .SetProperty(x => x.HoldExpiresAt, (DateTimeOffset?)null), ct);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.TripCancel, nameof(Trip), id.ToString(),
            $"Cancelled trip {trip.DepartureTime:g}: {reason}");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetStatusAsync(Guid id, TripStatus status, CancellationToken ct = default)
    {
        if (status == TripStatus.Cancelled)
            return OperationResult.Fail("Use the cancel action to cancel a trip.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var affected = await db.Trips.Where(t => t.Id == id && t.Status != TripStatus.Cancelled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, status)
                .SetProperty(t => t.IsManualOverride, true)
                .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow), ct);

        if (affected == 0)
            return OperationResult.Fail("Trip not found or already cancelled.");

        await audit.RecordAsync(AuditActions.TripOverride, nameof(Trip), id.ToString(),
            $"Set trip status to {status}");
        return OperationResult.Ok();
    }

    private string? Validate(BusRoute? route, Bus? bus, ManualTripInput input)
    {
        if (route is null) return "Pick a route.";
        if (bus is null) return "Pick a bus.";
        if (bus.SeatMap.SeatCount == 0) return "That bus has no seat layout.";
        if (input.Fare is <= 0 or > 100000) return "Enter a sensible fare.";
        if (input.DepartureTime <= clock.UtcNow) return "Departure has to be in the future.";
        return null;
    }
}
