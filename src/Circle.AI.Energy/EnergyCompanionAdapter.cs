using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Energy;
public sealed class EnergyCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public EnergyCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{EnergyDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> SizeSolarSystemAsync(string monthlyConsumptionKwh,string location,bool gridTied,CancellationToken ct=default)=>_i.AgentAsync($"Size a solar PV system for {monthlyConsumptionKwh} kWh/month in {location}. Grid-tied: {gridTied}. Include panel capacity, inverter size, battery sizing (if off-grid), estimated generation, and payback period.",ct);
    public Task<string> AnalyseTariffAsync(string tariffSchedule,string consumptionProfile,CancellationToken ct=default)=>_i.AgentAsync($"Analyse this tariff schedule for cost optimisation opportunities:\n{tariffSchedule}\nConsumption profile:\n{consumptionProfile}\nRecommend demand management and TOU strategies.",ct);}
