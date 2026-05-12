using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Media;
public sealed class MediaCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public MediaCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{MediaDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> CreateContentBriefAsync(string topic,string audience,string platform,CancellationToken ct=default)=>_i.AgentAsync($"Create a detailed content brief for {platform}: Topic: {topic}. Target audience: {audience}. Include angle, key messages, SEO keywords, call to action, and production notes.",ct);
    public Task<string> AnalyseAudienceDataAsync(string analyticsData,CancellationToken ct=default)=>_i.AgentAsync($"Analyse this audience/analytics data and provide actionable content strategy recommendations:\n{analyticsData}",ct);}
