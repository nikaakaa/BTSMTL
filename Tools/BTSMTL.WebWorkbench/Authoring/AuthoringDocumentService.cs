using System.Diagnostics;
using BTSMTL.Authoring.Workspace;
using BTSMTL.Graphs;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public sealed record AuthoringVariablesDraft(AuthoringDocumentEditingView EditingView, JObject Result);

// 作者文档的列表、打开、编辑视图、保存与变量声明编辑；文档类型与保存规范化由项目文档适配提供。
public sealed class AuthoringDocumentService
{
    readonly AuthoringDocumentRepository m_Documents;

    public AuthoringDocumentService(string workspace, IAuthoringDocumentAdapter adapter)
    {
        Adapter = adapter;
        m_Documents = new AuthoringDocumentRepository(workspace, adapter.DeclaredElements);
    }

    public IAuthoringDocumentAdapter Adapter { get; }
    public AuthoringDocumentRepository Repository => m_Documents;
    public string Workspace => m_Documents.Workspace;

    public async Task<JObject> List(string? kind = null)
    {
        IReadOnlyList<AuthoringDocumentSnapshot> documents = await m_Documents.Read();
        return new JObject
        {
            ["success"] = true,
            ["documents"] = new JArray(documents.Where(document => kind == null || document.Kind.Id == kind)
                .Select(document => Describe(document, false)))
        };
    }

