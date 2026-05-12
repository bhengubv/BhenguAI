using System.Runtime.CompilerServices;

namespace Bhengu.AI.Languages.Translation;

/// <summary>
/// On-device translation engine. No network call, no data leaving the device.
/// Translates meaning — not just words — using the on-device LLM.
/// </summary>
public interface ITranslationEngine
{
    Task<TranslationResult> TranslateAsync(
        TranslationRequest request, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamTranslateAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default);

    Task<bool> IsLanguagePairSupportedAsync(
        string sourceBcpTag, string targetBcpTag, CancellationToken ct = default);
}
