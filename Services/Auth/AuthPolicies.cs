namespace BusTicketing.Services.Auth;

public static class AuthPolicies
{
    /// <summary>Any signed-in staff account — counter or admin.</summary>
    public const string BackOffice = "BackOffice";

    /// <summary>Super administrators only.</summary>
    public const string SuperAdmin = "SuperAdminOnly";
}
