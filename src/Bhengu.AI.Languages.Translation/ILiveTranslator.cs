using System.Runtime.CompilerServices;

namespace Bhengu.AI.Languages.Translation;

/// <summary>
/// Bidirectional live conversation translator.
/// Party A speaks <paramref name="partyABcpTag"/>;
/// party B speaks <paramref name="partyBBcpTag"/>.
/// Each turn is translated in real-time so both parties hear each other.
/// Runs entirely on-device. No API call. No data leaves the device.
/// Example: you speak Zulu, they hear English — and vice versa.
/// </summary>
public interface ILiveTranslator : ITranslationEngine
{
    IAsyncEnumerable<ConversationTurn> StreamConversationAsync(
        IAsyncEnumerable<ConversationTurn> inputStream,
        string partyABcpTag,
        string partyBBcpTag,
        [EnumeratorCancellation] CancellationToken ct = default);
}
