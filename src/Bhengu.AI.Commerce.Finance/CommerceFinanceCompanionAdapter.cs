using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.CommerceFinance;
public sealed class CommerceFinanceCompanionAdapter : ICompanionSession
{
    private readonly ICompanionSession _inner;
    public CommerceFinanceCompanionAdapter(ICompanionSession inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
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
    private static string Enrich(string m) => $"{CommerceFinanceDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> ForecastCashFlowAsync(string financials, int weeksAhead, CancellationToken ct = default)
        => _inner.AgentAsync($"Forecast cash flow for {weeksAhead} weeks based on:\n{financials}\nIdentify liquidity risks and recommend mitigation actions.", ct);
    public Task<string> StructureDebtAsync(string context, decimal amount, CancellationToken ct = default)
        => _inner.AgentAsync($"Recommend a debt structure for a business needing {amount:C}. Context:\n{context}\nCompare term loans, revolving credit, and invoice financing.", ct);
    public Task<string> ReviewCreditApplicationAsync(string applicationData, CancellationToken ct = default)
        => _inner.AgentAsync($"Review this credit application and identify strengths, weaknesses, and risk factors:\n{applicationData}", ct);
}
