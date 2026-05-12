using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Civic;
public sealed class CivicCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public CivicCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{CivicDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> ExplainPermitProcessAsync(string permitType,string municipality,CancellationToken ct=default)=>_i.AgentAsync($"Explain the application process for a {permitType} permit in {municipality}. Include required documents, fees, timelines, and escalation steps.",ct);
    public Task<string> DraftObjectionAsync(string issue,string authority,CancellationToken ct=default)=>_i.AgentAsync($"Draft a formal objection letter regarding: {issue}. Addressed to: {authority}. Cite relevant rights under PAJA and request a formal response within the prescribed 90 days.",ct);}
