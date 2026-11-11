namespace BTSMTL.WebWorkbench;

public static partial class AuthoringApi
{
    public static void MapAuthoringApi(this WebApplication app, IAuthoringContractAdapter adapter)
    {
        app.MapPost("/api/authoring/documents/apply", (string documentPath, AuthoringDocumentApplyRequest input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { documentPath }, () => adapter.Apply(documentPath, input, context.Connection.LocalPort)));
        app.MapPost("/api/authoring/documents/document", (string documentPath, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { documentPath, input.GraphId }, () => adapter.Document(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/types", (string documentPath, string? search, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { search, input.Offset, input.Limit }, () => adapter.Types(documentPath, search, input)));
        app.MapPost("/api/authoring/documents/queries/type", (string documentPath, SkillAuthoringTypeQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Type, input.GraphId, input.Offset, input.Limit }, () => adapter.Type(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/nodes", (string documentPath, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.GraphId, input.Offset, input.Limit }, () => adapter.Nodes(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/fields", (string documentPath, SkillAuthoringNodeQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.GraphId, input.NodeId, input.Offset, input.Limit }, () => adapter.Fields(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/profile", (string documentPath, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Offset, input.Limit }, () => adapter.Profile(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/ports", (string documentPath, SkillAuthoringNodeQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.GraphId, input.NodeId, input.Offset, input.Limit }, () => adapter.Ports(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/references", (string documentPath, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Offset, input.Limit }, () => adapter.References(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/referrers", (string documentPath, SkillAuthoringReferenceQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Identity, input.Target, input.Offset, input.Limit }, () => adapter.Referrers(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/project", (string documentPath, SkillAuthoringProjectQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Offset, input.Limit, input.Snapshot }, () => adapter.Project(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/reference-curves", (string documentPath, SkillAuthoringReferenceQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Identity }, () => adapter.ReferenceCurves(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/blackboards", (string documentPath, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.GraphId, input.Offset, input.Limit }, () => adapter.Blackboards(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/elements", (string documentPath, string? timelineId, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { timelineId }, () => adapter.Elements(documentPath, timelineId, input)));
        app.MapPost("/api/authoring/types/describe", (SkillAuthoringConfigurationQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Type, input.Domain }, () => adapter.Configuration(input)));
    }
}
