namespace Bhengu.AI.Languages.Language;

/// <summary>Thread-safe in-memory <see cref="ILanguagePackRegistry"/>.</summary>
public sealed class DefaultLanguagePackRegistry : ILanguagePackRegistry
{
    private readonly Dictionary<string, ILanguagePack> _packs = [];
    private readonly Lock _lock = new();

    public void Register(ILanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        lock (_lock) _packs[pack.Metadata.BcpTag] = pack;
    }

    public ILanguagePack? GetByBcpTag(string bcpTag)
    {
        lock (_lock) return _packs.TryGetValue(bcpTag, out var p) ? p : null;
    }

    public IReadOnlyList<LanguagePackMetadata> GetAvailablePacks()
    {
        lock (_lock) return _packs.Values.Select(p => p.Metadata).ToList();
    }

    public bool HasPack(string bcpTag)
    {
        lock (_lock) return _packs.ContainsKey(bcpTag);
    }
}
