using BusTicketing.Domain;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BusTicketing.Services.Auth;

/// <summary>
/// Keeps the circuit's authentication state honest: every 30 minutes it
/// re-checks the user's security stamp against the database, so a disabled or
/// password-changed account drops offline without waiting for the cookie to expire.
/// </summary>
public sealed class IdentityRevalidatingStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<StaffUser>>();

        var user = await userManager.GetUserAsync(authenticationState.User);
        if (user is null || !user.IsActive)
            return false;

        if (!userManager.SupportsUserSecurityStamp)
            return true;

        var principalStamp = authenticationState.User.FindFirst(
            options.Value.ClaimsIdentity.SecurityStampClaimType)?.Value;
        var currentStamp = await userManager.GetSecurityStampAsync(user);
        return principalStamp == currentStamp;
    }
}
