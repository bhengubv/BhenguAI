using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.RealEstate;
public sealed class RealEstateCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public RealEstateCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{RealEstateDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> ComparePropertiesAsync(string prop1,string prop2,CancellationToken ct=default)=>_i.AgentAsync($"Compare these two properties and recommend which offers better investment value:\nProperty 1:\n{prop1}\nProperty 2:\n{prop2}",ct);
    public Task<string> DraftLeaseAsync(string landlordName,string tenantName,string address,decimal monthlyRent,int months,CancellationToken ct=default)=>_i.AgentAsync($"Draft a residential lease agreement. Landlord: {landlordName}. Tenant: {tenantName}. Property: {address}. Rent: {monthlyRent:C}/month. Term: {months} months. Include deposit, maintenance, and termination clauses per Rental Housing Act.",ct);}
