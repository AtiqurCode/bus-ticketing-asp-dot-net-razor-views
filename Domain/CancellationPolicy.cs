namespace BusTicketing.Domain;

/// <summary>
/// A tiered refund schedule. Exactly one policy is the default; a booking's
/// refund is decided by the first rule whose <see cref="CancellationRule.MinHoursBeforeDeparture"/>
/// the cancellation still clears.
/// </summary>
public class CancellationPolicy : Entity
{
    public string Name { get; set; } = "";

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public List<CancellationRule> Rules { get; set; } = [];

    /// <summary>Percent (0–100) refunded for a cancellation <paramref name="hoursBeforeDeparture"/> out.</summary>
    public int RefundPercentFor(double hoursBeforeDeparture)
    {
        var rule = Rules
            .OrderByDescending(r => r.MinHoursBeforeDeparture)
            .FirstOrDefault(r => hoursBeforeDeparture >= r.MinHoursBeforeDeparture);

        return rule?.RefundPercent ?? 0;
    }
}

public class CancellationRule : Entity
{
    public Guid PolicyId { get; set; }

    public CancellationPolicy Policy { get; set; } = null!;

    /// <summary>Cancel at least this many hours before departure to earn <see cref="RefundPercent"/>.</summary>
    public int MinHoursBeforeDeparture { get; set; }

    public int RefundPercent { get; set; }
}
