namespace BusTicketing.Domain;

/// <summary>
/// Single-row table holding the knobs an admin can turn. Loaded once and cached;
/// <see cref="Id"/> is pinned to 1.
/// </summary>
public class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    // --- Trip generation ---------------------------------------------------

    /// <summary>How many days ahead the rolling booking window reaches.</summary>
    public int GenerationWindowDays { get; set; } = 7;

    /// <summary>Maintenance switch — pauses the auto-generation job entirely.</summary>
    public bool AutoGenerationPaused { get; set; }

    /// <summary>Interval choices (minutes) offered when building a schedule template.</summary>
    public string IntervalOptionsCsv { get; set; } = "60,90,120,180,240";

    // --- Booking / holds -------------------------------------------------

    /// <summary>Minutes a seat stays held during an online checkout.</summary>
    public int SeatHoldMinutes { get; set; } = 5;

    /// <summary>Hours an unpaid online booking survives before it auto-cancels.</summary>
    public int PendingOnlinePaymentExpiryHours { get; set; } = 6;

    /// <summary>Hours a "pay at counter" reservation is held.</summary>
    public int CounterReservationExpiryHours { get; set; } = 12;

    // --- Presentation --------------------------------------------------

    public string TimeZoneId { get; set; } = "Asia/Dhaka";

    public string CurrencyCode { get; set; } = "BDT";

    public string BookingReferencePrefix { get; set; } = "BT";

    public string SiteName { get; set; } = "TicketBari";

    public string SupportPhone { get; set; } = "16xxx";

    /// <summary>Public URL the site is reachable at, e.g. https://ticketbari.example.
    /// Used to build links in SMS messages. Left blank until the domain is live.</summary>
    public string? PublicBaseUrl { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public IEnumerable<int> IntervalOptions() =>
        IntervalOptionsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, out var n) ? n : 0)
            .Where(n => n > 0);

    public TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
