using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.PersonalHealth;
public sealed class PersonalHealthCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public PersonalHealthCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{PersonalHealthDomainContext.SystemPromptSnippet}\n\n{m}";
        public Task<string> PrepareAppointmentAsync(string symptoms,string medHistory,CancellationToken ct=default)=>_i.AgentAsync($"Help me prepare for a doctor appointment. Symptoms: {symptoms}. Relevant history: {medHistory}. Draft a concise symptom summary and list of questions to ask the doctor.",ct);
    public Task<string> ExplainHealthTermAsync(string term,CancellationToken ct=default)=>_i.AgentAsync($"Explain the medical term or concept in plain language: {term}. Make it accessible to a non-medical person.",ct);
}