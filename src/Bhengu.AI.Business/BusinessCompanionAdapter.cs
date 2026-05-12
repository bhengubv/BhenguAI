using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;

namespace Bhengu.AI.Business;

public sealed class BusinessCompanionAdapter : ICompanionSession
{
    private readonly ICompanionSession _inner;
    public BusinessCompanionAdapter(ICompanionSession inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    public string SessionId  => _inner.SessionId;
    public string IdentityId => _inner.IdentityId;
    public InterfaceKind Interface => _inner.Interface;
    public IReadOnlyList<CompanionTurn> History => _inner.History;
    public CompanionContext GetContext() => _inner.GetContext();
    public Task RefreshContextAsync(CancellationToken ct = default) => _inner.RefreshContextAsync(ct);
    public Task SignalFeedbackAsync(bool positive, string? note = null, CancellationToken ct = default) => _inner.SignalFeedbackAsync(positive, note, ct);
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
    public event EventHandler<CompanionProactiveEvent>? ProactiveMessageReady
    { add => _inner.ProactiveMessageReady += value; remove => _inner.ProactiveMessageReady -= value; }
    public Task<string> SendAsync(string message, CancellationToken ct = default) => _inner.SendAsync(Enrich(message), ct);
    public IAsyncEnumerable<string> StreamAsync(string message, [EnumeratorCancellation] CancellationToken ct = default) => _inner.StreamAsync(Enrich(message), ct);
    public Task<string> AgentAsync(string instruction, CancellationToken ct = default) => _inner.AgentAsync(Enrich(instruction), ct);
    private static string Enrich(string m) => $"{BusinessDomainContext.SystemPromptSnippet}\n\n{m}";

    public Task<string> DraftBusinessCaseAsync(string proposal, CancellationToken ct = default)
        => _inner.AgentAsync(
            $"Draft a professional business case for: {proposal}. " +
            "Include: executive summary, problem statement, solution options, recommended approach, cost/benefit, timeline, and risks.", ct);

    public Task<string> SummariseMeetingAsync(string transcript, CancellationToken ct = default)
        => _inner.AgentAsync(
            $"Summarise this meeting transcript. Extract decisions, action items with owners, blockers, and next-meeting agenda.\n\nTranscript:\n{transcript}", ct);

    public Task<string> GenerateOkrsAsync(string companyContext, string quarter, CancellationToken ct = default)
        => _inner.AgentAsync(
            $"Generate a set of OKRs for {quarter} based on the following company context:\n{companyContext}\n" +
            "Provide 3-5 Objectives each with 2-4 measurable Key Results.", ct);
}
