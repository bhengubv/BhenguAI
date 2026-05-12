using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Hospitality;
public sealed class HospitalityCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public HospitalityCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{HospitalityDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> OptimiseRevParAsync(string occupancyData,string rateData,CancellationToken ct=default)=>_i.AgentAsync($"Analyse RevPAR performance and recommend rate and distribution strategies:\nOccupancy: {occupancyData}\nRates: {rateData}",ct);
    public Task<string> HandleGuestComplaintAsync(string complaint,string context,CancellationToken ct=default)=>_i.AgentAsync($"Draft a service recovery response for this guest complaint. Complaint: {complaint}. Context: {context}. Apply LAST (Listen, Apologise, Solve, Thank) framework.",ct);}
