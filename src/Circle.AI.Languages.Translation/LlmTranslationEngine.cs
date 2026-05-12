using System.Runtime.CompilerServices;
using Circle.AI.Inference;

namespace Circle.AI.Languages.Translation;

/// <summary>
/// <see cref="ITranslationEngine"/> backed by the on-device LLM via
/// <see cref="IChatGenerator"/>. All processing is on-device — no API calls,
/// no data leaving the device.
/// </summary>
public sealed class LlmTranslationEngine : ILiveTranslator
{
    private readonly IChatGenerator _generator;

    public LlmTranslationEngine(IChatGenerator generator)
        => _generator = generator ?? throw new ArgumentNullException(nameof(generator));

    /// <inheritdoc/>
    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request, CancellationToken ct = default)
    {
        var messages = new[] { new ChatMessage("user", BuildPrompt(request)) };
        var translated = await _generator
            .GenerateAsync(messages, ct: ct)
            .ConfigureAwait(false);

        return new TranslationResult(
            request.Text,
            translated.Trim(),
            request.SourceBcpTag,
            request.TargetBcpTag,
            0.9f,
            DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamTranslateAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new[] { new ChatMessage("user", BuildPrompt(request)) };
        await foreach (var token in _generator.StreamAsync(messages, ct: ct)
                                              .ConfigureAwait(false))
            yield return token;
    }

    /// <inheritdoc/>
    public Task<bool> IsLanguagePairSupportedAsync(
        string sourceBcpTag, string targetBcpTag, CancellationToken ct = default)
        => Task.FromResult(true); // On-device LLM handles any pair it was trained on.

    /// <inheritdoc/>
    public async IAsyncEnumerable<ConversationTurn> StreamConversationAsync(
        IAsyncEnumerable<ConversationTurn> inputStream,
        string partyABcpTag,
        string partyBBcpTag,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var turn in inputStream.WithCancellation(ct).ConfigureAwait(false))
        {
            var targetTag = turn.SpeakerBcpTag == partyABcpTag ? partyBBcpTag : partyABcpTag;

            var req = new TranslationRequest(
                turn.OriginalText, turn.SpeakerBcpTag, targetTag,
                TranslationMode.Conversational);

            var result = await TranslateAsync(req, ct).ConfigureAwait(false);

            yield return turn with { TranslatedText = result.TranslatedText };
        }
    }

    private static string BuildPrompt(TranslationRequest r) =>
        $"Translate the following text from {r.SourceBcpTag} to {r.TargetBcpTag}. " +
        $"Mode: {r.Mode}. Preserve meaning and cultural context, not just literal words. " +
        (r.ContextHint is not null ? $"Context: {r.ContextHint}. " : string.Empty) +
        $"Return only the translation with no explanation.\n\n{r.Text}";
}
