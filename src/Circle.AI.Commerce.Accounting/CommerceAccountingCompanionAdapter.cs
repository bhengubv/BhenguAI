using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.CommerceAccounting;
public sealed class CommerceAccountingCompanionAdapter : ICompanionSession
{
    private readonly ICompanionSession _inner;
    public CommerceAccountingCompanionAdapter(ICompanionSession inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
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
    private static string Enrich(string m) => $"{CommerceAccountingDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> ReconcileAsync(string bankStatement, string ledger, CancellationToken ct = default)
        => _inner.AgentAsync($"Reconcile these records and identify discrepancies.\n\nBank statement:\n{bankStatement}\n\nLedger:\n{ledger}", ct);
    public Task<string> PrepareVatReturnAsync(string period, decimal salesTotal, decimal purchasesTotal, CancellationToken ct = default)
        => _inner.AgentAsync($"Prepare a VAT201 return summary for {period}. Output VAT on sales {salesTotal:C}, Input VAT on purchases {purchasesTotal:C}. Show net payable/refundable and filing checklist.", ct);
    public Task<string> DraftManagementAccountsAsync(string financialData, string period, CancellationToken ct = default)
        => _inner.AgentAsync($"Draft management accounts for {period} from this data:\n{financialData}\nInclude P&L, balance sheet summary, cash flow, and key ratio analysis.", ct);
}
