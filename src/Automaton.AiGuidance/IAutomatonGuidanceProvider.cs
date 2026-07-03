namespace Automaton.AiGuidance;

public interface IAutomatonGuidanceProvider
{
    string GetLlms();
    string GetLlmsFull();
    string GetReference(string relativePath);
    string GetTemplate(string relativePath);
    IReadOnlyList<GuidanceSource> GetReferenceSources();
    IReadOnlyList<GuidanceSource> GetTemplateSources();
}
