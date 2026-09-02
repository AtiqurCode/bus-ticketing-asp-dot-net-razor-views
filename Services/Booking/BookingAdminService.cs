using BusTicketing.Data;
using BusTicketing.Domain;
using BusTicketing.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Bookings;

public sealed record BookingRow(
    Guid Id, string Reference, string PassengerName, string PassengerPhone,
    string RouteName, DateTimeOffset DepartureTime, string SeatSummary,
    decimal TotalAmount, PaymentMode PaymentMode, PaymentStatus PaymentStatus,
    BookingStatus Status, bool IsCounterSale, DateTimeOffset CreatedAt);

public sealed record BookingPage(IReadOnlyList<BookingRow> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
}

public sealed class BookingAdminService(
    IDbContextFactory<AppDbContext> dbFactory,
    SeatMapBroadcaster broadcaster,
    IAppClock clock,
    AuditService audit,
    SmsService sms)
{
    public async Task<BookingPage> QueryAsync(
        string? search = null, BookingStatus? status = null, PaymentStatus? paymentStatus = null,
        Guid? routeId = null, DateOnly? serviceDate = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Bookings.AsNoTracking()
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .Include(b => b.Seats)
            .AsQueryable();

        if (status is not null) query = query.Where(b => b.Status == status);
        if (paymentStatus is not null) query = query.Where(b => b.PaymentStatus == paymentStatus);
        if (routeId is not null) query = query.Where(b => b.Trip.RouteId == routeId);
        if (serviceDate is not null) query = query.Where(b => b.Trip.ServiceDate == serviceDate);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(b =>
                EF.Functions.ILike(b.Reference, term) ||
                EF.Functions.ILike(b.PassengerPhone, term) ||
                EF.Functions.ILike(b.PassengerName, term));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new BookingRow(
                b.Id, b.Reference, b.PassengerName, b.PassengerPhone,
                b.Trip.Route.OriginLocation.Name + " → " + b.Trip.Route.DestinationLocation.Name,
                b.Trip.DepartureTime,
                string.Join(", ", b.Seats.Select(s => s.SeatNumber)),
                b.TotalAmount, b.PaymentMode, b.PaymentStatus, b.Status,
                b.BookedByStaffId != null, b.CreatedAt))
            .ToListAsync(ct);

        return new BookingPage(rows, total, page, pageSize);
    }

    public async Task<Booking?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Bookings.AsNoTracking()
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .Include(b => b.Trip).ThenInclude(t => t.Bus)
            .Include(b => b.BoardingCounter)
            .Include(b => b.DroppingCounter)
            .Include(b => b.Payment)
            .Include(b => b.Seats)
            .Include(b => b.BookedByStaff)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    /// <summary>Staff cancellation. Frees the seats; refund handling is layered on in the policy engine.</summary>
    public async Task<OperationResult> CancelAsync(Guid id, Guid staffId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("Give a reason for the cancellation.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.Trip).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null)
            return OperationResult.Fail("Booking not found.");
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            return OperationResult.Fail("This booking is already closed.");

        var now = clock.UtcNow;
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = now;
        booking.CancellationReason = reason.Trim();
        booking.CancelledByStaffId = staffId;

        await db.TripSeats.Where(s => s.BookingId == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SeatStatus.Available)
                .SetProperty(x => x.BookingId, (Guid?)null)
                .SetProperty(x => x.HoldToken, (Guid?)null)
                .SetProperty(x => x.HoldExpiresAt, (DateTimeOffset?)null), ct);

        await db.SaveChangesAsync(ct);
        broadcaster.Notify(booking.TripId);
        await audit.RecordAsync(AuditActions.BookingCancel, nameof(Booking), id.ToString(),
            $"Cancelled {booking.Reference}: {reason.Trim()}", staffId, "");

        await sms.SendAsync(booking.PassengerPhone, TicketMessages.BookingCancelled(booking, reason),
            SmsPurpose.BookingCancelled, booking.Id, ct);

        return OperationResult.Ok();
    }
}
