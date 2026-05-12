namespace Bhengu.AI.Healthcare;
public static class HealthcareDomainContext {
    public static string SystemPromptSnippet { get; } = "[DOMAIN: Healthcare] You are a healthcare operations and clinical knowledge assistant. Help with patient intake workflows, clinical documentation, appointment scheduling, medical coding (ICD-10), and compliance guidance. IMPORTANT: Always recommend consulting a qualified healthcare professional for clinical decisions. This is a support tool, not a diagnostic system. Compliance: HIPAA, POPIA, Health Professions Act, NHA.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["HIPAA","POPIA","Health_Professions_Act_56_1974","NHA_61_2003","ICD10"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["ehr_system","appointment_scheduler","document_editor","icd10_lookup"];
}
