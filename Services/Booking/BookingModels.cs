using BusTicketing.Domain;

namespace BusTicketing.Services.Bookings;

public enum SeatViewStatus
{
    Available,
    Mine,      // held by this checkout session
    Taken,     // held by someone else, hold still live
    Booked,
    Blocked
}

public sealed record SeatView(
    string Number, int Row, int Column, int Deck, SeatType Type, SeatViewStatus Status)
{
    public bool Selectable => Status is SeatViewStatus.Available or SeatViewStatus.Mine;
}

public sealed record TripBookingContext(
    Guid TripId,
    string OriginName,
    string DestinationName,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    string BusName,
    string Operator,
    string ClassLabel,
    decimal Fare,
    int Rows,
    int Columns,
    int Decks,
    string? BoardingPoint,
    string? DroppingPoint,
    TripStatus Status)
{
    public IReadOnlyList<SeatView> Seats { get; init; } = [];

    /// <summary>Earliest expiry among the seats this session is holding, if any.</summary>
    public DateTimeOffset? MyHoldExpiresAt { get; init; }

    public IReadOnlyList<string> MySeatNumbers =>
        Seats.Where(s => s.Status == SeatViewStatus.Mine).Select(s => s.Number).ToList();
}

public sealed record HoldResult(bool Succeeded, string? Error, IReadOnlyList<string> MySeats)
{
    public static HoldResult Ok(IReadOnlyList<string> mySeats) => new(true, null, mySeats);
    public static HoldResult Fail(string error, IReadOnlyList<string> mySeats) => new(false, error, mySeats);
}

public sealed record BookingRequest
{
    public Guid TripId { get; init; }
    public Guid HoldToken { get; init; }
    public IReadOnlyList<string> SeatNumbers { get; init; } = [];
    public string PassengerName { get; init; } = "";
    public string PassengerPhone { get; init; } = "";
    public string? PassengerEmail { get; init; }
    public PaymentMode PaymentMode { get; init; }
    public MfsProvider? Provider { get; init; }
    public string? TransactionId { get; init; }
    public string? SenderMsisdn { get; init; }
    public Guid? BookedByStaffId { get; init; }
    /// <summary>Staff selling face-to-face can take the cash there and then.</summary>
    public bool MarkPaidNow { get; init; }
    public string? Notes { get; init; }
}
