#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Storage;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{
public sealed class AuthoringDocumentSet
{
    readonly Dictionary<GraphAuthoringElementId, object> m_Documents = new();
    readonly Dictionary<GraphAuthoringElementId, AuthoringSourceFile> m_Sources = new();
    readonly Dictionary<GraphAuthoringElementId, HashSet<GraphAuthoringElementId>> m_Elements = new();

    AuthoringDocumentSet(AuthoringDocumentSet accepted, AuthoringRootFiles source)
    {
        Source = source;
        ContentRevision = accepted.ContentRevision;
        m_Documents = accepted.m_Documents;
        m_Elements = accepted.m_Elements;
        string directory = Path.GetDirectoryName(source.Files[0].Path)!;
        var files = source.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        foreach (AuthoringDocumentFile file in source.Root.Files.Span)
            m_Sources.Add(file.Id, files[AuthoringWorkspaceDirectory.Resolve(directory, file.Path)]);
    }

    public AuthoringDocumentSet(AuthoringRootFiles source)
    {
        Source = source;
        string directory = Path.GetDirectoryName(source.Files[0].Path)!;
        var files = source.Files.Skip(1).ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identity = new StringBuilder();
        JObject rootContent = AuthoringDocumentJson.ReadObject(AuthoringDocumentJson.WriteRoot(source.Root));
        rootContent.Remove("unityOutputs");
        rootContent["files"] = new JArray(source.Root.Files.ToArray()
            .Where(file => file.Kind != AuthoringDocumentKind.Layout)
            .OrderBy(file => file.Id.Value, StringComparer.Ordinal)
            .Select(file => new JObject { ["id"] = file.Id.Value, ["kind"] = file.Kind.ToString() }));
        identity.Append(rootContent.ToString(Newtonsoft.Json.Formatting.None)).Append('\n');
        m_Elements.Add(source.Root.Id, new HashSet<GraphAuthoringElementId> { source.Root.Id });
        foreach (AuthoringDocumentFile file in source.Root.Files.Span)
        {
            string path = AuthoringWorkspaceDirectory.Resolve(directory, file.Path);
            if (!paths.Add(path)) throw Error(file.Id, "一个物理文件不能由同根多次声明。");
            AuthoringSourceFile input = files[path];
            object document = file.Kind switch
            {
                AuthoringDocumentKind.Graph => AuthoringDocumentJson.ReadGraph(input.Text, input.Path),
                AuthoringDocumentKind.Timeline or AuthoringDocumentKind.InputPlan =>
                    AuthoringDocumentJson.ReadTimeline(input.Text, input.Path),
                AuthoringDocumentKind.References => AuthoringDocumentJson.ReadReferenceTable(input.Text, input.Path),
                AuthoringDocumentKind.Layout => AuthoringDocumentJson.ReadObject(input.Text),
                _ => throw Error(file.Id, "未定义的作者文件种类。")
            };
            m_Documents.Add(file.Id, document);
            m_Sources.Add(file.Id, input);
            var elements = new HashSet<GraphAuthoringElementId> { file.Id };
            m_Elements.Add(file.Id, elements);
            if (document is AuthoringGraphDocument graph)
            {
                RequireContext(file.Id, graph.Id, graph.DefinitionVersion);
                foreach (AuthoringNodeDocument node in graph.Nodes.Span) AddElement(elements, file.Id, node.Id);
                foreach (AuthoringEdgeDocument edge in graph.Edges.Span) AddElement(elements, file.Id, edge.Id);
                foreach (AuthoringVariableDocument variable in graph.Variables.Span) AddElement(elements, file.Id, variable.Id);
            }
            else if (document is AuthoringTimelineDocument timeline)
            {
                RequireContext(file.Id, timeline.Id, timeline.DefinitionVersion);
                foreach (AuthoringTimelineTrackDocument track in timeline.Tracks.Span)
                {
                    AddElement(elements, file.Id, track.Id);
                    foreach (AuthoringTimelineClipDocument clip in track.Clips.Span) AddElement(elements, file.Id, clip.Id);
                    foreach (AuthoringTimelineMarkerDocument marker in track.Markers.Span) AddElement(elements, file.Id, marker.Id);
                }
                foreach (AuthoringTimelineBindingDocument binding in timeline.Bindings.Span) AddElement(elements, file.Id, binding.Id);
            }
        }
        foreach (var item in m_Documents.OrderBy(item => item.Key.Value, StringComparer.Ordinal))
        {
            if (item.Value is JObject) continue;
            identity.Append(item.Key.Value).Append('\n').Append(Canonical(item.Value)).Append('\n');
        }
        ContentRevision = AuthoringFileRevision.Compute(Encoding.UTF8.GetBytes(identity.ToString()));
    }

