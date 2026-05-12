namespace Bhengu.AI.Languages;

/// <summary>Thread-safe <see cref="ILanguageRegistry"/> backed by <see cref="KnownLanguages.All"/>.</summary>
public sealed class DefaultLanguageRegistry : ILanguageRegistry
{
    private readonly Dictionary<string, LanguageTag> _byTag;
    private readonly ILookup<string, LanguageTag> _byRegion;

    public DefaultLanguageRegistry()
    {
        _byTag    = KnownLanguages.All.ToDictionary(t => t.BcpTag, StringComparer.OrdinalIgnoreCase);
        _byRegion = KnownLanguages.All.ToLookup(t => t.IsoRegion, StringComparer.OrdinalIgnoreCase);
    }

    public LanguageTag? GetByBcpTag(string bcpTag)
        => _byTag.TryGetValue(bcpTag, out var t) ? t : null;

    public IReadOnlyList<LanguageTag> GetAll()
        => KnownLanguages.All;

    public IReadOnlyList<LanguageTag> GetForRegion(string isoRegion)
        => _byRegion[isoRegion].ToList();

    public bool IsSupported(string bcpTag)
        => _byTag.ContainsKey(bcpTag);
}
