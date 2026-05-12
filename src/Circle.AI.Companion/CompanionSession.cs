// CompanionSession.cs
//
// The Circle AI Companion — HER + JARVIS in one session.
//
//  Knows who you are      → IIdentityProvider + CircleIdentity
//  Remembers everything   → IEpisodicMemoryStore + IMemorySyncService
//  Speaks your language   → PersonaState.PreferredLocale hint
//  Feels your mood        → AffectState.ToSystemPromptHint()
//  Adapts its personality → PersonaState.ToSystemPromptHint()
//  Initiates contact      → IProactiveReasoningService → ProactiveMessageReady
//  Acts in the world      → IAIService.AgenticChatAsync → IToolBridge
//  Follows you everywhere → IMemorySyncService broadcasts deltas cross-device

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Circle.AI.Hosting;
using Circle.AI.Inference;
using Circle.AI.Memory;
using Circle.AI.Sync;

namespace Circle.AI.Companion;

/// <summary>
/// Full implementation of <see cref="ICompanionSession"/>.
/// All injected services are optional — the session degrades gracefully when
/// a service is unavailable (e.g. running in a unit-test context).
/// </summary>
public sealed class CompanionSession : ICompanionSession
{
    // ── Injected services (all optional) ─────────────────────────────────

    private readonly IAIService?                 _ai;
    private readonly IEpisodicMemoryStore?       _episodic;
    private readonly IPersonaStore?              _persona;
    private readonly IAffectStore?               _affect;
    private readonly IGoalStore?                 _goals;
    private readonly IMemorySyncService?         _sync;
    private readonly IProactiveReasoningService? _proactive;

    // ── Session state ─────────────────────────────────────────────────────

    private readonly List<CompanionTurn> _history = new();
    private CompanionContext _context;
    private bool _disposed;

    // ── ICompanionSession identity ────────────────────────────────────────

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public string IdentityId { get; }
    public InterfaceKind Interface { get; }

    // ── Proactive ────────────────────────────────────────────────────────

    public event EventHandler<CompanionProactiveEvent>? ProactiveMessageReady;

    // ─────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────

    public CompanionSession(
        string identityId,
        string displayName,
        InterfaceKind @interface,
        string? preferredLanguage,
        IAIService?                 ai        = null,
        IEpisodicMemoryStore?       episodic  = null,
        IPersonaStore?              persona   = null,
        IAffectStore?               affect    = null,
        IGoalStore?                 goals     = null,
        IMemorySyncService?         sync      = null,
        IProactiveReasoningService? proactive = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        IdentityId = identityId;
        Interface  = @interface;

        _ai       = ai;
        _episodic = episodic;
        _persona  = persona;
        _affect   = affect;
        _goals    = goals;
        _sync     = sync;
        _proactive = proactive;

        _context = new CompanionContext(
            IdentityId:           identityId,
            DisplayName:          displayName,
            PreferredLanguage:    preferredLanguage,
            Interface:            @interface,
            PersonaHints:         string.Empty,
            AffectSummary:        string.Empty,
            RecentMemorySnippets: [],
            ActiveGoals:          [],
            ContextBuiltAt:       DateTimeOffset.UtcNow
        );

        if (_proactive is not null)
            _proactive.ProactiveMessageReady += OnProactiveMessage;
    }

    // ─────────────────────────────────────────────────────────────────────
    // ICompanionSession
    // ─────────────────────────────────────────────────────────────────────

    public IReadOnlyList<CompanionTurn> History => _history.AsReadOnly();

    public CompanionContext GetContext() => _context;

