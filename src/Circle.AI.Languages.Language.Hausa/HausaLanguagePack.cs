using Circle.AI.Languages.Language;

namespace Circle.AI.Languages.Language.Hausa;

/// <summary>
/// Hausa language pack for Circle AI.
/// Provides idiomatic expressions, cultural context, and prompt tuning
/// to make the AI reason naturally in Hausa (Hausa).
/// </summary>
public sealed class HausaLanguagePack : ILanguagePack
{
    public static readonly HausaLanguagePack Instance = new();

    public LanguagePackMetadata Metadata { get; } = new(
        BcpTag:          "ha",
        DisplayName:     "Hausa",
        NativeName:      "Hausa",
        PrimaryRegion:   "NG",
        SpokenInRegions: ["NG","NE","GH"],
        PackVersion:     new Version(1, 0));

    private static readonly Dictionary<string, string> Idioms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Add Hausa-specific idiomatic mappings here.
        // Example entries are placeholders — extend with real linguistic data.
        ["hello"]   = "Barka da safe",
        ["goodbye"] = "Sai anjima",
    };

    private static readonly Dictionary<string, CulturalNote[]> Notes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["greeting"] =
        [
            new CulturalNote(
                "greeting",
                "Use 'Barka da safe' in the morning. Show respect to elders.",
                ["Barka da safe", "Sai anjima"])
        ]
    };

    public string? GetIdiomaticExpression(string phrase)
        => Idioms.TryGetValue(phrase, out var v) ? v : null;

    public string AdaptSystemPrompt(string basePrompt)
        => $"You are a culturally aware AI assistant for Hausa speakers. " +
           $"Respond in Hausa (Hausa) unless instructed otherwise. " +
           $"Use natural, idiomatic expressions. Respect regional customs. " +
           $"\n\n{basePrompt}";

    public IReadOnlyList<CulturalNote> GetCulturalNotes(string context)
        => Notes.TryGetValue(context, out var n) ? n : [];

    public string GetGreeting(string timeOfDay)
        => timeOfDay.ToLowerInvariant() switch
        {
            "morning" or "am" => "Barka da safe",
            _                 => "Sai anjima"
        };

    public IReadOnlyDictionary<string, string> GetLocaleHints()
        => new Dictionary<string, string>
        {
            ["bcp_tag"]     = "ha",
            ["region"]      = "NG",
            ["rtl"]         = "false",
            ["date_format"] = "dd/MM/yyyy"
        };
}