    public AuthoringRootFiles Source { get; }
    public AuthoringRootDocument Root => Source.Root;
    public string ContentRevision { get; }
    public IReadOnlyDictionary<GraphAuthoringElementId, object> Documents => m_Documents;
    public IReadOnlyDictionary<GraphAuthoringElementId, AuthoringSourceFile> Sources => m_Sources;
    public T Require<T>(GraphAuthoringElementId id) where T : class => (T)m_Documents[id];
    internal AuthoringDocumentSet WithSource(AuthoringRootFiles source) => new(this, source);

    public void ReceiveReferences()
    {
        ReceiveFields(Root.Id, Root.Fields);
        foreach (var item in m_Documents)
        {
            if (item.Value is AuthoringGraphDocument graph)
            {
                ReceiveReference(item.Key, graph.Owner);
                ReceiveFields(item.Key, graph.Fields);
                foreach (AuthoringNodeDocument node in graph.Nodes.Span) ReceiveFields(item.Key, node.Fields);
                foreach (AuthoringEdgeDocument edge in graph.Edges.Span) ReceiveFields(item.Key, edge.Fields);
                foreach (AuthoringVariableDocument variable in graph.Variables.Span)
                {
                    ReceiveFields(item.Key, variable.Declaration);
                    ReceiveValue(item.Key, variable.DefaultValue);
                }
            }
            else if (item.Value is AuthoringTimelineDocument timeline)
            {
                ReceiveReference(item.Key, timeline.Owner);
                ReceiveFields(item.Key, timeline.Fields);
                foreach (AuthoringTimelineTrackDocument track in timeline.Tracks.Span)
                {
                    ReceiveFields(item.Key, track.Fields);
                    foreach (AuthoringTimelineClipDocument clip in track.Clips.Span) ReceiveFields(item.Key, clip.Fields);
                    foreach (AuthoringTimelineMarkerDocument marker in track.Markers.Span) ReceiveFields(item.Key, marker.Fields);
                }
                foreach (AuthoringTimelineBindingDocument binding in timeline.Bindings.Span) ReceiveFields(item.Key, binding.Fields);
            }
        }
    }

    public static string Canonical(object document) => document switch
    {
        AuthoringRootDocument root => AuthoringDocumentJson.WriteRoot(root),
        AuthoringGraphDocument graph => AuthoringDocumentJson.WriteGraph(graph),
        AuthoringTimelineDocument timeline => AuthoringDocumentJson.WriteTimeline(timeline),
        AuthoringReferenceTable references => AuthoringDocumentJson.WriteReferenceTable(references),
        JObject layout => layout.ToString(Newtonsoft.Json.Formatting.Indented) + "\n",
        _ => throw new ArgumentException("没有正式作者文档编码：" + document.GetType().FullName, nameof(document))
    };

    void ReceiveFields(GraphAuthoringElementId document, ReadOnlyMemory<AuthoringNamedValue> fields)
    {
        foreach (AuthoringNamedValue field in fields.Span) ReceiveValue(document, field.Value);
    }

    void ReceiveValue(GraphAuthoringElementId document, AuthoringValue value)
    {
        switch (value)
        {
            case AuthoringReferenceValue reference: ReceiveReference(document, reference.Target); break;
            case AuthoringObjectValue composite: ReceiveFields(document, composite.Members); break;
            case AuthoringSequenceValue sequence:
                foreach (AuthoringValue item in sequence.Items.Span) ReceiveValue(document, item);
                break;
        }
    }

    void ReceiveReference(GraphAuthoringElementId document, AuthoringContentReference target)
    {
        if (target.RootId.Equals(Root.Id))
        {
            if (!m_Elements.TryGetValue(target.DocumentId, out var elements) || !elements.Contains(target.ElementId))
                throw Error(document, $"引用目标不存在：{target.DocumentId}/{target.ElementId}。");
        }
        else if (!Root.ExternalReferences.ToArray().Contains(target))
            throw Error(document, $"引用不属于本根声明的外部输入：{target.RootId}/{target.DocumentId}/{target.ElementId}。");
    }

    void RequireContext(GraphAuthoringElementId manifest, GraphAuthoringElementId actual, string definitions)
    {
        if (!manifest.Equals(actual)) throw Error(manifest, "文件正文身份与根清单不一致。");
        if (definitions != Root.DefinitionVersion) throw Error(manifest, "文件默认定义版本与根不一致。");
    }

    static void AddElement(ISet<GraphAuthoringElementId> elements, GraphAuthoringElementId document, GraphAuthoringElementId id)
    {
        if (!elements.Add(id)) throw Error(document, "元素身份重复：" + id);
    }

    static InvalidOperationException Error(GraphAuthoringElementId document, string reason) =>
        new("作者文档 " + document.Value + ": " + reason);
}
}
