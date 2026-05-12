namespace Bhengu.AI.Languages;

/// <summary>Static registry of every language Circle AI ships support for.</summary>
public static class KnownLanguages
{
    // ── Africa ────────────────────────────────────────────────────────────────
    public static readonly LanguageTag IsiZulu     = new("zu", "isiZulu",     "isiZulu",     WritingSystem.Latin,    false, "ZA");
    public static readonly LanguageTag Sesotho     = new("st", "Sesotho",     "Sesotho",     WritingSystem.Latin,    false, "ZA");
    public static readonly LanguageTag Afrikaans   = new("af", "Afrikaans",   "Afrikaans",   WritingSystem.Latin,    false, "ZA");
    public static readonly LanguageTag Swahili     = new("sw", "Swahili",     "Kiswahili",   WritingSystem.Latin,    false, "KE");
    public static readonly LanguageTag Hausa       = new("ha", "Hausa",       "Hausa",       WritingSystem.Latin,    false, "NG");
    public static readonly LanguageTag Amharic     = new("am", "Amharic",     "አማርኛ",        WritingSystem.Ethiopic, false, "ET");
    public static readonly LanguageTag Yoruba      = new("yo", "Yoruba",      "Yorùbá",      WritingSystem.Latin,    false, "NG");
    public static readonly LanguageTag Igbo        = new("ig", "Igbo",        "Igbo",        WritingSystem.Latin,    false, "NG");
    public static readonly LanguageTag Xhosa       = new("xh", "isiXhosa",    "isiXhosa",    WritingSystem.Latin,    false, "ZA");
    public static readonly LanguageTag Sepedi      = new("nso","Sepedi",      "Sepedi",      WritingSystem.Latin,    false, "ZA");
    public static readonly LanguageTag Setswana    = new("tn", "Setswana",    "Setswana",    WritingSystem.Latin,    false, "ZA");
    public static readonly LanguageTag Somali      = new("so", "Somali",      "Soomaali",    WritingSystem.Latin,    false, "SO");
    public static readonly LanguageTag Oromo       = new("om", "Oromo",       "Afaan Oromoo",WritingSystem.Latin,    false, "ET");

    // ── Middle East & North Africa ────────────────────────────────────────────
    public static readonly LanguageTag Arabic      = new("ar", "Arabic",      "العربية",     WritingSystem.Arabic,   true,  "SA");

    // ── Europe & Americas ─────────────────────────────────────────────────────
    public static readonly LanguageTag English     = new("en", "English",     "English",     WritingSystem.Latin,    false, "GB");
    public static readonly LanguageTag Portuguese  = new("pt", "Portuguese",  "Português",   WritingSystem.Latin,    false, "PT");
    public static readonly LanguageTag French      = new("fr", "French",      "Français",    WritingSystem.Latin,    false, "FR");
    public static readonly LanguageTag Spanish     = new("es", "Spanish",     "Español",     WritingSystem.Latin,    false, "ES");

    // ── Asia ──────────────────────────────────────────────────────────────────
    public static readonly LanguageTag Mandarin    = new("zh", "Mandarin",    "中文",          WritingSystem.Han,      false, "CN");
    public static readonly LanguageTag Hindi       = new("hi", "Hindi",       "हिन्दी",        WritingSystem.Devanagari, false, "IN");

    /// <summary>All languages shipped with Circle AI.</summary>
    public static readonly IReadOnlyList<LanguageTag> All =
    [
        IsiZulu, Sesotho, Afrikaans, Swahili, Hausa, Amharic,
        Yoruba, Igbo, Xhosa, Sepedi, Setswana, Somali, Oromo,
        Arabic,
        English, Portuguese, French, Spanish,
        Mandarin, Hindi
    ];
}
