#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Documents;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{

public sealed record AuthoringReferenceSource(string RootPath, string RootId, string RootRevision,
    string DocumentPath, string DocumentId, string FileRevision, string ElementId, string Pointer);
public sealed record AuthoringReferenceTarget(string RootId, string DocumentId, string ElementId);
public sealed record AuthoringReferenceUse(AuthoringReferenceSource Source, AuthoringReferenceTarget Target,
    string Relation, string? ValueType);
public sealed record AuthoringReferencePage(AuthoringReferenceUse[] Items, int Offset, int Total,
    int? NextOffset, bool Complete, AuthoringSourceProblem[] Problems);
public sealed record AuthoringLocationPage(AuthoringReferenceSource[] Items, int Offset, int Total,
    int? NextOffset, bool Complete, AuthoringSourceProblem[] Problems);

public static class AuthoringReferenceQuery
{
    public static AuthoringLocationPage Locations(AuthoringWorkspaceDirectory workspace, string documentId,
        string elementId, int offset, int limit, string? rootId)
    {
        RequirePage(offset, limit);
        var matches = new List<AuthoringReferenceSource>();
        var problems = new List<AuthoringSourceProblem>();
        int cursor = 0;
        do
        {
            AuthoringPage<AuthoringRootEntry> page = workspace.List(cursor, 200);
            foreach (AuthoringRootEntry entry in page.Items)
            {
                if (entry.Problems.Count != 0) { problems.AddRange(entry.Problems); continue; }
                try
                {
                    AuthoringSourceFile manifest = workspace.ReadSource(entry.Path);
                    AuthoringRootDocument declaration = AuthoringDocumentJson.ReadRoot(manifest.Text, manifest.Path);
                    if (rootId != null && declaration.Id.Value != rootId) continue;
                    if (declaration.Id.Value != documentId && !declaration.Files.ToArray().Any(file => file.Id.Value == documentId)) continue;
                    AuthoringRootFiles root = workspace.Read(entry.Path);
                    (AuthoringSourceFile file, JObject? element) = FindElement(root, documentId, elementId);
                    if (element == null) continue;
                    string directory = Path.GetDirectoryName(root.Files[0].Path)!;
                    matches.Add(new(entry.Path, root.Root.Id.Value, root.Revision,
                        Path.GetRelativePath(directory, file.Path).Replace('\\', '/'), documentId, file.Revision,
                        elementId, element.Path));
                }
                catch (Exception error) when (error is IOException or FormatException or
                    Newtonsoft.Json.JsonException or ArgumentException)
                { problems.Add(new(entry.Path, "authoring_location_invalid", error.Message)); }
            }
            if (page.NextOffset is not int next) break;
            cursor = next;
        } while (true);
        return new(matches.Skip(offset).Take(limit).ToArray(), offset, matches.Count,
            offset + limit < matches.Count ? offset + limit : null, problems.Count == 0, problems.ToArray());
    }

    public static object Content(AuthoringWorkspaceDirectory workspace, string path,
        string documentId, string elementId)
    {
        AuthoringRootFiles root = workspace.Read(path);
        (AuthoringSourceFile source, JObject? element) = FindElement(root, documentId, elementId);
        if (element == null)
            throw new InvalidDataException($"文档 '{documentId}' 不存在稳定对象 '{elementId}'。");
        string directory = Path.GetDirectoryName(root.Files[0].Path)!;
        return new { RootPath = path, RootId = root.Root.Id.Value, RootRevision = root.Revision,
            DocumentId = documentId, ElementId = elementId,
            SourcePath = Path.GetRelativePath(directory, source.Path).Replace('\\', '/'),
            FileRevision = source.Revision, JsonPath = element.Path,
            Content = element.ToString(Newtonsoft.Json.Formatting.Indented) };
    }

    static (AuthoringSourceFile File, JObject? Element) FindElement(AuthoringRootFiles root,
        string documentId, string elementId)
    {
        string directory = Path.GetDirectoryName(root.Files[0].Path)!;
        string sourcePath = documentId == root.Root.Id.Value ? root.Files[0].Path :
            AuthoringWorkspaceDirectory.Resolve(directory, root.Root.Files.ToArray()
                .Single(file => file.Id.Value == documentId).Path);
        AuthoringSourceFile source = root.Files.Single(file => file.Path == sourcePath);
        JObject content = JObject.Parse(source.Text);
        JObject[] elements = content.DescendantsAndSelf().OfType<JObject>()
            .Where(value => value["id"]?.Type == JTokenType.String && value["id"]!.Value<string>() == elementId).ToArray();
        if (elements.Length > 1)
            throw new InvalidDataException($"文档 '{documentId}' 的稳定对象 '{elementId}' 匹配 {elements.Length} 项。");
        return (source, elements.Length == 0 ? null : elements[0]);
    }

