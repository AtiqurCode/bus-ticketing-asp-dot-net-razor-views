using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Admin;

public sealed class BusService(IDbContextFactory<AppDbContext> dbFactory, AuditService audit)
{
    public async Task<List<Bus>> ListAsync(string? search = null, bool includeInactive = true, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Buses.AsNoTracking();

        if (!includeInactive)
            query = query.Where(b => b.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(b => EF.Functions.ILike(b.Name, term) || EF.Functions.ILike(b.Operator, term));
        }

        return await query.OrderBy(b => b.Operator).ThenBy(b => b.Name).ToListAsync(ct);
    }

    public async Task<Bus?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Buses.FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<List<Bus>> ActiveForSchedulingAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Buses.AsNoTracking()
            .Where(b => b.IsActive && b.TotalSeats > 0)
            .OrderBy(b => b.Operator).ThenBy(b => b.Name)
            .ToListAsync(ct);
    }

    public async Task<OperationResult<Guid>> CreateAsync(Bus input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
            return OperationResult<Guid>.Fail(validation);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bus = new Bus
        {
            Name = input.Name.Trim(),
            Operator = input.Operator.Trim(),
            RegistrationNumber = Trimmed(input.RegistrationNumber),
            Class = input.Class,
            Amenities = Trimmed(input.Amenities),
            IsActive = input.IsActive,
            SeatMap = input.SeatMap,
            TotalSeats = input.SeatMap.SeatCount
        };

        db.Buses.Add(bus);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityCreate, nameof(Bus), bus.Id.ToString(),
            $"Added bus “{bus.Name}” ({bus.Operator}, {bus.TotalSeats} seats)");
        return OperationResult<Guid>.Ok(bus.Id);
    }

    public async Task<OperationResult> UpdateAsync(Bus input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
            return OperationResult.Fail(validation);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bus = await db.Buses.FirstOrDefaultAsync(b => b.Id == input.Id, ct);
        if (bus is null)
            return OperationResult.Fail("Bus not found.");

        var hadTrips = await db.Trips.AnyAsync(t => t.BusId == bus.Id
            && t.DepartureTime > DateTimeOffset.UtcNow
            && t.Status != TripStatus.Cancelled, ct);
        var seatsChanged = !SeatNumbersEqual(bus.SeatMap, input.SeatMap);
        if (hadTrips && seatsChanged)
            return OperationResult.Fail(
                "This bus has upcoming trips — changing the seat layout would break their seat maps. Retire it and add a new bus instead.");

        bus.Name = input.Name.Trim();
        bus.Operator = input.Operator.Trim();
        bus.RegistrationNumber = Trimmed(input.RegistrationNumber);
        bus.Class = input.Class;
        bus.Amenities = Trimmed(input.Amenities);
        bus.IsActive = input.IsActive;
        bus.SeatMap = input.SeatMap;
        bus.TotalSeats = input.SeatMap.SeatCount;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityUpdate, nameof(Bus), bus.Id.ToString(),
            $"Updated bus “{bus.Name}”");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bus = await db.Buses.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bus is null)
            return OperationResult.Fail("Bus not found.");

        if (await db.Trips.AnyAsync(t => t.BusId == id, ct) ||
            await db.ScheduleTemplates.AnyAsync(t => t.BusId == id, ct))
            return OperationResult.Fail("This bus is assigned to trips or a schedule. Deactivate it instead.");

        db.Buses.Remove(bus);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.EntityDelete, nameof(Bus), id.ToString(),
            $"Deleted bus “{bus.Name}”");
        return OperationResult.Ok();
    }

    private static string? Validate(Bus bus)
    {
        if (string.IsNullOrWhiteSpace(bus.Name)) return "Give the bus a name.";
        if (string.IsNullOrWhiteSpace(bus.Operator)) return "Set the operating company.";
        if (bus.SeatMap.SeatCount == 0) return "Lay out at least one seat.";

        var duplicates = bus.SeatMap.Seats
            .GroupBy(s => s.Number, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            return $"Seat numbers must be unique — repeated: {string.Join(", ", duplicates)}.";

        if (bus.SeatMap.Seats.Any(s => string.IsNullOrWhiteSpace(s.Number)))
            return "Every seat needs a label.";

        return null;
    }

    private static bool SeatNumbersEqual(SeatMap a, SeatMap b) =>
        a.SeatNumbers.OrderBy(x => x).SequenceEqual(b.SeatNumbers.OrderBy(x => x));

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
