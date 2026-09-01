using Microsoft.AspNetCore.Components.Web;

namespace BusTicketing.Components;

/// <summary>Shared render-mode instances.</summary>
public static class RenderModes
{
    /// <summary>
    /// Interactive server with prerendering off — for form-heavy pages where a
    /// half-second of hydration lag could otherwise swallow the first keystrokes.
    /// </summary>
    public static readonly InteractiveServerRenderMode ServerNoPrerender = new(prerender: false);
}
