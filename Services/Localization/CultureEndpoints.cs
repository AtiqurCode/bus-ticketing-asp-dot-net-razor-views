using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace BusTicketing.Services.Localization;

public static class CultureEndpoints
{
    /// <summary>
    /// Writes the culture cookie and bounces back. A plain link so the language
    /// switcher needs no interactive circuit and no antiforgery token.
    /// </summary>
    public static IEndpointRouteBuilder MapCultureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/culture/set", (HttpContext http, string culture, string? redirectUri) =>
        {
            if (CultureInfo.GetCultures(CultureTypes.AllCultures).Any(c => c.Name == culture))
            {
                http.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddYears(1),
                        IsEssential = true,
                        Path = "/",
                        SameSite = SameSiteMode.Lax
                    });
            }

            var target = string.IsNullOrWhiteSpace(redirectUri) ? "/" : redirectUri;
            return Results.LocalRedirect(target);
        });

        return endpoints;
    }
}
