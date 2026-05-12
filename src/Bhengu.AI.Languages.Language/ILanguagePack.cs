namespace Bhengu.AI.Languages.Language;

/// <summary>Metadata for a language pack.</summary>
public sealed record LanguagePackMetadata(
    string BcpTag,
    string DisplayName,
    string NativeName,
    string PrimaryRegion,
    string[] SpokenInRegions,
    Version PackVersion);

/// <summary>Cultural/contextual note for a specific topic.</summary>
public sealed record CulturalNote(string Context, string Guidance, string[] Examples);

/// <summary>
/// A language-specific knowledge pack.
/// Provides idiomatic expressions, cultural context, and prompt tuning
/// for the on-device LLM to reason correctly in this language.
/// </summary>
public interface ILanguagePack
{
    LanguagePackMetadata Metadata { get; }

    /// <summary>Returns the idiomatic translation of a common phrase, or null if not mapped.</summary>
    string? GetIdiomaticExpression(string phrase);

    /// <summary>Adapts a base system prompt for this language and culture.</summary>
    string AdaptSystemPrompt(string basePrompt);

    /// <summary>Cultural notes for a given context (e.g. "greeting", "business", "medical").</summary>
    IReadOnlyList<CulturalNote> GetCulturalNotes(string context);

    /// <summary>Returns a locale-appropriate greeting for the given time of day.</summary>
    string GetGreeting(string timeOfDay);

    /// <summary>Returns locale-specific number/date/currency formatting hints.</summary>
    IReadOnlyDictionary<string, string> GetLocaleHints();
}
