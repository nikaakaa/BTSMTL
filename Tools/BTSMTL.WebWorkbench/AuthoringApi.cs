using System.Text.Json;
using System.Text.Json.Serialization;
using BTSMTL.Authoring.Workspace;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public sealed record AuthoringVariableQuery([property: JsonRequired] string OwnerId,
    [property: JsonRequired] AuthoringDocumentEditingView EditingView);

public sealed record AuthoringVariableEdit([property: JsonRequired] string OwnerId,
    [property: JsonRequired] AuthoringDocumentEditingView EditingView,
    [property: JsonRequired] AuthoringVariableOperation Operation);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AuthoringVariableCreate), "create")]
[JsonDerivedType(typeof(AuthoringVariableRename), "rename")]
[JsonDerivedType(typeof(AuthoringVariableSetDefault), "set-default")]
[JsonDerivedType(typeof(AuthoringVariableSetObjectKind), "set-object-kind")]
[JsonDerivedType(typeof(AuthoringVariableConfigurePublic), "configure-public")]
[JsonDerivedType(typeof(AuthoringVariableConfigureBlackboard), "configure-blackboard")]
[JsonDerivedType(typeof(AuthoringVariableDelete), "delete")]
public abstract record AuthoringVariableOperation
{
    public abstract string Apply(BtsmtlGraphVariableDeclarations source);
}

public sealed record AuthoringVariableCreate([property: JsonRequired] string TypeId, string? Name = null,
    string? ObjectKind = null) : AuthoringVariableOperation
{
    public override string Apply(BtsmtlGraphVariableDeclarations source)
    {
        OrchestrationObjectKind? kind = ObjectKind == null ? null : OrchestrationObjectKinds.Require(ObjectKind);
        return (Name == null ? source.Create(TypeId, kind) : source.Create(TypeId, Name, kind)).VariableId;
    }
}

public sealed record AuthoringVariableSetObjectKind([property: JsonRequired] string VariableId,
    [property: JsonRequired] string ObjectKind) : AuthoringVariableOperation
{
    public override string Apply(BtsmtlGraphVariableDeclarations source)
    {
        source.SetObjectKind(VariableId, OrchestrationObjectKinds.Require(ObjectKind));
        return VariableId;
    }
}

public sealed record AuthoringVariableRename([property: JsonRequired] string VariableId,
    [property: JsonRequired] string Name) : AuthoringVariableOperation
{
    public override string Apply(BtsmtlGraphVariableDeclarations source)
    {
        source.Rename(VariableId, Name);
        return VariableId;
    }
}

public sealed record AuthoringVariableSetDefault([property: JsonRequired] string VariableId,
    [property: JsonRequired] JsonElement Value) : AuthoringVariableOperation
{
    public override string Apply(BtsmtlGraphVariableDeclarations source)
    {
        source.SetDefaultValue(VariableId, JToken.Parse(Value.GetRawText()));
        return VariableId;
    }
}

public sealed record AuthoringVariableConfigurePublic([property: JsonRequired] string VariableId,
    [property: JsonRequired] bool IsPublic) : AuthoringVariableOperation
{
    public override string Apply(BtsmtlGraphVariableDeclarations source)
    {
        source.ConfigureVariable(VariableId, IsPublic);
        return VariableId;
    }
}

public sealed record AuthoringVariableConfigureBlackboard([property: JsonRequired] string VariableId,
    [property: JsonRequired] JsonElement Changes) : AuthoringVariableOperation
{
    public override string Apply(BtsmtlGraphVariableDeclarations source)
    {
        source.ConfigureBlackboard(VariableId, JObject.Parse(Changes.GetRawText()));
        return VariableId;
    }
}

public sealed record AuthoringVariableDelete([property: JsonRequired] string VariableId) : AuthoringVariableOperation
{
    public override string Apply(BtsmtlGraphVariableDeclarations source)
    {
        source.Delete(VariableId);
        return VariableId;
    }
}