    // 调用方带上已读修订且修订未变时只回执 changed=false，不重复传正文；带等待时长时按文件戳每 100 ms 复查，变化立即答复。
    public async Task<JObject> Open(string documentPath, string? revision = null, TimeSpan wait = default,
        CancellationToken cancellation = default)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            AuthoringDocumentSnapshot document = await m_Documents.Open(documentPath);
            if (revision == null || document.Revision != revision)
                return new JObject { ["success"] = true, ["document"] = Describe(document, true) };
            if (elapsed.Elapsed >= wait || cancellation.IsCancellationRequested)
                return new JObject { ["success"] = true, ["changed"] = false, ["revision"] = revision };
            await Task.Delay(100);
        }
    }

    public async Task<JObject> Elements(string? kind = null)
    {
        IReadOnlyList<AuthoringDocumentSnapshot> documents = await m_Documents.Read();
        return new JObject { ["success"] = true, ["elements"] = Elements(documents, kind) };
    }

    public async Task<IReadOnlyList<AuthoringDocumentSnapshot>> Capture(AuthoringDocumentEditingView? request) =>
        request == null ? await m_Documents.Read() : m_Documents.EditingView(await m_Documents.Read(), Writes(request));

    public async Task<JObject> EditingView(AuthoringDocumentEditingView request)
    {
        AuthoringDocumentWrite[] writes = Writes(request);
        IReadOnlyList<AuthoringDocumentSnapshot> documents = m_Documents.EditingView(await m_Documents.Read(), writes);
        return View(documents, writes);
    }

    public async Task<JObject> Save(AuthoringDocumentEditingView request)
    {
        AuthoringDocumentWrite[] writes = Adapter.PrepareSave(Workspace, Writes(request), await m_Documents.Read(), request);
        return View(await m_Documents.Save(writes, Reads(request)), writes);
    }

    public async Task<JObject> Variables(string ownerId, AuthoringDocumentEditingView request)
    {
        IReadOnlyList<AuthoringDocumentSnapshot> view = await Capture(request);
        AuthoringDocumentSnapshot owner = VariablesOwner(view, ownerId);
        return new JObject
        {
            ["success"] = true, ["document"] = Describe(owner, false),
            ["value"] = BtsmtlGraphVariableDeclarationsJson.Write(BtsmtlGraphVariableDeclarationsJson.Read(owner.Root)),
            ["choices"] = Adapter.VariableTypeChoices()
        };
    }

    public async Task<AuthoringVariablesDraft> MutateVariables(string ownerId, AuthoringDocumentEditingView request,
        Action<BtsmtlGraphVariableDeclarations> mutate)
    {
        IReadOnlyList<AuthoringDocumentSnapshot> captured = await m_Documents.Read();
        IReadOnlyList<AuthoringDocumentSnapshot> view = m_Documents.EditingView(captured, Writes(request));
        AuthoringDocumentSnapshot owner = VariablesOwner(view, ownerId);
        var variables = BtsmtlGraphVariableDeclarationsJson.Read(owner.Root);
        string revision = variables.Revision;
        mutate(variables);
        JObject value = BtsmtlGraphVariableDeclarationsJson.Write(variables);
        AuthoringDocumentEditingView edited = request;
        if (variables.Revision != revision)
        {
            var files = owner.Files.ToDictionary(file => file.Key, file => file.Value, StringComparer.Ordinal);
            files[owner.Kind.RootFile] = AuthoringJsonText.Text(value);
            AuthoringDocumentEdit? previous = request.Writes.SingleOrDefault(write => write.DocumentPath == owner.DocumentPath);
            AuthoringDocumentSnapshot saved = captured.Single(document => document.DocumentPath == owner.DocumentPath);
            var replacement = new AuthoringDocumentEdit(owner.Kind.Id, owner.DocumentPath,
                previous == null ? saved.Revision : previous.ExpectedRevision, files);
            edited = request with
            {
                Writes = request.Writes.Where(write => write.DocumentPath != owner.DocumentPath).Append(replacement).ToArray(),
                Reads = request.Reads.Any(read => read.DocumentPath == owner.DocumentPath) ? request.Reads :
                    request.Reads.Append(new AuthoringDocumentReadRevision(owner.DocumentPath, saved.Revision)).ToArray()
            };
        }
        AuthoringDocumentWrite[] writes = Writes(edited);
        IReadOnlyList<AuthoringDocumentSnapshot> finalView = m_Documents.EditingView(captured, writes);
        JObject result = View(finalView, writes);
        result["document"] = Describe(finalView.Single(document => document.DocumentPath == owner.DocumentPath), false);
        result["value"] = value;
        return new AuthoringVariablesDraft(edited, result);
    }

    static AuthoringDocumentSnapshot VariablesOwner(IReadOnlyList<AuthoringDocumentSnapshot> view, string ownerId) =>
        view.Single(document => document.Kind == AuthoringDocumentKind.Variables && document.Identity == ownerId);

    public AuthoringDocumentRead[] Reads(AuthoringDocumentEditingView request) => request.Reads.Select(read =>
        new AuthoringDocumentRead(DirectoryPath(read.DocumentPath), read.Revision)).ToArray();

    public AuthoringDocumentWrite[] Writes(AuthoringDocumentEditingView request) => request.Writes
        .Select(write => new AuthoringDocumentWrite(
            AuthoringDocumentKind.All.Single(kind => kind.Id == write.Kind),
            DirectoryPath(write.DocumentPath), write.ExpectedRevision, write.Files)).ToArray();

    public string DirectoryPath(string documentPath) => Path.GetFullPath(Path.Combine(m_Documents.Workspace, documentPath));

    JObject View(IReadOnlyList<AuthoringDocumentSnapshot> documents, IReadOnlyList<AuthoringDocumentWrite> writes) => new()
    {
        ["success"] = true,
        ["documents"] = new JArray(documents.Where(document => writes.Any(write =>
                string.Equals(write.DirectoryPath, document.DirectoryPath, StringComparison.OrdinalIgnoreCase)))
            .Select(document => Describe(document, true))),
        ["elements"] = Elements(documents, null)
    };

    public JArray Elements(IReadOnlyList<AuthoringDocumentSnapshot> documents, string? kind) =>
        new(m_Documents.Elements(documents).Where(element => kind == null || element.Kind == kind).Select(Describe));

    public static JObject Describe(AuthoringDocumentElement element) => new()
    {
        // 作者看到的名称取内容自身的 m_Name；身份是目录哈希，只在内容没有名称时显示。
        ["kind"] = element.Kind, ["identity"] = element.Identity,
        ["name"] = element.Value["m_Name"]?.Value<string>() ?? element.Identity,
        ["contentDomain"] = element.ContentDomain, ["sourceIdentity"] = element.SourceIdentity,
        ["documentPath"] = element.Owner.DocumentPath, ["relativePath"] = element.RelativePath,
        ["jsonPath"] = element.JsonPath, ["revision"] = element.Owner.Revision,
        ["authoringType"] = element.AuthoringType?.FullName
    };

    public JObject Describe(AuthoringDocumentSnapshot document, bool content)
    {
        var result = new JObject
        {
            // 作者看到的名称取根文件的名称（图文档为 name）；没有名称的文档用目录名。
            ["kind"] = document.Kind.Id, ["identity"] = document.Identity,
            ["name"] = (string?)document.Root?["m_Name"] ?? (string?)document.Root?["name"] ?? Path.GetFileName(document.DocumentPath),
            ["documentPath"] = document.DocumentPath, ["rootFile"] = document.Kind.RootFile,
            ["revision"] = document.Revision, ["sourceIdentity"] = document.SourceIdentity,
            ["contentDomain"] = document.ContentDomain, ["scope"] = document.Scope
        };
        if (Adapter.AuthoringType(document) is { } authoringType)
        {
            result["authoringType"] = authoringType.FullName;
            // 存储形状只随正文给出，供读写该文档的调用方使用；目录列表只列身份与修订。
            if (content && Adapter.StorageShape(authoringType) is { } shape)
                result["storageShape"] = shape;
        }
        if (content)
            result["files"] = new JObject(document.Files.OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => new JProperty(file.Key, file.Value)));
        return result;
    }
}
