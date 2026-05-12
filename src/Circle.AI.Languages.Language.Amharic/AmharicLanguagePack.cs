using Circle.AI.Languages.Language;

namespace Circle.AI.Languages.Language.Amharic;

/// <summary>
/// Amharic language pack for Circle AI.
/// Provides idiomatic expressions, cultural context, and prompt tuning
/// to make the AI reason naturally in Amharic (አማርኛ).
/// </summary>
public sealed class AmharicLanguagePack : ILanguagePack
{
    public static readonly AmharicLanguagePack Instance = new();

    public LanguagePackMetadata Metadata { get; } = new(
        BcpTag:          "am",
        DisplayName:     "Amharic",
        NativeName:      "አማርኛ",
        PrimaryRegion:   "ET",
        SpokenInRegions: ["ET"],
        PackVersion:     new Version(1, 0));

    private static readonly Dictionary<string, string> Idioms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Add Amharic-specific idiomatic mappings here.
        // Example entries are placeholders — extend with real linguistic data.
        ["hello"]   = "ጤና ይስጥልኝ",
        ["goodbye"] = "መልካም ምሽት",
    };

    private static readonly Dictionary<string, CulturalNote[]> Notes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["greeting"] =
        [
            new CulturalNote(
                "greeting",
                "Use 'ጤና ይስጥልኝ' in the morning. Show respect to elders.",
                ["ጤና ይስጥልኝ", "መልካም ምሽት"])
        ]
    };

    public string? GetIdiomaticExpression(string phrase)
        => Idioms.TryGetValue(phrase, out var v) ? v : null;

    public string AdaptSystemPrompt(string basePrompt)
        => $"You are a culturally aware AI assistant for Amharic speakers. " +
           $"Respond in Amharic (አማርኛ) unless instructed otherwise. " +
           $"Use natural, idiomatic expressions. Respect regional customs. " +
           $"\n\n{basePrompt}";

    public IReadOnlyList<CulturalNote> GetCulturalNotes(string context)
        => Notes.TryGetValue(context, out var n) ? n : [];

    public string GetGreeting(string timeOfDay)
        => timeOfDay.ToLowerInvariant() switch
        {
            "morning" or "am" => "ጤና ይስጥልኝ",
            _                 => "መልካም ምሽት"
        };

    public IReadOnlyDictionary<string, string> GetLocaleHints()
        => new Dictionary<string, string>
        {
            ["bcp_tag"]     = "am",
            ["region"]      = "ET",
            ["rtl"]         = "false",
            ["date_format"] = "dd/MM/yyyy"
        };
}
