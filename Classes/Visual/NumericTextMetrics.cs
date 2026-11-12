using Godot;

namespace Tempora.Classes.Visual;

internal static class NumericTextMetrics
{
    public static (float Top, float Bottom) GetDigitBounds(Label label)
    {
        Font font = label.GetThemeFont("font");
        int size = label.GetThemeFontSize("font_size");
        Rid rid = font.GetRids()[0];
        TextServer server = TextServerManager.GetPrimaryInterface();
        long glyph = server.FontGetGlyphIndex(rid, size, '0', 0);
        Vector2I cacheSize = new(size, 0);
        server.FontRenderGlyph(rid, cacheSize, glyph);
        float baseline = label.Size.Y - font.GetHeight(size) + font.GetAscent(size);
        float top = baseline + server.FontGetGlyphOffset(rid, cacheSize, glyph).Y;
        float bottom = top + server.FontGetGlyphSize(rid, cacheSize, glyph).Y;
        // Both labels contain digits and use bottom alignment. A shared digit keeps
        // their vertical positions stable when the displayed numbers change.
        return (top * label.Scale.Y, bottom * label.Scale.Y);
    }
}