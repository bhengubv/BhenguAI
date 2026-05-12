using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.SafetyChild;
public sealed class SafetyChildCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public SafetyChildCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{SafetyChildDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> SetDigitalRulesAsync(string childAge,CancellationToken ct=default)=>_i.AgentAsync($"Create age-appropriate digital safety rules for a {childAge}-year-old. Include screen time limits, app/platform permissions, online communication rules, and how to report concerning content.",ct);
    public Task<string> EducateOnlineRisksAsync(string childAge,CancellationToken ct=default)=>_i.AgentAsync($"Explain online safety concepts appropriate for a {childAge}-year-old. Cover: stranger danger online, personal information sharing, cyberbullying, and who to tell if something feels wrong. Use simple, non-scary language.",ct);}
