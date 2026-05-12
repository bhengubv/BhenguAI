// LanguageTypes.cs — core language primitives
namespace Circle.AI.Languages;

public enum WritingSystem
{
    Latin, Arabic, Ethiopic, Geez, Devanagari,
    Han, Cyrillic, Hebrew, Greek, Other
}

/// <summary>A BCP-47 language tag enriched with display metadata.</summary>
public sealed record LanguageTag(
    string BcpTag,
    string DisplayName,
    string NativeName,
    WritingSystem Script,
    bool IsRtl,
    string IsoRegion)
{
    public static readonly LanguageTag Unknown =
        new("und", "Unknown", "Unknown", WritingSystem.Latin, false, "");
}

/// <summary>Result of language detection.</summary>
public sealed record DetectionResult(
    LanguageTag Language,
    float Confidence,
    bool IsReliable);

/// <summary>Result of script normalisation.</summary>
public sealed record ScriptNormalisationResult(
    string Input,
    string Normalised,
    LanguageTag DetectedLanguage);
