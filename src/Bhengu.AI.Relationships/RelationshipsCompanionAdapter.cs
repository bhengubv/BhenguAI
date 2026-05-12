using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Relationships;
public sealed class RelationshipsCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public RelationshipsCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{RelationshipsDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> GuideConflictResolutionAsync(string situation,CancellationToken ct=default)=>_i.AgentAsync($"Guide me through resolving this conflict using Non-Violent Communication (NVC):\n{situation}\nHelp me identify observations, feelings, needs, and requests.",ct);
    public Task<string> DraftDifficultConversationAsync(string topic,string relationship,CancellationToken ct=default)=>_i.AgentAsync($"Help me prepare for a difficult conversation about {topic} with my {relationship}. Draft key points using assertive but empathetic language.",ct);}
