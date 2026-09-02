using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Bookings;

public sealed record CancellationRuleInput(int MinHoursBeforeDeparture, int RefundPercent);

public sealed class CancellationPolicyService(IDbContextFactory<AppDbContext> dbFactory, AuditService audit)
{
    public async Task<CancellationPolicy> GetDefaultAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var policy = await db.CancellationPolicies.AsNoTracking()
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.IsDefault, ct);

        return policy ?? new CancellationPolicy { Name = "Standard", IsDefault = true, IsActive = true };
    }

    public async Task<OperationResult> SaveDefaultAsync(
        string name, IReadOnlyList<CancellationRuleInput> rules, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return OperationResult.Fail("Name the policy.");
        if (rules.Count == 0)
            return OperationResult.Fail("Add at least one tier.");
        if (rules.Any(r => r.MinHoursBeforeDeparture < 0 || r.RefundPercent is < 0 or > 100))
            return OperationResult.Fail("Hours must be 0 or more and refund must be 0–100%.");
        if (rules.Select(r => r.MinHoursBeforeDeparture).Distinct().Count() != rules.Count)
            return OperationResult.Fail("Each tier needs a distinct hours-before-departure threshold.");
        if (!rules.Any(r => r.MinHoursBeforeDeparture == 0))
            return OperationResult.Fail("Add a 0-hour tier so every cancellation resolves to a refund percentage.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var policy = await db.CancellationPolicies.Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.IsDefault, ct);

        if (policy is null)
        {
            policy = new CancellationPolicy { IsDefault = true, IsActive = true };
            db.CancellationPolicies.Add(policy);
        }

        policy.Name = name.Trim();

        // Rules carry a client-generated Guid key (Entity.Id), so EF's change-tracker
        // fixup would mistake a plain policy.Rules.Add(new …) for an *existing* row
        // and try to UPDATE it — 0 rows affected, concurrency exception. Adding the
        // new rows straight to the DbSet forces them to be tracked as Added.
        db.CancellationRules.RemoveRange(policy.Rules);
        policy.Rules.Clear();
        db.CancellationRules.AddRange(rules.Select(r => new CancellationRule
        {
            PolicyId = policy.Id,
            MinHoursBeforeDeparture = r.MinHoursBeforeDeparture,
            RefundPercent = r.RefundPercent
        }));

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditActions.SettingsUpdate, nameof(CancellationPolicy), policy.Id.ToString(),
            $"Updated cancellation policy “{policy.Name}” ({rules.Count} tiers)");
        return OperationResult.Ok();
    }
}
