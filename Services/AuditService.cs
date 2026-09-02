using System.Security.Claims;
using System.Text.Json;
using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services;

/// <summary>Writes the append-only <see cref="AuditLog"/>. Never throws into the caller.</summary>
public sealed class AuditService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHttpContextAccessor httpContext,
    ILogger<AuditService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Attributes the entry to the current signed-in staff user.</summary>
    public Task RecordAsync(
        string action, string entityType, string? entityId, string summary,
        object? detail = null, CancellationToken ct = default)
    {
        var user = httpContext.HttpContext?.User;
        return RecordAsync(action, entityType, entityId, summary,
            ParseUserId(user), user?.Identity?.Name ?? "system", detail, ct);
    }

    /// <summary>
    /// Attributes the entry to <paramref name="actor"/> explicitly — used right
    /// after sign-in, before <c>HttpContext.User</c> reflects the new cookie.
    /// </summary>
    public Task RecordAsync(
        string action, string entityType, string? entityId, string summary,
        ClaimsPrincipal? actor, object? detail = null, CancellationToken ct = default)
        => RecordAsync(action, entityType, entityId, summary,
            ParseUserId(actor), actor?.Identity?.Name ?? "system", detail, ct);

    public async Task RecordAsync(
        string action, string entityType, string? entityId, string summary,
        Guid? actorId, string actorName, object? detail = null, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorId,
                ActorName = actorName,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Summary = summary,
                DetailJson = detail is null ? null : JsonSerializer.Serialize(detail, JsonOptions),
                IpAddress = httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString()
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write audit log for {Action} on {EntityType} {EntityId}",
                action, entityType, entityId);
        }
    }

    public async Task<AuditPage> QueryAsync(
        string? action = null, string? search = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);
        if (from is not null)
            query = query.Where(a => a.CreatedAt >= from);
        if (to is not null)
            query = query.Where(a => a.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.Summary, term) ||
                EF.Functions.ILike(a.ActorName, term) ||
                EF.Functions.ILike(a.EntityType, term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new AuditPage(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<string>> DistinctActionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditLogs.AsNoTracking()
            .Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);
    }

    private static Guid? ParseUserId(ClaimsPrincipal? actor) =>
        Guid.TryParse(actor?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

public sealed record AuditPage(IReadOnlyList<AuditLog> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
}
