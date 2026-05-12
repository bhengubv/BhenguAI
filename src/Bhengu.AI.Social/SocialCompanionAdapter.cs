using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Social;
public sealed class SocialCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public SocialCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{SocialDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> WritePostAsync(string platform,string message,string tone,CancellationToken ct=default)=>_i.AgentAsync($"Write an engaging {platform} post. Core message: {message}. Tone: {tone}. Include relevant hashtags, call to action, and emoji where appropriate for the platform.",ct);
    public Task<string> PlanContentCalendarAsync(string brand,string month,string goals,CancellationToken ct=default)=>_i.AgentAsync($"Plan a social media content calendar for {brand} in {month}. Goals: {goals}. Include content mix, posting frequency, themes, and key dates.",ct);}
