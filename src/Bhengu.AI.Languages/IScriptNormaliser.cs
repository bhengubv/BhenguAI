namespace Bhengu.AI.Languages;

/// <summary>
/// Normalises text for a given writing system —
/// NFC/NFD, RTL markers, zero-width characters, etc.
/// </summary>
public interface IScriptNormaliser
{
    ScriptNormalisationResult Normalise(string text, LanguageTag? targetLanguage = null);
    string ToAsciiApproximation(string text);
    bool ContainsRtl(string text);
}
