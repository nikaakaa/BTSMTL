#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Graph;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{
static class AuthoringDocumentInverse
{
    internal static IReadOnlyList<AuthoringDocumentText> Create(AuthoringDocumentCandidate applied, AuthoringRootFiles current)
    {
        string directory = Path.GetDirectoryName(current.Files[0].Path)!;
        var texts = current.Files.ToDictionary(file => file.Path, file => file.Text, StringComparer.OrdinalIgnoreCase);
        foreach (var change in applied.Changes)
        {
            string documentId = applied.Differences.Single(file => file.Path == change.Path).DocumentId.Value;
            JToken? expected = change.After == null ? null : Parse(change.After);
            JToken? target = change.Before == null ? null : Parse(change.Before);
            JToken? actual = texts.TryGetValue(change.Path, out var text) ? Parse(text) : null;
            JToken? inverse = Merge(documentId, documentId, "", expected, target, actual);
            if (inverse == null) texts.Remove(change.Path);
            else texts[change.Path] = inverse.ToString(Formatting.Indented) + "\n";
        }
        return texts.Select(file => new AuthoringDocumentText(Path.GetRelativePath(directory, file.Key)
            .Replace('\\', '/'), file.Value)).ToArray();
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
            if (!current.Select(Key).Where(before.ContainsKey).SequenceEqual(retained, StringComparer.Ordinal))
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
    static JToken Parse(byte[] value) => Parse(new System.Text.UTF8Encoding(false, true).GetString(
        value.AsSpan(value.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble) ? 3 : 0)));
    static JToken Parse(string value)
    {
        using var reader = new JsonTextReader(new StringReader(value)) { DateParseHandling = DateParseHandling.None };
        return JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
    }
    static AuthoringUndoConflictException Conflict(string document, string element, string path,
        JToken? expected, JToken? current) => new(document, element, path,
            expected?.ToString(Formatting.None) ?? "absent", current?.ToString(Formatting.None) ?? "absent");
}
}
