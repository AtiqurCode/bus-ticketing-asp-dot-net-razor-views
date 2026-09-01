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
}

public sealed class AppClock(SettingsService settings) : IAppClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeZoneInfo TimeZone => settings.Current.ResolveTimeZone();

    public DateTimeOffset ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZone);

    public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow).DateTime);
}
