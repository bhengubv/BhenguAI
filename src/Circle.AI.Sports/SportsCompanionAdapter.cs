using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Sports;
public sealed class SportsCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public SportsCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{SportsDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> DesignTrainingProgramAsync(string sport,string athleteProfile,string goal,int weeks,CancellationToken ct=default)=>_i.AgentAsync($"Design a {weeks}-week periodised training programme for {sport}. Athlete: {athleteProfile}. Goal: {goal}. Include weekly volume, intensity zones, key sessions, and recovery weeks.",ct);
    public Task<string> AnalysePerformanceAsync(string athleteData,CancellationToken ct=default)=>_i.AgentAsync($"Analyse this athlete performance data and identify strengths, weaknesses, and priority interventions:\n{athleteData}",ct);}
