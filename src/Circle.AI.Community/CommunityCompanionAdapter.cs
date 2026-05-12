using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Community;
public sealed class CommunityCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public CommunityCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{CommunityDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> PlanCommunityEventAsync(string eventType,string size,string budget,CancellationToken ct=default)=>_i.AgentAsync($"Plan a community {eventType} for {size} people. Budget: {budget}. Include logistics checklist, volunteer roles, publicity plan, and risk management.",ct);
    public Task<string> WriteAdvocacyLetterAsync(string issue,string authority,CancellationToken ct=default)=>_i.AgentAsync($"Write a compelling advocacy letter about {issue} to {authority}. Include evidence, community impact, and specific ask.",ct);}
