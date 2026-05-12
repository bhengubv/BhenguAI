using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.HR;
public sealed class HRCompanionAdapter : ICompanionSession {
    private readonly ICompanionSession _i;
    public HRCompanionAdapter(ICompanionSession i) => _i = i ?? throw new ArgumentNullException(nameof(i));
    public string SessionId  => _i.SessionId;
    public string IdentityId => _i.IdentityId;
    public InterfaceKind Interface => _i.Interface;
    public IReadOnlyList<CompanionTurn> History => _i.History;
    public CompanionContext GetContext() => _i.GetContext();
    public Task RefreshContextAsync(CancellationToken ct=default) => _i.RefreshContextAsync(ct);
    public Task SignalFeedbackAsync(bool p,string? n=null,CancellationToken ct=default) => _i.SignalFeedbackAsync(p,n,ct);
    public ValueTask DisposeAsync() => _i.DisposeAsync();
    public event EventHandler<CompanionProactiveEvent>? ProactiveMessageReady
    { add=>_i.ProactiveMessageReady+=value; remove=>_i.ProactiveMessageReady-=value; }
    public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
    public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
    public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
    private static string E(string m)=>$"{HRDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> DraftJobDescriptionAsync(string role, string requirements, CancellationToken ct=default)
        =>_i.AgentAsync($"Draft a compelling, legally compliant job description for: {role}. Requirements: {requirements}. Include purpose, responsibilities, qualifications, and EEA statement.",ct);
    public Task<string> GeneratePerformanceReviewAsync(string employeeName, string role, string achievements, CancellationToken ct=default)
        =>_i.AgentAsync($"Generate a structured performance review for {employeeName} ({role}). Achievements: {achievements}. Include ratings, development areas, and SMART goals.",ct);
    public Task<string> AdviseOnDisciplinaryAsync(string misconduct, string employeeHistory, CancellationToken ct=default)
        =>_i.AgentAsync($"Advise on disciplinary action for: {misconduct}. Employee history: {employeeHistory}. Apply LRA progressive discipline principles and recommend appropriate sanction.",ct);}
