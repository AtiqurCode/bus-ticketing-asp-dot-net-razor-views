namespace BusTicketing.Domain;

public enum LocationType
{
    City,
    Terminal,
    Counter
}

public enum BusClass
{
    NonAc,
    Ac,
    AcSleeper,
    AcBusiness
}

public enum SeatType
{
    Regular,
    Window,
    Premium,
    Ladies
}

public enum TripStatus
{
    Scheduled,
    Departed,
    Completed,
    Cancelled
}

public enum SeatStatus
{
    Available,
    Held,
    Booked,
    Blocked
}

public enum PaymentMode
{
    Online,
    Counter
}

public enum PaymentStatus
{
    Pending,
    Verified,
    Rejected,
    Refunded
}

public enum BookingStatus
{
    /// <summary>Seats held, awaiting payment (online) or counter collection.</summary>
    Reserved,
    Confirmed,
    Cancelled,
    /// <summary>Hold lapsed before payment was settled.</summary>
    Expired
}

public enum MfsProvider
{
    Bkash,
    Nagad,
    Rocket,
    Upay,
    Other,
    Cash
}

/// <summary>Bitmask of the weekdays a schedule template runs on.</summary>
[Flags]
public enum WeekDays
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,
    All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday
}

public static class WeekDaysExtensions
{
    public static bool Includes(this WeekDays days, DayOfWeek day) =>
        (days & FromDayOfWeek(day)) != 0;

    public static WeekDays FromDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => WeekDays.Sunday,
        DayOfWeek.Monday => WeekDays.Monday,
        DayOfWeek.Tuesday => WeekDays.Tuesday,
        DayOfWeek.Wednesday => WeekDays.Wednesday,
        DayOfWeek.Thursday => WeekDays.Thursday,
        DayOfWeek.Friday => WeekDays.Friday,
        DayOfWeek.Saturday => WeekDays.Saturday,
        _ => WeekDays.None
    };
}
