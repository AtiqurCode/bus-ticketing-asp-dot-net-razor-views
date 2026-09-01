using Microsoft.AspNetCore.Identity;

namespace BusTicketing.Domain;

/// <summary>
/// A back-office account — either a super admin or counter staff. Customers never
/// have one of these; they are identified by phone number alone.
/// </summary>
public class StaffUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";

    /// <summary>The counter this person sells from; null for super admins.</summary>
    public Guid? CounterLocationId { get; set; }

    public Location? CounterLocation { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }
}

public class StaffRole : IdentityRole<Guid>
{
    public StaffRole() { }

    public StaffRole(string roleName) : base(roleName) { }
}

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string CounterStaff = "CounterStaff";

    public static readonly string[] All = [SuperAdmin, CounterStaff];
}
