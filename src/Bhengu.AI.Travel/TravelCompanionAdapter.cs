using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Travel;
public sealed class TravelCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public TravelCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{TravelDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> PlanTripAsync(string destination,int nights,string travellers,string budget,CancellationToken ct=default)=>_i.AgentAsync($"Plan a {nights}-night trip to {destination} for {travellers}. Budget: {budget}. Include flights, accommodation tiers, daily activities, transport, and estimated total cost.",ct);
    public Task<string> CreatePackingListAsync(string destination,string duration,string activities,CancellationToken ct=default)=>_i.AgentAsync($"Create a packing list for {duration} in {destination}. Activities: {activities}. Organise by category (clothing, toiletries, documents, tech, emergency) and note carry-on vs checked restrictions.",ct);}
