using BusTicketing.Data;
using BusTicketing.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services.Admin;

public sealed record StaffRow(
    Guid Id, string Username, string FullName, string Role,
    string? CounterName, bool IsActive, DateTimeOffset? LastLoginAt);

public sealed record StaffInput
{
    public Guid? Id { get; init; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = Roles.CounterStaff;
    public Guid? CounterLocationId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Password { get; set; }
}

public sealed class StaffService(
    UserManager<StaffUser> userManager,
    IDbContextFactory<AppDbContext> dbFactory,
    AuditService audit)
{
    public async Task<List<StaffRow>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var users = await db.Users
            .Include(u => u.CounterLocation)
            .AsNoTracking()
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        var roleByUser = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, r.Name })
            .ToDictionaryAsync(x => x.UserId, x => x.Name ?? "", ct);

        return users.Select(u => new StaffRow(
            u.Id, u.UserName ?? "", u.FullName,
            roleByUser.GetValueOrDefault(u.Id, Roles.CounterStaff),
            u.CounterLocation?.DisplayName, u.IsActive, u.LastLoginAt)).ToList();
    }

    public async Task<StaffInput?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return null;

        var roles = await userManager.GetRolesAsync(user);
        return new StaffInput
        {
            Id = user.Id,
            Username = user.UserName ?? "",
            FullName = user.FullName,
            Role = roles.FirstOrDefault() ?? Roles.CounterStaff,
            CounterLocationId = user.CounterLocationId,
            IsActive = user.IsActive
        };
    }

    public async Task<OperationResult<Guid>> CreateAsync(StaffInput input, CancellationToken ct = default)
    {
        var validation = Validate(input, isNew: true);
        if (validation is not null)
            return OperationResult<Guid>.Fail(validation);

        if (await userManager.FindByNameAsync(input.Username.Trim()) is not null)
            return OperationResult<Guid>.Fail("That username is taken.");

        var user = new StaffUser
        {
            UserName = input.Username.Trim(),
            FullName = input.FullName.Trim(),
            IsActive = input.IsActive,
            CounterLocationId = input.Role == Roles.CounterStaff ? input.CounterLocationId : null,
            EmailConfirmed = true
        };

        var created = await userManager.CreateAsync(user, input.Password!);
        if (!created.Succeeded)
            return OperationResult<Guid>.Fail(Describe(created));

        await userManager.AddToRoleAsync(user, input.Role);
        await audit.RecordAsync(AuditActions.EntityCreate, nameof(StaffUser), user.Id.ToString(),
            $"Created {input.Role} account “{user.UserName}”");
        return OperationResult<Guid>.Ok(user.Id);
    }

    public async Task<OperationResult> UpdateAsync(StaffInput input, CancellationToken ct = default)
    {
        var validation = Validate(input, isNew: false);
        if (validation is not null)
            return OperationResult.Fail(validation);

        var user = await userManager.FindByIdAsync(input.Id!.Value.ToString());
        if (user is null)
            return OperationResult.Fail("Account not found.");

        user.FullName = input.FullName.Trim();
        user.IsActive = input.IsActive;
        user.CounterLocationId = input.Role == Roles.CounterStaff ? input.CounterLocationId : null;

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return OperationResult.Fail(Describe(updated));

        var currentRoles = await userManager.GetRolesAsync(user);
        if (!currentRoles.Contains(input.Role))
        {
            await userManager.RemoveFromRolesAsync(user, currentRoles);
            await userManager.AddToRoleAsync(user, input.Role);
        }

        if (!input.IsActive)
            await userManager.UpdateSecurityStampAsync(user); // drop any live session

        await audit.RecordAsync(AuditActions.EntityUpdate, nameof(StaffUser), user.Id.ToString(),
            $"Updated account “{user.UserName}”");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        if (newPassword.Length < 8)
            return OperationResult.Fail("Use at least 8 characters.");

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return OperationResult.Fail("Account not found.");

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            return OperationResult.Fail(Describe(result));

        await userManager.UpdateSecurityStampAsync(user);
        await audit.RecordAsync(AuditActions.EntityUpdate, nameof(StaffUser), id.ToString(),
            $"Reset password for “{user.UserName}”");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return OperationResult.Fail("Account not found.");

        user.IsActive = active;
        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user);
        await audit.RecordAsync(AuditActions.EntityUpdate, nameof(StaffUser), id.ToString(),
            active ? $"Reactivated “{user.UserName}”" : $"Deactivated “{user.UserName}”");
        return OperationResult.Ok();
    }

    private static string? Validate(StaffInput input, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(input.FullName)) return "Enter the person's name.";
        if (isNew && string.IsNullOrWhiteSpace(input.Username)) return "Choose a username.";
        if (isNew && (input.Password is null || input.Password.Length < 8))
            return "Set a starting password of at least 8 characters.";
        if (input.Role is not (Roles.SuperAdmin or Roles.CounterStaff))
            return "Pick a valid role.";
        if (input.Role == Roles.CounterStaff && input.CounterLocationId is null)
            return "Assign the counter staff to a counter.";
        return null;
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));
}