public sealed record AuthoringReferenceReplacementInput([property: JsonRequired] string Action,
    [property: JsonRequired] string Domain, [property: JsonRequired] string OldValue,
    [property: JsonRequired] string NewValue, string? Context = null, string? ExpectedRevision = null);

// 与项目无关的作者文档接口：文档目录、正文、编辑视图与保存、变量声明、适配者目录与资源引用替换。
// 项目专有的类型、节点与引用查询由项目在同一 /api/authoring 前缀下另行挂载。
public static class AuthoringApi
{
    static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    public static void MapAuthoringApi(this WebApplication app, AuthoringDocumentService documents,
        IAuthoringResourceAdapter resources)
    {
        string workspace = documents.Workspace;
        AuthoringCommits commits = AuthoringCommits.For(workspace);
        app.MapGet("/api/authoring/documents", (string? kind, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { kind }, () => documents.List(kind)));
        app.MapGet("/api/authoring/documents/content", (string documentPath, string? revision, int? wait, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, null, () => documents.Open(documentPath, revision,
                TimeSpan.FromSeconds(Math.Clamp(wait ?? 0, 0, 30)), context.RequestAborted)));
        app.MapGet("/api/authoring/documents/elements", (string? kind, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { kind }, () => documents.Elements(kind)));
        app.MapPost("/api/authoring/documents/editing-view", (AuthoringDocumentEditingView input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { DocumentPaths = input.Writes.Select(write => write.DocumentPath) },
                () => documents.EditingView(input)));
        app.MapPut("/api/authoring/documents", (AuthoringDocumentEditingView input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { DocumentPaths = input.Writes.Select(write => write.DocumentPath) },
                () => documents.Save(input)));
        app.MapPost("/api/authoring/documents/queries/variables", (AuthoringVariableQuery input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.OwnerId },
                () => documents.Variables(input.OwnerId, input.EditingView)));
        app.MapPost("/api/authoring/documents/variables/edit", (AuthoringVariableEdit input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.OwnerId, Operation = input.Operation.GetType().Name }, async () =>
            {
                string? variableId = null;
                var draft = await documents.MutateVariables(input.OwnerId, input.EditingView,
                    source => variableId = input.Operation.Apply(source));
                draft.Result["variableId"] = variableId;
                draft.Result["editingView"] = JObject.Parse(JsonSerializer.Serialize(draft.EditingView, s_Json));
                return draft.Result;
            }));
        app.MapGet("/api/authoring/catalog", (string? adapter, string? revision, HttpContext context) => commits.Read(() =>
            Task.FromResult(AuthoringQueryResults.Read(context, new { adapter, revision },
                () => new AuthoringCatalog(workspace).Read(adapter).ToJson(revision)))));
        app.MapPost("/api/authoring/references/replace", (AuthoringReferenceReplacementInput input, HttpContext context) =>
            commits.Write(() => Task.FromResult(AuthoringQueryResults.Read(context,
                new { input.Action, input.Domain, input.OldValue, input.NewValue, input.Context }, () =>
                {
                    if (input.Action is not ("prepare" or "commit"))
                        throw new ArgumentException("引用替换动作必须是prepare或commit。");
                    AuthoringCatalogSnapshot catalog = new AuthoringCatalog(workspace).Read();
                    AuthoringReferenceCapture capture = resources.CaptureReferences(workspace, catalog);
                    AuthoringReferenceReplacementPlan plan = AuthoringReferenceReplacement.Prepare(
                        new AuthoringReferenceReplacementRequest
                        {
                            Domain = input.Domain, OldValue = input.OldValue, NewValue = input.NewValue, Context = input.Context
                        }, catalog, capture.Documents, capture.SchemaInputs);
                    if (input.Action == "commit") plan.Commit(commits, input.ExpectedRevision!);
                    return plan.Describe(input.Action == "commit");
                }))));
    }
}
