using Bhengu.AI.Languages.Language;

namespace Bhengu.AI.Languages.Language.isiZulu;

/// <summary>
/// isiZulu language pack for Circle AI.
/// Provides idiomatic expressions, cultural context, and prompt tuning
/// to make the AI reason naturally in isiZulu (isiZulu).
/// </summary>
public sealed class isiZuluLanguagePack : ILanguagePack
{
    public static readonly isiZuluLanguagePack Instance = new();

    public LanguagePackMetadata Metadata { get; } = new(
        BcpTag:          "zu",
        DisplayName:     "isiZulu",
        NativeName:      "isiZulu",
        PrimaryRegion:   "ZA",
        SpokenInRegions: ["ZA"],
        PackVersion:     new Version(1, 0));

    private static readonly Dictionary<string, string> Idioms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Add isiZulu-specific idiomatic mappings here.
        // Example entries are placeholders — extend with real linguistic data.
        ["hello"]   = "Sawubona",
        ["goodbye"] = "Lala kahle",
    };

    private static readonly Dictionary<string, CulturalNote[]> Notes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["greeting"] =
        [
            new CulturalNote(
                "greeting",
                "Use 'Sawubona' in the morning. Show respect to elders.",
                ["Sawubona", "Lala kahle"])
        ]
    };

    public string? GetIdiomaticExpression(string phrase)
        => Idioms.TryGetValue(phrase, out var v) ? v : null;

    public string AdaptSystemPrompt(string basePrompt)
        => $"You are a culturally aware AI assistant for isiZulu speakers. " +
           $"Respond in isiZulu (isiZulu) unless instructed otherwise. " +
           $"Use natural, idiomatic expressions. Respect regional customs. " +
           $"\n\n{basePrompt}";

    public IReadOnlyList<CulturalNote> GetCulturalNotes(string context)
        => Notes.TryGetValue(context, out var n) ? n : [];

    public string GetGreeting(string timeOfDay)
        => timeOfDay.ToLowerInvariant() switch
        {
            "morning" or "am" => "Sawubona",
            _                 => "Lala kahle"
        };

    public IReadOnlyDictionary<string, string> GetLocaleHints()
        => new Dictionary<string, string>
        {
            ["bcp_tag"]     = "zu",
            ["region"]      = "ZA",
            ["rtl"]         = "false",
            ["date_format"] = "dd/MM/yyyy"
        };
}
