using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Logistics;
public sealed class LogisticsCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public LogisticsCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{LogisticsDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> OptimiseRouteAsync(string origin,string destinations,string constraints,CancellationToken ct=default)=>_i.AgentAsync($"Optimise delivery routes from {origin} to: {destinations}. Constraints: {constraints}. Minimise total distance and time while respecting load limits and delivery windows.",ct);
    public Task<string> PrepareCustomsDocAsync(string shipmentDetails,string incoterm,CancellationToken ct=default)=>_i.AgentAsync($"Prepare a customs documentation checklist for: {shipmentDetails}. Incoterm: {incoterm}. Include required forms, HS codes guidance, and SARS requirements.",ct);}
