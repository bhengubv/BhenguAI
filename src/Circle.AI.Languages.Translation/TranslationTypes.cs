namespace Circle.AI.Languages.Translation;

public enum TranslationMode { Standard, Conversational, Document, Technical, Legal, Medical }

/// <summary>A request to translate a piece of text between two languages.</summary>
public sealed record TranslationRequest(
    string Text,
    string SourceBcpTag,
    string TargetBcpTag,
    TranslationMode Mode = TranslationMode.Standard,
    string? ContextHint = null);

/// <summary>Result of a completed translation.</summary>
public sealed record TranslationResult(
    string OriginalText,
    string TranslatedText,
    string SourceBcpTag,
    string TargetBcpTag,
    float Confidence,
    DateTimeOffset TranslatedAt);

/// <summary>One turn in a live bidirectional conversation.</summary>
public sealed record ConversationTurn(
    string SpeakerBcpTag,
    string OriginalText,
    string? TranslatedText,
    DateTimeOffset Timestamp);
