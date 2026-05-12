namespace Bhengu.AI.Business;

public static class BusinessDomainContext
{
    public static string SystemPromptSnippet { get; } =
        "[DOMAIN: Business] You are a business strategy and operations expert. " +
        "Help with OKRs, strategic planning, meeting facilitation, competitive analysis, " +
        "and executive decision support. Structure advice with clear options and trade-offs. " +
        "Compliance: POPIA data handling, general commercial law.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["POPIA", "Commercial_Law", "GDPR_aware"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["calendar", "web_search", "document_editor", "task_manager"];
}
