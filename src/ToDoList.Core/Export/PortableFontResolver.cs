using PdfSharp.Fonts;

namespace ToDoList.Core.Export;

internal sealed class PortableFontResolver : IFontResolver
{
    private readonly byte[] _font;
    public PortableFontResolver(string fontFile) => _font = File.ReadAllBytes(fontFile);

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new("PlanSans", bold, italic);

    public byte[] GetFont(string faceName) => _font;
}
