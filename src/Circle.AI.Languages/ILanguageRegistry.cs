namespace Circle.AI.Languages;

/// <summary>Registry of all BCP-47 language tags that Circle AI understands.</summary>
public interface ILanguageRegistry
{
    LanguageTag? GetByBcpTag(string bcpTag);
    IReadOnlyList<LanguageTag> GetAll();
    IReadOnlyList<LanguageTag> GetForRegion(string isoRegion);
    bool IsSupported(string bcpTag);
}
