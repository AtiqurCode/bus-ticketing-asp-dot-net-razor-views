using BusTicketing.Data;
using BusTicketing.Domain;
using BusTicketing.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Bookings;

public sealed record RefundPreview(bool CanCancel, string? BlockReason, double HoursBeforeDeparture, int RefundPercent, decimal RefundAmount);

public sealed class BookingService(
    IDbContextFactory<AppDbContext> dbFactory,
    SettingsService settings,
    IAppClock clock,
    SeatMapBroadcaster broadcaster,
    AuditService audit,
    SmsService sms,
    CancellationPolicyService cancellationPolicies,
    ILogger<BookingService> logger)
{
    public async Task<OperationResult<string>> CreateAsync(BookingRequest request, CancellationToken ct = default)
    {
        var phone = PhoneNumber.Normalize(request.PassengerPhone);
        var validation = Validate(request, phone);
        if (validation is not null)
            return OperationResult<string>.Fail(validation);

        var config = settings.Current;
        var now = clock.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var trip = await db.Trips
            .Include(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .FirstOrDefaultAsync(t => t.Id == request.TripId, ct);
        if (trip is null || trip.Status != TripStatus.Scheduled)
            return OperationResult<string>.Fail("This trip is no longer open for booking.");
        if (trip.DepartureTime <= now)
            return OperationResult<string>.Fail("This departure has already left.");

        var wanted = request.SeatNumbers.Distinct().ToList();
        var seats = await db.TripSeats
            .Where(s => s.TripId == request.TripId && wanted.Contains(s.SeatNumber))
            .ToListAsync(ct);

        if (seats.Count != wanted.Count)
            return OperationResult<string>.Fail("Some of those seats aren't on this bus anymore.");

        var lost = seats
            .Where(s => !(s.Status == SeatStatus.Held && s.HoldToken == request.HoldToken && s.HoldExpiresAt > now))
            .Select(s => s.SeatNumber)
            .ToList();
        if (lost.Count > 0)
            return OperationResult<string>.Fail(
                $"Your hold on seat {string.Join(", ", lost)} expired. Please pick your seats again.");

        var paidNow = request is { BookedByStaffId: not null, MarkPaidNow: true };
        var total = trip.Fare * seats.Count;

        var booking = new Booking
        {
            Reference = await UniqueReferenceAsync(db, config.BookingReferencePrefix, ct),
            TripId = trip.Id,
            PassengerName = request.PassengerName.Trim(),
            PassengerPhone = phone!,
            PassengerEmail = string.IsNullOrWhiteSpace(request.PassengerEmail) ? null : request.PassengerEmail.Trim(),
            Seats = seats.Select(s => new BookingSeat { SeatNumber = s.SeatNumber }).ToList(),
            UnitFare = trip.Fare,
            SeatCount = seats.Count,
            TotalAmount = total,
            PaymentMode = request.PaymentMode,
            PaymentStatus = paidNow ? PaymentStatus.Verified : PaymentStatus.Pending,
            Status = paidNow ? BookingStatus.Confirmed : BookingStatus.Reserved,
            BookedByStaffId = request.BookedByStaffId,
            BoardingCounterId = trip.BoardingCounterId,
            DroppingCounterId = trip.DroppingCounterId,
            ConfirmedAt = paidNow ? now : null,
            HoldExpiresAt = paidNow ? null : now.AddHours(request.PaymentMode == PaymentMode.Online
                ? config.PendingOnlinePaymentExpiryHours
                : config.CounterReservationExpiryHours),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes!.Trim(),
            Payment = new Payment
            {
                Mode = request.PaymentMode,
                Provider = request.Provider,
                TransactionId = request.TransactionId?.Trim(),
                SenderMsisdn = PhoneNumber.Normalize(request.SenderMsisdn),
                Amount = total,
                Status = paidNow ? PaymentStatus.Verified : PaymentStatus.Pending,
                SubmittedAt = request.PaymentMode == PaymentMode.Online || paidNow ? now : null,
                ReviewedByStaffId = paidNow ? request.BookedByStaffId : null,
                ReviewedAt = paidNow ? now : null
            }
        };

        foreach (var seat in seats)
        {
            seat.Status = SeatStatus.Booked;
            seat.HoldToken = null;
            seat.HoldExpiresAt = null;
            seat.Booking = booking;
        }

        db.Bookings.Add(booking);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Booking save failed for trip {Trip}.", request.TripId);
            return OperationResult<string>.Fail("A seat was taken while you were checking out. Please try again.");
        }

        broadcaster.Notify(trip.Id);
        await audit.RecordAsync(AuditActions.BookingCreate, nameof(Booking), booking.Id.ToString(),
            $"Booking {booking.Reference} — {booking.SeatCount} seat(s), ৳{total:0}, {request.PaymentMode}"
                + (request.BookedByStaffId is not null ? " (counter sale)" : ""),
            request.BookedByStaffId, request.BookedByStaffId is not null ? "counter" : "customer", ct: ct);

        await SendConfirmationSmsAsync(booking, trip, paidNow, ct);

        return OperationResult<string>.Ok(booking.Reference);
    }

    public async Task<Booking?> GetByReferenceAsync(string reference, CancellationToken ct = default)
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
            .FirstOrDefaultAsync(b => b.Reference == reference.Trim().ToUpperInvariant(), ct);
    }

    public async Task<List<Booking>> HistoryByPhoneAsync(string phone, CancellationToken ct = default)
    {
        var normalized = PhoneNumber.Normalize(phone);
        if (normalized is null)
            return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Bookings.AsNoTracking()
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .Include(b => b.Trip).ThenInclude(t => t.Bus)
            .Include(b => b.Payment)
            .Include(b => b.Seats)
            .Where(b => b.PassengerPhone == normalized)
            .OrderByDescending(b => b.Trip.DepartureTime)
            .ToListAsync(ct);
    }

    /// <summary>What the passenger would get back if they cancelled right now.</summary>
    public async Task<RefundPreview> PreviewCancellationAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.AsNoTracking()
            .Include(b => b.Trip)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
            return new RefundPreview(false, "Booking not found.", 0, 0, 0);
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            return new RefundPreview(false, "This booking is already closed.", 0, 0, 0);

        var hours = (booking.Trip.DepartureTime - clock.UtcNow).TotalHours;
        if (hours <= 0)
            return new RefundPreview(false, "This trip has already departed.", 0, 0, 0);

        var policy = await cancellationPolicies.GetDefaultAsync(ct);
        var percent = policy.RefundPercentFor(hours);
        var amount = Math.Round(booking.TotalAmount * percent / 100m, 0);
        return new RefundPreview(true, null, hours, percent, amount);
    }

    /// <summary>Self-service cancellation — the phone number is the passenger's only credential.</summary>
    public async Task<OperationResult> CancelByCustomerAsync(
        Guid bookingId, string phone, string? reason, CancellationToken ct = default)
    {
        var normalized = PhoneNumber.Normalize(phone);
        if (normalized is null)
            return OperationResult.Fail("That doesn't look like a valid mobile number.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var booking = await db.Bookings
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.OriginLocation)
            .Include(b => b.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.DestinationLocation)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null || booking.PassengerPhone != normalized)
            return OperationResult.Fail("Booking not found.");
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            return OperationResult.Fail("This booking is already closed.");

        var hours = (booking.Trip.DepartureTime - clock.UtcNow).TotalHours;
        if (hours <= 0)
            return OperationResult.Fail("This trip has already departed.");

        var policy = await cancellationPolicies.GetDefaultAsync(ct);
        var percent = policy.RefundPercentFor(hours);
        var amount = Math.Round(booking.TotalAmount * percent / 100m, 0);

        var now = clock.UtcNow;
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = now;
        booking.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by passenger" : reason.Trim();
        booking.RefundAmount = amount;

        await db.TripSeats.Where(s => s.BookingId == bookingId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SeatStatus.Available)
                .SetProperty(x => x.BookingId, (Guid?)null)
                .SetProperty(x => x.HoldToken, (Guid?)null)
                .SetProperty(x => x.HoldExpiresAt, (DateTimeOffset?)null), ct);

        await db.SaveChangesAsync(ct);
        broadcaster.Notify(booking.TripId);
        await audit.RecordAsync(AuditActions.BookingCancel, nameof(Booking), booking.Id.ToString(),
            $"Self-service cancel {booking.Reference} — {percent}% refund (৳{amount:0})",
            actorId: null, actorName: "customer", ct: ct);

        await sms.SendAsync(booking.PassengerPhone,
            TicketMessages.BookingCancelled(booking, $"refund ৳{amount:0} ({percent}%) will be arranged"),
            SmsPurpose.BookingCancelled, booking.Id, ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult> ResubmitPaymentAsync(
        string reference, MfsProvider provider, string transactionId, string? senderMsisdn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            return OperationResult.Fail("Enter the transaction ID from your payment.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Reference == reference.Trim().ToUpperInvariant(), ct);
        if (booking is null)
            return OperationResult.Fail("Booking not found.");
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            return OperationResult.Fail("This booking is no longer active.");
        if (booking.PaymentStatus == PaymentStatus.Verified)
            return OperationResult.Fail("This booking is already paid.");

        booking.Payment ??= new Payment { BookingId = booking.Id, Amount = booking.TotalAmount };
        booking.Payment.Mode = PaymentMode.Online;
        booking.Payment.Provider = provider;
        booking.Payment.TransactionId = transactionId.Trim();
        booking.Payment.SenderMsisdn = PhoneNumber.Normalize(senderMsisdn);
        booking.Payment.Status = PaymentStatus.Pending;
        booking.Payment.SubmittedAt = clock.UtcNow;
        booking.Payment.ReviewedAt = null;
        booking.Payment.ReviewedByStaffId = null;
        booking.Payment.ReviewNote = null;

        booking.PaymentMode = PaymentMode.Online;
        booking.PaymentStatus = PaymentStatus.Pending;
        booking.HoldExpiresAt = clock.UtcNow.AddHours(settings.Current.PendingOnlinePaymentExpiryHours);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.BookingCreate, nameof(Payment), booking.Id.ToString(),
            $"Transaction ID resubmitted for {booking.Reference}", actorId: null, actorName: "customer", ct: ct);
        return OperationResult.Ok();
    }

    private Task SendConfirmationSmsAsync(Booking booking, Trip trip, bool paidNow, CancellationToken ct)
    {
        var link = TicketMessages.BuildLink(settings.Current.PublicBaseUrl, booking.Reference);
        var message = paidNow
            ? TicketMessages.PaymentConfirmed(booking, trip, clock, link)
            : TicketMessages.BookingReceived(booking, trip, clock, link);
        var purpose = paidNow ? SmsPurpose.PaymentVerified : SmsPurpose.BookingCreated;
        return sms.SendAsync(booking.PassengerPhone, message, purpose, booking.Id, ct);
    }

    /// <summary>Reserved bookings whose payment window lapsed — release the seats.</summary>
    public async Task<int> ExpireStaleAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var stale = await db.Bookings
            .Where(b => b.Status == BookingStatus.Reserved
                && b.PaymentStatus != PaymentStatus.Verified
                && b.HoldExpiresAt != null && b.HoldExpiresAt < now)
            .Select(b => new { b.Id, b.TripId, b.Reference })
            .ToListAsync(ct);

        if (stale.Count == 0)
            return 0;

        var ids = stale.Select(s => s.Id).ToList();

        await db.Bookings.Where(b => ids.Contains(b.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, BookingStatus.Expired)
                .SetProperty(b => b.CancelledAt, now)
                .SetProperty(b => b.CancellationReason, "Payment not settled in time")
                .SetProperty(b => b.UpdatedAt, now), ct);

        await db.TripSeats.Where(s => s.BookingId != null && ids.Contains(s.BookingId!.Value))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SeatStatus.Available)
                .SetProperty(x => x.BookingId, (Guid?)null)
                .SetProperty(x => x.HoldToken, (Guid?)null)
                .SetProperty(x => x.HoldExpiresAt, (DateTimeOffset?)null), ct);

        foreach (var tripId in stale.Select(s => s.TripId).Distinct())
            broadcaster.Notify(tripId);

        logger.LogInformation("Expired {Count} unpaid bookings and freed their seats.", stale.Count);
        return stale.Count;
    }

    private async Task<string> UniqueReferenceAsync(AppDbContext db, string prefix, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = BookingReference.New(prefix);
            if (!await db.Bookings.AnyAsync(b => b.Reference == candidate, ct))
                return candidate;
        }
        return BookingReference.New(prefix, 8);
    }

    private static string? Validate(BookingRequest r, string? normalizedPhone)
    {
        if (r.SeatNumbers.Count == 0) return "Choose at least one seat.";
        if (r.SeatNumbers.Count > 6) return "That's more than the six-seat limit.";
        if (string.IsNullOrWhiteSpace(r.PassengerName) || r.PassengerName.Trim().Length < 2)
            return "Enter the passenger's name.";
        if (normalizedPhone is null)
            return "Enter a valid Bangladeshi mobile number (01XXXXXXXXX).";
        if (!string.IsNullOrWhiteSpace(r.PassengerEmail) && !r.PassengerEmail.Contains('@'))
            return "That email address doesn't look right.";

        if (r.PaymentMode == PaymentMode.Online && !r.MarkPaidNow)
        {
            if (r.Provider is null) return "Pick the wallet you paid from.";
            if (string.IsNullOrWhiteSpace(r.TransactionId)) return "Enter the transaction ID from your payment.";
        }
        return null;
    }
}