    public static AuthoringReferencePage Dependencies(AuthoringWorkspaceDirectory workspace, string path,
        string? documentId, string? elementId, int offset, int limit)
    {
        RequirePage(offset, limit);
        AuthoringReferenceUse[] matches = Read(workspace, path).Where(use =>
            (documentId == null || use.Source.DocumentId == documentId) &&
            (elementId == null || use.Source.ElementId == elementId)).ToArray();
        return Page(matches, offset, limit, Array.Empty<AuthoringSourceProblem>());
    }

    public static AuthoringReferencePage Uses(AuthoringWorkspaceDirectory workspace, string rootId,
        string? documentId, string? elementId, int offset, int limit)
    {
        RequirePage(offset, limit);
        var uses = new List<AuthoringReferenceUse>();
        var problems = new List<AuthoringSourceProblem>();
        int cursor = 0;
        do
        {
            AuthoringPage<AuthoringRootEntry> roots = workspace.List(cursor, 200);
            foreach (AuthoringRootEntry root in roots.Items)
            {
                if (root.Problems.Count != 0) { problems.AddRange(root.Problems); continue; }
                try
                {
                    uses.AddRange(Read(workspace, root.Path).Where(use => use.Target.RootId == rootId &&
                        (documentId == null || use.Target.DocumentId == documentId) &&
                        (elementId == null || use.Target.ElementId == elementId)));
                }
                catch (Exception error) when (error is IOException or FormatException or
                    Newtonsoft.Json.JsonException or ArgumentException)
                {
                    problems.Add(new(root.Path, "reference_source_invalid", error.Message));
                }
            }
            if (roots.NextOffset is not int next) break;
            cursor = next;
        } while (true);
        return Page(uses.ToArray(), offset, limit, problems.ToArray());
    }

    static AuthoringReferencePage Page(AuthoringReferenceUse[] items, int offset, int limit,
        AuthoringSourceProblem[] problems) => new(items.Skip(offset).Take(limit).ToArray(), offset, items.Length,
            offset + limit < items.Length ? offset + limit : null, problems.Length == 0, problems);

