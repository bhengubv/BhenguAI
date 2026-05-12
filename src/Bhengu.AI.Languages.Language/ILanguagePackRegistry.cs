namespace Bhengu.AI.Languages.Language;

/// <summary>Registry of all installed language packs.</summary>
public interface ILanguagePackRegistry
{
    void Register(ILanguagePack pack);
    ILanguagePack? GetByBcpTag(string bcpTag);
    IReadOnlyList<LanguagePackMetadata> GetAvailablePacks();
    bool HasPack(string bcpTag);
}
