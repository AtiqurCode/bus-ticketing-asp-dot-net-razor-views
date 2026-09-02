using System.Reflection;
using QuestPDF.Drawing;

namespace BusTicketing.Services.Ticketing;

/// <summary>
/// QuestPDF needs a font that actually carries Bengali glyphs registered before
/// any document uses it — passenger names and route labels can be Bangla text
/// regardless of which UI language booked the ticket. Hind Siliguri covers both
/// scripts, so it's the one font every ticket PDF uses. The .ttf files are
/// embedded in the assembly (see the .csproj) so this works in every packaging.
/// </summary>
public static class PdfFonts
{
    public const string Family = "Hind Siliguri";

    public static void RegisterEmbeddedFonts()
    {
        var assembly = typeof(PdfFonts).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is not null)
                FontManager.RegisterFont(stream);
        }
    }
}
