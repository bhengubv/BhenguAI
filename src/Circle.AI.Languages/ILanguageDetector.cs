namespace Circle.AI.Languages;

/// <summary>Detects the BCP-47 language of a piece of text.</summary>
public interface ILanguageDetector
{
    /// <summary>
    /// Detects the most likely language.
    /// Returns <see cref="LanguageTag.Unknown"/> with Confidence=0 when detection fails.
    /// </summary>
    Task<DetectionResult> DetectAsync(string text, CancellationToken ct = default);

    /// <summary>Returns up to <paramref name="maxResults"/> candidates ranked by confidence.</summary>
    Task<IReadOnlyList<DetectionResult>> DetectMultipleAsync(
        string text, int maxResults = 3, CancellationToken ct = default);
}