    static AuthoringReferenceUse[] Read(AuthoringWorkspaceDirectory workspace, string path)
    {
        AuthoringRootFiles root = workspace.Read(path);
        string directory = Path.GetDirectoryName(root.Files[0].Path)!;
        var files = root.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var source = new AuthoringReferenceSource(path, root.Root.Id.Value, root.Revision, "root.json",
            root.Root.Id.Value, root.Files[0].Revision, root.Root.Id.Value, "");
        var uses = new List<AuthoringReferenceUse>();
        Fields(root.Root.Fields, source, "/fields", uses);
        for (int index = 0; index < root.Root.ExternalReferences.Length; index++)
            Reference(source with { Pointer = "/externalReferences/" + index }, "declared-external", null,
                root.Root.ExternalReferences.Span[index], uses);
        foreach (AuthoringDocumentFile declaration in root.Root.Files.Span)
        {
            if (declaration.Kind == AuthoringDocumentKind.Layout) continue;
            AuthoringSourceFile file = files[AuthoringWorkspaceDirectory.Resolve(directory, declaration.Path)];
            source = source with { DocumentPath = declaration.Path, DocumentId = declaration.Id.Value,
                FileRevision = file.Revision, ElementId = declaration.Id.Value, Pointer = "" };
            if (declaration.Kind == AuthoringDocumentKind.References)
            {
                AuthoringReferenceTable references = AuthoringDocumentJson.ReadReferenceTable(file.Text, file.Path);
                for (int index = 0; index < references.Entries.Length; index++)
                {
                    AuthoringReferenceEntry entry = references.Entries.Span[index];
                    Value(entry.Source, source with { Pointer = "/entries/" + index + "/source" }, uses);
                    Fields(entry.Fields, source, "/entries/" + index + "/fields", uses);
                }
                continue;
            }
            if (declaration.Kind == AuthoringDocumentKind.Graph)
            {
                AuthoringGraphDocument graph = AuthoringDocumentJson.ReadGraph(file.Text, file.Path);
                Reference(source with { Pointer = "/owner" }, "owner", null, graph.Owner, uses);
                Fields(graph.Fields, source, "/fields", uses);
                for (int index = 0; index < graph.Nodes.Length; index++)
                {
                    AuthoringNodeDocument node = graph.Nodes.Span[index];
                    Fields(node.Fields, source with { ElementId = node.Id.Value }, "/nodes/" + index + "/fields", uses);
                }
                for (int index = 0; index < graph.Edges.Length; index++)
                {
                    AuthoringEdgeDocument edge = graph.Edges.Span[index];
                    Fields(edge.Fields, source with { ElementId = edge.Id.Value }, "/edges/" + index + "/fields", uses);
                }
                for (int index = 0; index < graph.Variables.Length; index++)
                {
                    AuthoringVariableDocument variable = graph.Variables.Span[index];
                    var variableSource = source with { ElementId = variable.Id.Value };
                    Fields(variable.Declaration, variableSource, "/variables/" + index + "/declaration", uses);
                    Value(variable.DefaultValue, variableSource with { Pointer = "/variables/" + index + "/defaultValue" }, uses);
                }
                continue;
            }
            if (declaration.Kind is not (AuthoringDocumentKind.Timeline or AuthoringDocumentKind.InputPlan))
                throw new InvalidDataException($"文件 '{file.Path}' 的作者种类 '{declaration.Kind}' 没有正式引用读取入口。");
            AuthoringTimelineDocument timeline = AuthoringDocumentJson.ReadTimeline(file.Text, file.Path);
            Reference(source with { Pointer = "/owner" }, "owner", null, timeline.Owner, uses);
            Fields(timeline.Fields, source, "/fields", uses);
            for (int index = 0; index < timeline.Tracks.Length; index++)
            {
                AuthoringTimelineTrackDocument track = timeline.Tracks.Span[index];
                string trackPath = "/tracks/" + index;
                Fields(track.Fields, source with { ElementId = track.Id.Value }, trackPath + "/fields", uses);
                for (int clipIndex = 0; clipIndex < track.Clips.Length; clipIndex++)
                {
                    AuthoringTimelineClipDocument clip = track.Clips.Span[clipIndex];
                    Fields(clip.Fields, source with { ElementId = clip.Id.Value }, trackPath + "/clips/" + clipIndex + "/fields", uses);
                }
                for (int markerIndex = 0; markerIndex < track.Markers.Length; markerIndex++)
                {
                    AuthoringTimelineMarkerDocument marker = track.Markers.Span[markerIndex];
                    Fields(marker.Fields, source with { ElementId = marker.Id.Value }, trackPath + "/markers/" + markerIndex + "/fields", uses);
                }
            }
            for (int index = 0; index < timeline.Bindings.Length; index++)
            {
                AuthoringTimelineBindingDocument binding = timeline.Bindings.Span[index];
                Fields(binding.Fields, source with { ElementId = binding.Id.Value }, "/bindings/" + index + "/fields", uses);
            }
        }
        return uses.ToArray();
    }

    static void Fields(ReadOnlyMemory<AuthoringNamedValue> fields, AuthoringReferenceSource source,
        string pointer, List<AuthoringReferenceUse> uses)
    {
        foreach (AuthoringNamedValue field in fields.Span)
            Value(field.Value, source with { Pointer = pointer + "/" + field.FieldId.Value.Replace("~", "~0").Replace("/", "~1") }, uses);
    }

    static void Value(AuthoringValue value, AuthoringReferenceSource source, List<AuthoringReferenceUse> uses)
    {
        switch (value)
        {
            case AuthoringReferenceValue reference:
                Reference(source, "value", value.TypeId, reference.Target, uses);
                break;
            case AuthoringObjectValue composite:
                Fields(composite.Members, source, source.Pointer + "/members", uses);
                break;
            case AuthoringSequenceValue sequence:
                for (int index = 0; index < sequence.Items.Length; index++)
                    Value(sequence.Items.Span[index], source with { Pointer = source.Pointer + "/items/" + index }, uses);
                break;
        }
    }

    static void Reference(AuthoringReferenceSource source, string relation, string? valueType,
        AuthoringContentReference target, List<AuthoringReferenceUse> uses) =>
        uses.Add(new(source, new(target.RootId.Value, target.DocumentId.Value, target.ElementId.Value), relation, valueType));

    static void RequirePage(int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > 200)
            throw new ArgumentException("文档引用查询要求 offset ≥ 0，limit 在 1～200 之间。");
    }
}
}
