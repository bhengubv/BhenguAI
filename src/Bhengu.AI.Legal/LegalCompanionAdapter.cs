using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Legal;
public sealed class LegalCompanionAdapter : ICompanionSession {
    private readonly ICompanionSession _i;
    public LegalCompanionAdapter(ICompanionSession i) => _i = i ?? throw new ArgumentNullException(nameof(i));
    public string SessionId  => _i.SessionId;
    public string IdentityId => _i.IdentityId;
    public InterfaceKind Interface => _i.Interface;
    public IReadOnlyList<CompanionTurn> History => _i.History;
    public CompanionContext GetContext() => _i.GetContext();
    public Task RefreshContextAsync(CancellationToken ct=default) => _i.RefreshContextAsync(ct);
    public Task SignalFeedbackAsync(bool p,string? n=null,CancellationToken ct=default) => _i.SignalFeedbackAsync(p,n,ct);
    public ValueTask DisposeAsync() => _i.DisposeAsync();
    public event EventHandler<CompanionProactiveEvent>? ProactiveMessageReady
    { add=>_i.ProactiveMessageReady+=value; remove=>_i.ProactiveMessageReady-=value; }
    public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
    public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
    public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
    private static string E(string m)=>$"{LegalDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> ReviewContractClausesAsync(string contractText, string focusArea, CancellationToken ct=default)
        =>_i.AgentAsync($"Review the following contract for {focusArea} issues. Identify risky clauses, missing protections, and suggest improvements:\n{contractText}",ct);
    public Task<string> DraftContractSummaryAsync(string contractText, CancellationToken ct=default)
        =>_i.AgentAsync($"Summarise this contract in plain language. Highlight key obligations, payment terms, IP ownership, termination, and dispute resolution:\n{contractText}",ct);
    public Task<string> GenerateComplianceChecklistAsync(string businessType, string jurisdiction, CancellationToken ct=default)
        =>_i.AgentAsync($"Generate a compliance checklist for a {businessType} operating in {jurisdiction}. Cover company registration, tax, labour, data protection, and sector-specific regulations.",ct);}
