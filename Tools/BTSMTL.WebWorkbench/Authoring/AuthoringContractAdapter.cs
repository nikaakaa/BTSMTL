using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public sealed record AuthoringQueryResult(object Value, int StatusCode = 200);

public interface IAuthoringContractAdapter
{
    Task<AuthoringQueryResult> Apply(string documentPath, AuthoringDocumentApplyRequest input, int localPort);
    Task<AuthoringQueryResult> Document(string documentPath, SkillAuthoringPageQuery input);
    Task<AuthoringQueryResult> Types(string documentPath, string? search, SkillAuthoringPageQuery input);
    Task<AuthoringQueryResult> Type(string documentPath, SkillAuthoringTypeQuery input);
    Task<AuthoringQueryResult> Nodes(string documentPath, SkillAuthoringPageQuery input);
    Task<AuthoringQueryResult> Fields(string documentPath, SkillAuthoringNodeQuery input);
    Task<AuthoringQueryResult> Profile(string documentPath, SkillAuthoringPageQuery input);
    Task<AuthoringQueryResult> Ports(string documentPath, SkillAuthoringNodeQuery input);
    Task<AuthoringQueryResult> References(string documentPath, SkillAuthoringPageQuery input);
    Task<AuthoringQueryResult> Referrers(string documentPath, SkillAuthoringReferenceQuery input);
    Task<JObject> Project(string documentPath, SkillAuthoringProjectQuery input);
    Task<AuthoringQueryResult> ReferenceCurves(string documentPath, SkillAuthoringReferenceQuery input);
    Task<AuthoringQueryResult> Blackboards(string documentPath, SkillAuthoringPageQuery input);
    Task<AuthoringQueryResult> Elements(string documentPath, AuthoringElementsQuery input);
    Task<AuthoringQueryResult> Configuration(SkillAuthoringConfigurationQuery input);
}
