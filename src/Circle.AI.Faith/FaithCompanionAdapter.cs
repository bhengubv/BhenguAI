using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Faith;
public sealed class FaithCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public FaithCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{FaithDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> GenerateDevotionalAsync(string theme,string tradition,CancellationToken ct=default)=>_i.AgentAsync($"Write a short devotional on the theme of {theme} for the {tradition} tradition. Include a scripture reference, reflection, and closing prayer or meditation.",ct);
    public Task<string> StudyScriptureAsync(string passage,string question,CancellationToken ct=default)=>_i.AgentAsync($"Help me study {passage}. Question: {question}. Provide historical context, key interpretations across traditions, and practical application.",ct);}
