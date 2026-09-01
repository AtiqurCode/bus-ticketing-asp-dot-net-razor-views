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

    private static Guid? ParseUserId(ClaimsPrincipal? actor) =>
        Guid.TryParse(actor?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
