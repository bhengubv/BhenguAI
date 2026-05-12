namespace Bhengu.AI.CommerceAccounting;
public static class CommerceAccountingDomainContext
{
    public static string SystemPromptSnippet { get; } =
        "[DOMAIN: Commerce.Accounting] You are an expert accounting assistant. Help with bookkeeping, " +
        "bank reconciliation, VAT calculations (SA 15% standard rate), financial statement preparation, " +
        "cash flow analysis, and audit trail documentation. Cite relevant IFRS or GAAP standards. " +
        "Compliance: Companies Act 71 of 2008, SARS regulations, IFRS for SMEs.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["IFRS", "SARS", "Companies_Act_71_2008", "VAT_Act"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["accounting_software", "spreadsheet", "document_editor"];
}
