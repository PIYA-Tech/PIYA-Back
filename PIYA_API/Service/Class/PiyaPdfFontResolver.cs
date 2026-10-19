using PdfSharp.Fonts;

namespace PIYA_API.Service.Class;

/// <summary>Loads only the bundled, licensed faces used by PIYA PDF exports.</summary>
public sealed class PiyaPdfFontResolver : IFontResolver
{
    private static readonly object Gate = new();
    public const string Family = "Liberation Sans";

    public static void EnsureConfigured()
    {
        lock (Gate)
        {
            if (GlobalFontSettings.FontResolver is PiyaPdfFontResolver) return;
            if (GlobalFontSettings.FontResolver != null)
                throw new InvalidOperationException("A different global PDF font resolver is already configured.");
            GlobalFontSettings.FontResolver = new PiyaPdfFontResolver();
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (!string.Equals(familyName, Family, StringComparison.OrdinalIgnoreCase)) return null;
        var style = bold ? (italic ? "BoldItalic" : "Bold") : (italic ? "Italic" : "Regular");
        return new FontResolverInfo($"LiberationSans-{style}");
    }

    public byte[] GetFont(string faceName)
    {
        if (faceName is not ("LiberationSans-Regular" or "LiberationSans-Bold" or "LiberationSans-Italic" or "LiberationSans-BoldItalic"))
            throw new InvalidOperationException("Unknown PDF font face.");
        using var resource = typeof(PiyaPdfFontResolver).Assembly.GetManifestResourceStream($"PIYA_API.Assets.Fonts.{faceName}.ttf")
            ?? throw new InvalidOperationException($"Bundled PDF font {faceName} is missing.");
        using var stream = new MemoryStream();
        resource.CopyTo(stream);
        return stream.ToArray();
    }
}
