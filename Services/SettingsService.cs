using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services;

/// <summary>
/// Reads and writes the single <see cref="AppSettings"/> row, keeping a cached
/// copy so the layout and clock don't hit the database on every render.
/// </summary>
public sealed class SettingsService(IDbContextFactory<AppDbContext> dbFactory)
{
    private AppSettings? _cache;
    private readonly Lock _gate = new();

    public AppSettings Current
    {
        get
        {
            if (_cache is not null) return _cache;
            lock (_gate)
            {
                _cache ??= Load();
                return _cache;
            }
        }
    }

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AppSettings.SingletonId, ct)
            ?? new AppSettings();

        lock (_gate) _cache = settings;
        return settings;
    }

    public async Task SaveAsync(AppSettings updated, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        updated.Id = AppSettings.SingletonId;
        updated.UpdatedAt = DateTimeOffset.UtcNow;

        var exists = await db.AppSettings.AnyAsync(s => s.Id == AppSettings.SingletonId, ct);
        db.AppSettings.Update(updated);
        if (!exists) db.Entry(updated).State = EntityState.Added;

        await db.SaveChangesAsync(ct);
        lock (_gate) _cache = updated;
    }

    /// <summary>Drops the cache so the next read reloads from the database.</summary>
    public void Invalidate()
    {
        lock (_gate) _cache = null;
    }

    private AppSettings Load()
    {
        using var db = dbFactory.CreateDbContext();
        return db.AppSettings.AsNoTracking()
            .FirstOrDefault(s => s.Id == AppSettings.SingletonId) ?? new AppSettings();
    }
}
