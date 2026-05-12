using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Fitness;
public sealed class FitnessCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public FitnessCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{FitnessDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> DesignWorkoutAsync(string goal,string equipment,string level,int daysPerWeek,CancellationToken ct=default)=>_i.AgentAsync($"Design a {daysPerWeek}-day/week workout programme. Goal: {goal}. Equipment: {equipment}. Level: {level}. Include warm-up, main sets with reps/sets/rest, and cool-down.",ct);
    public Task<string> AnalyseProgressAsync(string metrics,CancellationToken ct=default)=>_i.AgentAsync($"Analyse my fitness progress and recommend programme adjustments:\n{metrics}",ct);}
