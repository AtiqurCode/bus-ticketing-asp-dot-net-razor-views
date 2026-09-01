namespace BusTicketing.Services;

/// <summary>
/// Wall-clock access, injectable so tests and the trip generator can pin "now".
/// All instants are UTC; <see cref="ToLocal"/> projects into the platform time
/// zone for display and for deciding which calendar day a departure falls on.
/// </summary>
public interface IAppClock
{
    DateTimeOffset UtcNow { get; }

    TimeZoneInfo TimeZone { get; }

    DateTimeOffset ToLocal(DateTimeOffset instant);

    DateOnly LocalToday { get; }

    /// <summary>Interpret a wall-clock date + time in the platform zone and return the UTC instant.</summary>
    DateTimeOffset ToInstant(DateOnly date, TimeOnly time);

    /// <summary>Interpret a wall-clock <see cref="DateTime"/> in the platform zone and return the UTC instant.</summary>
    DateTimeOffset ToInstant(DateTime localWallClock);
}

public sealed class AppClock(SettingsService settings) : IAppClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeZoneInfo TimeZone => settings.Current.ResolveTimeZone();

    public DateTimeOffset ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZone);

    public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow).DateTime);

    public DateTimeOffset ToInstant(DateOnly date, TimeOnly time) =>
        ToInstant(date.ToDateTime(time));

    public DateTimeOffset ToInstant(DateTime localWallClock)
    {
        var unspecified = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZone), TimeSpan.Zero);
    }
}
