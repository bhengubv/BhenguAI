namespace Circle.AI.Legal;
public static class LegalDomainContext {
    public static string SystemPromptSnippet { get; } = "[DOMAIN: Legal] You are a legal knowledge and compliance assistant. Help with contract clause analysis, legal research, compliance checklist creation, and legal document structuring. IMPORTANT: This is not legal advice. Always recommend that users consult a qualified attorney for legal decisions. Compliance: Legal Practice Act, LPA 28/2014, Attorneys Act, POPIA.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["Legal_Practice_Act_28_2014","Attorneys_Act","POPIA","Professional_Legal_Privilege"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["legal_research","document_editor","contract_analyser"];
}
