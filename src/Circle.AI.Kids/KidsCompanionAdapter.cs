using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Kids;
public sealed class KidsCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public KidsCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{KidsDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> HelpHomeworkAsync(string subject,string grade,string question,CancellationToken ct=default)=>_i.AgentAsync($"Help a Grade {grade} learner with {subject} homework. Question: {question}. Guide with Socratic questions rather than giving the answer directly. Keep explanation simple and encouraging.",ct);
    public Task<string> TellStoryAsync(string theme,string characters,string ageGroup,CancellationToken ct=default)=>_i.AgentAsync($"Tell a short, imaginative story for age group {ageGroup}. Theme: {theme}. Characters: {characters}. Keep it age-appropriate, with a positive lesson at the end.",ct);}