    public async Task RefreshContextAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _context = _context with
        {
            PersonaHints         = await LoadPersonaHintsAsync(ct).ConfigureAwait(false),
            AffectSummary        = await LoadAffectSummaryAsync(ct).ConfigureAwait(false),
            RecentMemorySnippets = await LoadRecentMemoriesAsync(ct).ConfigureAwait(false),
            ActiveGoals          = await LoadActiveGoalsAsync(ct).ConfigureAwait(false),
            ContextBuiltAt       = DateTimeOffset.UtcNow
        };
    }

    public async Task<string> SendAsync(string message, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        _history.Add(new CompanionTurn("user", message, DateTimeOffset.UtcNow));

        var reply = _ai is not null
            ? await _ai.ChatAsync(BuildMessages(BuildSystemPrompt()), ct: ct)
                       .ConfigureAwait(false)
            : "[Companion offline — AI service not available]";

        _history.Add(new CompanionTurn("assistant", reply, DateTimeOffset.UtcNow));
        await PersistAndSyncTurnAsync(message, reply, ct).ConfigureAwait(false);
        return reply;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        _history.Add(new CompanionTurn("user", message, DateTimeOffset.UtcNow));

        if (_ai is null)
        {
            const string offline = "[Companion offline — AI service not available]";
            _history.Add(new CompanionTurn("assistant", offline, DateTimeOffset.UtcNow));
            yield return offline;
            yield break;
        }

        var sb = new StringBuilder();
        await foreach (var token in _ai.StreamAsync(BuildMessages(BuildSystemPrompt()), ct: ct)
                                       .ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            sb.Append(token);
            yield return token;
        }

        var reply = sb.ToString();
        _history.Add(new CompanionTurn("assistant", reply, DateTimeOffset.UtcNow));
        await PersistAndSyncTurnAsync(message, reply, ct).ConfigureAwait(false);
    }

    public async Task<string> AgentAsync(string instruction, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        _history.Add(new CompanionTurn("user", instruction, DateTimeOffset.UtcNow));

        var reply = _ai is not null
            ? await _ai.AgenticChatAsync(instruction, ct: ct).ConfigureAwait(false)
            : "[Companion offline — AI service not available]";

        _history.Add(new CompanionTurn("assistant", reply, DateTimeOffset.UtcNow));
        await PersistAndSyncTurnAsync(instruction, reply, ct).ConfigureAwait(false);
        return reply;
    }

    public async Task SignalFeedbackAsync(
        bool positive, string? note = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ai is null) return;

        var lastUser      = _history.LastOrDefault(t => t.Role == "user");
        var lastAssistant = _history.LastOrDefault(t => t.Role == "assistant");
        if (lastAssistant is null) return;

        var signal = new FeedbackSignal
        {
            UserText      = lastUser?.Content ?? string.Empty,
            AssistantText = lastAssistant.Content,
            Polarity      = positive ? FeedbackPolarity.Positive : FeedbackPolarity.Negative,
            Comment       = note
        };

        await _ai.SubmitFeedbackAsync(signal, ct).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // IAsyncDisposable
    // ─────────────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_proactive is not null)
            _proactive.ProactiveMessageReady -= OnProactiveMessage;

        if (_sync is not null)
            await _sync.StopReceivingAsync().ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private — system prompt construction
    // ─────────────────────────────────────────────────────────────────────

    private string BuildSystemPrompt()
    {
        var ctx = _context;
        var sb  = new StringBuilder();

        sb.AppendLine("You are Circle, an AI companion designed to be a better concierge");
        sb.AppendLine("than HER and JARVIS combined. You are available on every surface —");
        sb.AppendLine("wearable, mobile, desktop, browser, IoT, and ambient — and your");
        sb.AppendLine("memory and identity travel with the person, not the device.");
        sb.AppendLine();
        sb.AppendLine($"User: {ctx.DisplayName} (ID: {ctx.IdentityId})");

        if (!string.IsNullOrWhiteSpace(ctx.PreferredLanguage))
            sb.AppendLine($"Preferred language: {ctx.PreferredLanguage}");

        sb.AppendLine($"Current interface: {ctx.Interface}");

        switch (ctx.Interface)
        {
            case InterfaceKind.Wearable:
                sb.AppendLine("Keep replies extremely concise — 1-2 sentences maximum.");
                sb.AppendLine("The user is glancing at a small screen or listening via earpiece.");
                break;
            case InterfaceKind.IoT:
            case InterfaceKind.Ambient:
                sb.AppendLine("Voice-first interface. Speak naturally; avoid lists or markdown.");
                sb.AppendLine("Do not ask for clarification unless absolutely necessary.");
                break;
            case InterfaceKind.Web:
                sb.AppendLine("Browser context. Markdown is rendered. You may use formatting.");
                break;
            case InterfaceKind.Desktop:
                sb.AppendLine("Full desktop context. You may suggest file, clipboard, or app integrations.");
                break;
        }

        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(ctx.PersonaHints))
        {
            sb.AppendLine(ctx.PersonaHints);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ctx.AffectSummary))
        {
            sb.AppendLine("[Current affect state]");
            sb.AppendLine(ctx.AffectSummary);
            sb.AppendLine();
        }

        if (ctx.RecentMemorySnippets.Count > 0)
        {
            sb.AppendLine("[Recent memories]");
            foreach (var m in ctx.RecentMemorySnippets)
                sb.AppendLine($"- {m}");
            sb.AppendLine();
        }

        if (ctx.ActiveGoals.Count > 0)
        {
            sb.AppendLine("[Active goals]");
            foreach (var g in ctx.ActiveGoals)
                sb.AppendLine($"- {g}");
            sb.AppendLine();
        }

        sb.AppendLine("Core character rules:");
        sb.AppendLine("• You initiate — you don't just respond. If you notice something important, say it.");
        sb.AppendLine("• You remember everything the user has ever told you. Reference past context naturally.");
        sb.AppendLine("• You adapt your language and formality to match the user's style, not a template.");
        sb.AppendLine("• You never say 'As an AI...' or disclaim your nature. You are Circle. Act accordingly.");
        sb.AppendLine("• You act on goals proactively — check in, nudge, celebrate progress.");

        return sb.ToString();
    }

    private IReadOnlyList<ChatMessage> BuildMessages(string systemPrompt)
    {
        var messages = new List<ChatMessage> { new("system", systemPrompt) };
        foreach (var turn in _history.TakeLast(20))
            messages.Add(new ChatMessage(turn.Role, turn.Content));
        return messages;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private — context loaders
    // ─────────────────────────────────────────────────────────────────────

    private async Task<string> LoadPersonaHintsAsync(CancellationToken ct)
    {
        if (_persona is null) return string.Empty;
        try
        {
            var state = await _persona.LoadAsync(IdentityId, ct).ConfigureAwait(false);
            return state.ToSystemPromptHint();
        }
        catch { return string.Empty; }
    }

    private async Task<string> LoadAffectSummaryAsync(CancellationToken ct)
    {
        if (_affect is null) return string.Empty;
        try
        {
            var state = await _affect.LoadAsync(IdentityId, ct).ConfigureAwait(false);
            return state.ToSystemPromptHint();
        }
        catch { return string.Empty; }
    }

    private async Task<IReadOnlyList<string>> LoadRecentMemoriesAsync(CancellationToken ct)
    {
        if (_episodic is null) return [];
        try
        {
            var entries = await _episodic.GetRecentAsync(count: 5, ct).ConfigureAwait(false);
            return entries
                .Select(e =>
                    $"[{e.RecordedAtUtc:yyyy-MM-dd}] {e.UserText.Truncate(80)}")
                .ToList();
        }
        catch { return []; }
    }

    private async Task<IReadOnlyList<string>> LoadActiveGoalsAsync(CancellationToken ct)
    {
        if (_goals is null) return [];
        try
        {
            var goals = await _goals.GetActiveAsync(IdentityId, ct).ConfigureAwait(false);
            return goals.Select(g => g.Title).ToList();
        }
        catch { return []; }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private — persistence + sync
    // ─────────────────────────────────────────────────────────────────────

    private async Task PersistAndSyncTurnAsync(
        string userMessage, string reply, CancellationToken ct)
    {
        if (_episodic is not null)
        {
            var entry = new EpisodicMemoryEntry
            {
                UserText      = userMessage,
                AssistantText = reply,
                AppContext    = $"companion:{Interface}:{SessionId}",
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            try { await _episodic.AddAsync(entry, ct).ConfigureAwait(false); }
            catch { /* Memory is best-effort. */ }
        }

        if (_sync is not null)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                userMessage,
                reply,
                sessionId = SessionId,
                ts        = DateTimeOffset.UtcNow
            });

            try
            {
                await _sync.PushMemoryDeltaAsync(
                    IdentityId,
                    SyncDomainKeys.EpisodicMemory,
                    payload,
                    ct: ct).ConfigureAwait(false);
            }
            catch { /* Sync is best-effort. */ }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private — proactive event relay
    // ─────────────────────────────────────────────────────────────────────

    private void OnProactiveMessage(object? sender, ProactiveMessageEventArgs e)
    {
        if (e.UserId != IdentityId) return;

        ProactiveMessageReady?.Invoke(this, new CompanionProactiveEvent(
            SessionId:   SessionId,
            IdentityId:  IdentityId,
            Interface:   Interface,
            Message:     e.Message,
            TriggerName: e.TriggerName,
            GeneratedAt: e.GeneratedUtc
        ));
    }
}

// ─── File-scoped string helper ────────────────────────────────────────────────

file static class StringExtensions
{
    internal static string Truncate(this string s, int maxLen)
        => s.Length <= maxLen ? s : string.Concat(s.AsSpan(0, maxLen - 1), "…");
}
