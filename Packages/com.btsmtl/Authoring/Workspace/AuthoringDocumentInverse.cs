#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Documents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{
public static class AuthoringDocumentInverse
{
    public static IReadOnlyList<AuthoringDocumentText> Create(AuthoringRootFiles before, AuthoringRootFiles after,
        IReadOnlyList<GraphAuthoringElementId> changedDocuments, AuthoringRootFiles current)
    {
        string directory = Path.GetDirectoryName(current.Files[0].Path)!;
        var texts = current.Files.ToDictionary(file => file.Path, file => file.Text, StringComparer.OrdinalIgnoreCase);
        var original = Sources(before);
        var applied = Sources(after);
        var actual = Sources(current);
        string rootId = current.Root.Id.Value;
        JToken root = Merge(rootId, rootId, "", Parse(after.Files[0].Text),
            Parse(before.Files[0].Text), Parse(current.Files[0].Text))!;
        string rootText = root.ToString(Formatting.Indented) + "\n";
        AuthoringRootDocument manifest = AuthoringDocumentJson.ReadRoot(rootText, current.Files[0].Path);
        var destinations = manifest.Files.ToArray().ToDictionary(file => file.Id,
            file => AuthoringWorkspaceDirectory.Resolve(directory, file.Path));
        var pending = new List<(string Id, string? CurrentPath, string? TargetPath, JToken? Value)>();
        foreach (GraphAuthoringElementId id in changedDocuments.Distinct().Where(id => !id.Equals(current.Root.Id)))
        {
            original.TryGetValue(id, out AuthoringSourceFile? prior);
            applied.TryGetValue(id, out AuthoringSourceFile? saved);
            actual.TryGetValue(id, out AuthoringSourceFile? now);
            JToken? inverse = Merge(id.Value, id.Value, "", saved == null ? null : Parse(saved.Text),
                prior == null ? null : Parse(prior.Text), now == null ? null : Parse(now.Text));
            pending.Add((id.Value, now?.Path, inverse == null ? null : destinations[id], inverse));
        }
        foreach (var change in pending)
            if (change.CurrentPath != null) texts.Remove(change.CurrentPath);
        foreach (var change in pending.Where(change => change.Value != null))
        {
            if (texts.TryGetValue(change.TargetPath!, out string? occupied))
                throw Conflict(change.Id, change.Id, "/file", null, Parse(occupied));
            texts.Add(change.TargetPath!, change.Value!.ToString(Formatting.Indented) + "\n");
        }
        texts[current.Files[0].Path] = rootText;
        return texts.Select(file => new AuthoringDocumentText(Path.GetRelativePath(directory, file.Key)
            .Replace('\\', '/'), file.Value)).ToArray();
    }

    static IReadOnlyDictionary<GraphAuthoringElementId, AuthoringSourceFile> Sources(AuthoringRootFiles source)
    {
        string directory = Path.GetDirectoryName(source.Files[0].Path)!;
        var byPath = source.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        return source.Root.Files.ToArray().ToDictionary(file => file.Id,
            file => byPath[AuthoringWorkspaceDirectory.Resolve(directory, file.Path)]);
    }

    static JToken? Merge(string document, string element, string path, JToken? expected, JToken? target, JToken? current)
    {
        if (JToken.DeepEquals(expected, target)) return current?.DeepClone();
        if (expected is JObject before && target is JObject after && current is JObject actual)
        {
            foreach (string key in new[] { "id", "type", "kind", "formatVersion", "definitionVersion", "domain", "role" })
                if (before[key] != null && !JToken.DeepEquals(before[key], actual[key]))
                    throw Conflict(document, element, path + "/" + key, before[key], actual[key]);
            var result = (JObject)actual.DeepClone();
            foreach (string key in before.Properties().Select(value => value.Name)
                .Union(after.Properties().Select(value => value.Name), StringComparer.Ordinal))
            {
                JToken? inverse = Merge(document, element, path + "/" + key, before[key], after[key], actual[key]);
                if (inverse == null) result.Property(key)?.Remove();
                else result[key] = inverse;
            }
            return result;
        }
        if (expected is JArray beforeArray && target is JArray afterArray && current is JArray actualArray &&
            beforeArray.Concat(afterArray).Concat(actualArray).All(value => value is JObject && value["id"]?.Type == JTokenType.String))
            return MergeElements(document, path, beforeArray, afterArray, actualArray);
        if (!JToken.DeepEquals(expected, current)) throw Conflict(document, element, path, expected, current);
        return target?.DeepClone();
    }

    static JArray MergeElements(string document, string path, JArray expected, JArray target, JArray current)
    {
        var before = expected.ToDictionary(Key, StringComparer.Ordinal);
        var after = target.ToDictionary(Key, StringComparer.Ordinal);
        var actual = current.ToDictionary(Key, StringComparer.Ordinal);
        var result = current.Select(value => value.DeepClone()).ToList();
        foreach (string id in before.Keys.Union(after.Keys, StringComparer.Ordinal))
        {
            before.TryGetValue(id, out var old);
            after.TryGetValue(id, out var wanted);
            actual.TryGetValue(id, out var now);
            JToken? inverse = Merge(document, id, path + "/" + id, old, wanted, now);
            int index = result.FindIndex(value => Key(value) == id);
            if (inverse == null)
            { if (index >= 0) result.RemoveAt(index); }
            else if (index >= 0) result[index] = inverse;
            else
            {
                string[] order = target.Select(Key).ToArray();
                int insertion = result.Count;
                for (int next = Array.IndexOf(order, id) + 1; next < order.Length; next++)
                {
                    int anchor = result.FindIndex(value => Key(value) == order[next]);
                    if (anchor >= 0) { insertion = anchor; break; }
                }
                result.Insert(insertion, inverse);
            }
        }
        string[] retained = expected.Select(Key).Where(after.ContainsKey).ToArray();
        string[] desired = target.Select(Key).Where(before.ContainsKey).ToArray();
        if (!retained.SequenceEqual(desired, StringComparer.Ordinal))
        {
            if (!current.Select(Key).Where(id => before.ContainsKey(id) && after.ContainsKey(id))
                .SequenceEqual(retained, StringComparer.Ordinal))
                throw Conflict(document, document, path + "/order", expected, current);
            var positions = result.Select((value, index) => (Id: Key(value), Index: index))
                .Where(value => after.ContainsKey(value.Id)).Select(value => value.Index).ToArray();
            var values = result.ToDictionary(Key, StringComparer.Ordinal);
            string[] order = target.Select(Key).ToArray();
            for (int index = 0; index < positions.Length; index++) result[positions[index]] = values[order[index]];
        }
        return new JArray(result);
    }

    static string Key(JToken value) => value.Value<string>("id")!;
    static JToken Parse(string value) => AuthoringDocumentJson.ReadObject(value);
    static AuthoringUndoConflictException Conflict(string document, string element, string path,
        JToken? expected, JToken? current) => new(document, element, path,
            expected?.ToString(Formatting.None) ?? "absent", current?.ToString(Formatting.None) ?? "absent");
}
}
