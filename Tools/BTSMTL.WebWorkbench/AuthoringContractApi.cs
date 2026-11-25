namespace BTSMTL.WebWorkbench;

public static partial class AuthoringApi
{
    public static void MapAuthoringApi(this WebApplication app, IAuthoringContractAdapter adapter)
    {
        app.MapAuthoringEditorApi(adapter.Document, adapter.Types, adapter.Type, adapter.Fields, adapter.Ports,
            adapter.Configuration, adapter.Elements);
        app.MapPost("/api/authoring/documents/apply", (string documentPath, AuthoringDocumentApplyRequest input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { documentPath }, () => adapter.Apply(documentPath, input, context.Connection.LocalPort)));
        app.MapPost("/api/authoring/documents/queries/nodes", (string documentPath, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.GraphId, input.Offset, input.Limit }, () => adapter.Nodes(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/profile", (string documentPath, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Offset, input.Limit }, () => adapter.Profile(documentPath, input)));
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
    }

    public static void MapAuthoringEditorApi(this WebApplication app,
        Func<string, SkillAuthoringPageQuery, Task<AuthoringQueryResult>> document,
        Func<string, string?, SkillAuthoringPageQuery, Task<AuthoringQueryResult>> types,
        Func<string, SkillAuthoringTypeQuery, Task<AuthoringQueryResult>> type,
        Func<string, SkillAuthoringNodeQuery, Task<AuthoringQueryResult>> fields,
        Func<string, SkillAuthoringNodeQuery, Task<AuthoringQueryResult>> ports,
        Func<SkillAuthoringConfigurationQuery, Task<AuthoringQueryResult>> configuration,
        Func<string, AuthoringElementsQuery, Task<AuthoringQueryResult>> elements)
    {
        app.MapPost("/api/authoring/documents/document", (string documentPath, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { documentPath, input.GraphId }, () => document(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/types", (string documentPath, string? search, SkillAuthoringPageQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { search, input.Offset, input.Limit }, () => types(documentPath, search, input)));
        app.MapPost("/api/authoring/documents/queries/type", (string documentPath, SkillAuthoringTypeQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Type, input.GraphId, input.Offset, input.Limit }, () => type(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/fields", (string documentPath, SkillAuthoringNodeQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.GraphId, input.NodeId, input.Offset, input.Limit }, () => fields(documentPath, input)));
        app.MapPost("/api/authoring/documents/queries/ports", (string documentPath, SkillAuthoringNodeQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.GraphId, input.NodeId, input.Offset, input.Limit }, () => ports(documentPath, input)));
        app.MapPost("/api/authoring/types/describe", (SkillAuthoringConfigurationQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Type, input.Domain }, () => configuration(input)));
        app.MapPost("/api/authoring/documents/queries/elements", (string documentPath, AuthoringElementsQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.InputContext }, () => elements(documentPath, input)));
    }
}
