using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Home;
public sealed class HomeCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public HomeCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{HomeDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> PlanMaintenanceAsync(string homeType,CancellationToken ct=default)=>_i.AgentAsync($"Create an annual home maintenance schedule for a {homeType}. Include monthly, quarterly, bi-annual, and annual tasks with estimated time and cost per task.",ct);
    public Task<string> EstimateRenovationAsync(string scope,string area,CancellationToken ct=default)=>_i.AgentAsync($"Estimate the cost and timeline for this renovation: {scope} in {area}. Break down labour, materials, and contingency. Identify potential hidden costs.",ct);}
