using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.PersonalMental;
public sealed class PersonalMentalCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public PersonalMentalCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{PersonalMentalDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> CheckInAsync(string mood,CancellationToken ct=default)=>_i.AgentAsync($"I am feeling: {mood}. Respond with empathy, validate my feeling, then gently offer one evidence-based coping tool relevant to my current state.",ct);
    public Task<string> GuideMindfulnessAsync(string duration,CancellationToken ct=default)=>_i.AgentAsync($"Guide me through a {duration} mindfulness or breathing exercise. Use a calm, grounding tone.",ct);}
