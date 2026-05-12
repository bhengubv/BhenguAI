using Bhengu.AI.Languages.Language;

namespace Bhengu.AI.Languages.Language.Swahili;

/// <summary>
/// Swahili language pack for Circle AI.
/// Provides idiomatic expressions, cultural context, and prompt tuning
/// to make the AI reason naturally in Swahili (Kiswahili).
/// </summary>
public sealed class SwahiliLanguagePack : ILanguagePack
{
    public static readonly SwahiliLanguagePack Instance = new();

    public LanguagePackMetadata Metadata { get; } = new(
        BcpTag:          "sw",
        DisplayName:     "Swahili",
        NativeName:      "Kiswahili",
        PrimaryRegion:   "KE",
        SpokenInRegions: ["KE","TZ","UG"],
        PackVersion:     new Version(1, 0));

    private static readonly Dictionary<string, string> Idioms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Add Swahili-specific idiomatic mappings here.
        // Example entries are placeholders — extend with real linguistic data.
        ["hello"]   = "Habari",
        ["goodbye"] = "Usiku mwema",
    };

    private static readonly Dictionary<string, CulturalNote[]> Notes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["greeting"] =
        [
            new CulturalNote(
                "greeting",
                "Use 'Habari' in the morning. Show respect to elders.",
                ["Habari", "Usiku mwema"])
        ]
    };

    public string? GetIdiomaticExpression(string phrase)
        => Idioms.TryGetValue(phrase, out var v) ? v : null;

    public string AdaptSystemPrompt(string basePrompt)
        => $"You are a culturally aware AI assistant for Swahili speakers. " +
           $"Respond in Swahili (Kiswahili) unless instructed otherwise. " +
           $"Use natural, idiomatic expressions. Respect regional customs. " +
           $"\n\n{basePrompt}";

    public IReadOnlyList<CulturalNote> GetCulturalNotes(string context)
        => Notes.TryGetValue(context, out var n) ? n : [];

    public string GetGreeting(string timeOfDay)
        => timeOfDay.ToLowerInvariant() switch
        {
            "morning" or "am" => "Habari",
            _                 => "Usiku mwema"
        };

    public IReadOnlyDictionary<string, string> GetLocaleHints()
        => new Dictionary<string, string>
        {
            ["bcp_tag"]     = "sw",
            ["region"]      = "KE",
            ["rtl"]         = "false",
            ["date_format"] = "dd/MM/yyyy"
        };
}
