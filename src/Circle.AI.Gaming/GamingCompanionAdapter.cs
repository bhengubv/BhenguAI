using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Gaming;
public sealed class GamingCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public GamingCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{GamingDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> BuildStrategyAsync(string game,string goal,string currentSetup,CancellationToken ct=default)=>_i.AgentAsync($"Build a competitive strategy for {game}. Goal: {goal}. Current setup: {currentSetup}. Include build recommendations, macro strategy, and key counters.",ct);
    public Task<string> WriteGameReviewAsync(string game,string playtime,string verdict,CancellationToken ct=default)=>_i.AgentAsync($"Write a structured game review for {game}. Playtime: {playtime}. My verdict: {verdict}. Include: graphics, gameplay, story, performance, value, and a score out of 10.",ct);}
