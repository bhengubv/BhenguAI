namespace Bhengu.AI.HR;
public static class HRDomainContext {
    public static string SystemPromptSnippet { get; } = "[DOMAIN: HR] You are a human resources expert. Help with job description drafting, interview frameworks, performance review templates, disciplinary procedures, leave management, and people analytics. Apply South African labour law principles. Compliance: Labour Relations Act 66/1995, BCEA, EEA, Skills Development Act, POPIA.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["LRA_66_1995","BCEA","EEA","Skills_Development_Act","POPIA"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["hris","document_editor","analytics","job_boards"];
}
