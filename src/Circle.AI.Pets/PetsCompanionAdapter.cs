using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Pets;
public sealed class PetsCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public PetsCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{PetsDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> TriageSymptomAsync(string species,string breed,string symptom,CancellationToken ct=default)=>_i.AgentAsync($"Triage this pet health concern. Species: {species}. Breed: {breed}. Symptom: {symptom}. Indicate urgency level and whether immediate vet care is needed.",ct);
    public Task<string> CreateTrainingPlanAsync(string species,string age,string behaviour,CancellationToken ct=default)=>_i.AgentAsync($"Create a positive reinforcement training plan for a {age} {species} to address: {behaviour}. Include daily session structure, reward strategy, and realistic timeline.",ct);}
