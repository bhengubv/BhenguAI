using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Agriculture;
public sealed class AgricultureCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public AgricultureCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{AgricultureDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> DiagnosePestAsync(string cropType,string symptoms,CancellationToken ct=default)=>_i.AgentAsync($"Diagnose this crop problem and recommend treatment. Crop: {cropType}. Symptoms: {symptoms}. Include integrated pest management (IPM) options and registered chemical controls.",ct);
    public Task<string> PlanCropRotationAsync(string farmContext,int seasons,CancellationToken ct=default)=>_i.AgentAsync($"Design a {seasons}-season crop rotation plan for: {farmContext}. Optimise soil health, disease break cycles, and profitability.",ct);}
