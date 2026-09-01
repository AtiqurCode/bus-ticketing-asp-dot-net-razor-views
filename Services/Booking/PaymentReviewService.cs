using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Bookings;

public sealed record PaymentReviewRow(
    Guid BookingId,
    string Reference,
    string PassengerName,
    string PassengerPhone,
    string RouteName,
    DateTimeOffset DepartureTime,
    string SeatSummary,
    decimal Amount,
    PaymentMode Mode,
    MfsProvider? Provider,
    string? TransactionId,
    string? SenderMsisdn,
    PaymentStatus PaymentStatus,
    BookingStatus BookingStatus,
    DateTimeOffset? SubmittedAt,
    string? ReviewNote,
    string? ReviewedBy);

public sealed class PaymentReviewService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAppClock clock,
    AuditService audit)
{
    public async Task<List<PaymentReviewRow>> QueryAsync(
        PaymentStatus? status = PaymentStatus.Pending, string? search = null,
        Guid? counterLocationId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Bookings.AsNoTracking()
            .Include(b => b.Payment)
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .Include(b => b.Seats)
            .Where(b => b.Status != BookingStatus.Expired)
            .AsQueryable();

        if (status is not null)
            query = query.Where(b => b.PaymentStatus == status);

        if (counterLocationId is not null)
            query = query.Where(b => b.BoardingCounterId == counterLocationId || b.DroppingCounterId == counterLocationId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(b =>
                EF.Functions.ILike(b.Reference, term) ||
                EF.Functions.ILike(b.PassengerPhone, term) ||
                EF.Functions.ILike(b.PassengerName, term) ||
                (b.Payment != null && b.Payment.TransactionId != null && EF.Functions.ILike(b.Payment.TransactionId, term)));
        }

        var bookings = await query
            .OrderBy(b => b.PaymentStatus == PaymentStatus.Pending ? 0 : 1)
            .ThenByDescending(b => b.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var reviewerNames = await ReviewerNamesAsync(db, bookings, ct);

        return bookings.Select(b => new PaymentReviewRow(
            b.Id, b.Reference, b.PassengerName, b.PassengerPhone,
            b.Trip.Route.OriginLocation.Name + " → " + b.Trip.Route.DestinationLocation.Name,
            b.Trip.DepartureTime,
            string.Join(", ", b.Seats.Select(s => s.SeatNumber)),
            b.TotalAmount, b.PaymentMode, b.Payment?.Provider, b.Payment?.TransactionId,
            b.Payment?.SenderMsisdn, b.PaymentStatus, b.Status, b.Payment?.SubmittedAt,
            b.Payment?.ReviewNote,
            b.Payment?.ReviewedByStaffId is { } rid ? reviewerNames.GetValueOrDefault(rid) : null))
            .ToList();
    }

    public async Task<OperationResult> VerifyAsync(Guid bookingId, Guid staffId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.Payment).FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null)
            return OperationResult.Fail("Booking not found.");
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            return OperationResult.Fail("This booking is no longer active.");
        if (booking.PaymentStatus == PaymentStatus.Verified)
            return OperationResult.Fail("Already verified.");

        var now = clock.UtcNow;
        booking.PaymentStatus = PaymentStatus.Verified;
        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedAt = now;
        booking.HoldExpiresAt = null;

        booking.Payment ??= new Payment { BookingId = booking.Id, Amount = booking.TotalAmount, Mode = booking.PaymentMode };
        booking.Payment.Status = PaymentStatus.Verified;
        booking.Payment.ReviewedByStaffId = staffId;
        booking.Payment.ReviewedAt = now;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.PaymentVerify, nameof(Booking), booking.Id.ToString(),
            $"Verified payment for {booking.Reference} (৳{booking.TotalAmount:0})", staffId, "");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> RejectAsync(Guid bookingId, Guid staffId, string note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
            return OperationResult.Fail("Add a note so the passenger knows what to fix.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.Payment).FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null)
            return OperationResult.Fail("Booking not found.");
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            return OperationResult.Fail("This booking is no longer active.");

        var now = clock.UtcNow;
        booking.PaymentStatus = PaymentStatus.Rejected;
        // Booking stays Reserved so the passenger can resubmit before the hold lapses.
        booking.Payment ??= new Payment { BookingId = booking.Id, Amount = booking.TotalAmount, Mode = booking.PaymentMode };
        booking.Payment.Status = PaymentStatus.Rejected;
        booking.Payment.ReviewedByStaffId = staffId;
        booking.Payment.ReviewedAt = now;
        booking.Payment.ReviewNote = note.Trim();

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.PaymentReject, nameof(Booking), booking.Id.ToString(),
            $"Rejected payment for {booking.Reference}: {note.Trim()}", staffId, "");
        return OperationResult.Ok();
    }

    /// <summary>Counter staff take payment in person for a "pay at counter" reservation.</summary>
    public async Task<OperationResult> RecordCounterPaymentAsync(
        Guid bookingId, Guid staffId, MfsProvider provider, string? transactionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.Payment).FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null)
            return OperationResult.Fail("Booking not found.");
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            return OperationResult.Fail("This booking is no longer active.");
        if (booking.PaymentStatus == PaymentStatus.Verified)
            return OperationResult.Fail("Already paid.");
        if (provider != MfsProvider.Cash && string.IsNullOrWhiteSpace(transactionId))
            return OperationResult.Fail("Enter the mFS transaction ID.");

        var now = clock.UtcNow;
        booking.PaymentMode = PaymentMode.Counter;
        booking.PaymentStatus = PaymentStatus.Verified;
        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedAt = now;
        booking.HoldExpiresAt = null;

        booking.Payment ??= new Payment { BookingId = booking.Id, Amount = booking.TotalAmount };
        booking.Payment.Mode = PaymentMode.Counter;
        booking.Payment.Provider = provider;
        booking.Payment.TransactionId = provider == MfsProvider.Cash ? null : transactionId?.Trim();
        booking.Payment.Status = PaymentStatus.Verified;
        booking.Payment.SubmittedAt ??= now;
        booking.Payment.ReviewedByStaffId = staffId;
        booking.Payment.ReviewedAt = now;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.PaymentVerify, nameof(Booking), booking.Id.ToString(),
            $"Counter payment taken for {booking.Reference} ({provider}, ৳{booking.TotalAmount:0})", staffId, "");
        return OperationResult.Ok();
    }

    private static async Task<Dictionary<Guid, string>> ReviewerNamesAsync(
        AppDbContext db, List<Booking> bookings, CancellationToken ct)
    {
        var ids = bookings
            .Select(b => b.Payment?.ReviewedByStaffId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return [];

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }
}
