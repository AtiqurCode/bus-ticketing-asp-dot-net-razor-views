namespace BusTicketing.Resources;

/// <summary>
/// Marker type for the shared string catalogue. Inject
/// <c>IStringLocalizer&lt;SharedResource&gt;</c> to resolve UI text; the
/// <c>SharedResource.resx</c> / <c>SharedResource.bn.resx</c> pair backs it.
/// </summary>
public sealed class SharedResource;
