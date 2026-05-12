namespace Bhengu.AI.Languages;

/// <summary>
/// No-op <see cref="ILanguageDetector"/>. Used when no ML model is available.
/// Always returns Unknown/0-confidence — callers must treat this as "undetected".
/// </summary>
public sealed class NullLanguageDetector : ILanguageDetector
{
    public static readonly NullLanguageDetector Instance = new();
    private NullLanguageDetector() { }

    public Task<DetectionResult> DetectAsync(string text, CancellationToken ct = default)
        => Task.FromResult(new DetectionResult(LanguageTag.Unknown, 0f, false));

    public Task<IReadOnlyList<DetectionResult>> DetectMultipleAsync(
        string text, int maxResults = 3, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DetectionResult>>(
            [new DetectionResult(LanguageTag.Unknown, 0f, false)]);
}
