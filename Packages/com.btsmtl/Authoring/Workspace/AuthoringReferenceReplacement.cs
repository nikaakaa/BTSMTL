using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{
    public sealed class AuthoringResourceReference
    {
        public AuthoringResourceReference(string domain, string value, string context, string pointer)
        { Domain = domain; Value = value; Context = context; Pointer = pointer; }
        public string Domain { get; }
        public string Value { get; }
        public string Context { get; }
        public string Pointer { get; }
    }

    public sealed class AuthoringReferenceDocument
    {
        public string Path { get; set; }
        public string Text { get; set; }
        public string Owner { get; set; }
        public string[] Identities { get; set; }
        public string[] Dependencies { get; set; }
        public IReadOnlyList<AuthoringResourceReference> References { get; set; }
    }

    public sealed class AuthoringReferenceReplacementRequest
    {
        public string Domain { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Context { get; set; }
    }

    public sealed class AuthoringReferenceReplacementPlan
    {
        internal AuthoringReferenceReplacementPlan(AuthoringReferenceReplacementRequest request,
            IReadOnlyList<AuthoringFileChange> changes, IReadOnlyList<AuthoringReadRevision> reads,
            JObject[] files, string[] owners)
        {
            Request = request;
            Changes = changes;
            Reads = reads;
            Files = files;
            AffectedOwners = owners;
            Revision = AuthoringCommits.Revision(Describe(false).ToString(Formatting.None));
        }

        public AuthoringReferenceReplacementRequest Request { get; }
        public string Revision { get; }
        public IReadOnlyList<AuthoringFileChange> Changes { get; }
        public IReadOnlyList<AuthoringReadRevision> Reads { get; }
        public IReadOnlyList<JObject> Files { get; }
        public IReadOnlyList<string> AffectedOwners { get; }

        public void Commit(AuthoringCommits commits, string expectedRevision)
        {
            if (expectedRevision != Revision)
                throw new InvalidOperationException($"引用替换目标或读取来源已变化，期望修订 {expectedRevision}、实际 {Revision}，请重新准备。");
            commits.Commit(Changes, Reads);
        }

        public JObject Describe(bool saved) => new JObject
        {
            ["success"] = true, ["saved"] = saved, ["revision"] = Revision,
            ["domain"] = Request.Domain, ["oldValue"] = Request.OldValue, ["newValue"] = Request.NewValue,
            ["context"] = Request.Context, ["files"] = new JArray(Files),
            ["readFiles"] = new JArray(Reads.Select(read => new JObject { ["path"] = read.Path, ["revision"] = read.Revision })),
            ["affectedSkills"] = new JArray(AffectedOwners),
            ["authorReferenceState"] = saved ? "replaced" : "prepared",
            ["runtimeState"] = AffectedOwners.Count == 0 ? "not-required" : "pending"
        };
    }

    public static class AuthoringReferenceReplacement
    {
        public static AuthoringReferenceReplacementPlan Prepare(AuthoringReferenceReplacementRequest request,
            AuthoringCatalogSnapshot catalog, IReadOnlyList<AuthoringReferenceDocument> documents,
            IReadOnlyDictionary<string, string> schemaInputs, bool adapterReportedMove = false)
        {
            if (!adapterReportedMove && !catalog.Entries.Any(item => item.Domain == request.Domain && item.Value == request.NewValue &&
                (request.Context == null || item.Context == request.Context || item.Context == "")))
                throw new InvalidDataException($"领域 '{request.Domain}' 的新引用 '{request.NewValue}' 不在适配者目录中。");
            var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var input in schemaInputs.Concat(catalog.Inputs)) inputs[input.Key] = input.Value;
            var changes = new List<AuthoringFileChange>();
            var files = new List<JObject>();
            var affected = new HashSet<string>(StringComparer.Ordinal);
            foreach (AuthoringReferenceDocument document in documents.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                inputs.Add(document.Path, document.Text);
                AuthoringResourceReference[] references = document.References.Where(reference =>
                    reference.Domain == request.Domain && reference.Value == request.OldValue &&
                    (request.Context == null || reference.Context == request.Context)).ToArray();
                if (references.Length == 0) continue;
                if (!adapterReportedMove)
                    foreach (AuthoringResourceReference reference in references)
                        catalog.Require(request.Domain, request.NewValue, reference.Context, document.Path, reference.Pointer);
                string after = AuthoringJsonPointer.ReplaceStrings(document.Text,
                    references.Select(reference => reference.Pointer), request.NewValue);
                string readRevision = AuthoringCommits.Revision(document.Text);
                changes.Add(new AuthoringFileChange(document.Path, after, readRevision));
                files.Add(new JObject
                {
                    ["path"] = document.Path, ["members"] = new JArray(references.Select(reference => reference.Pointer).Distinct(StringComparer.Ordinal)),
                    ["readRevision"] = readRevision, ["writeRevision"] = AuthoringCommits.Revision(after)
                });
                affected.Add(document.Owner);
            }
            var owners = documents.SelectMany(document => document.Identities.Select(identity => (identity, document.Owner)))
                .GroupBy(item => item.identity, StringComparer.Ordinal).ToDictionary(group => group.Key,
                    group => group.Select(item => item.Owner).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            bool expanded;
            do
            {
                expanded = false;
                foreach (AuthoringReferenceDocument document in documents)
                    if (document.Dependencies.Any(identity => owners.TryGetValue(identity, out string[] dependencies) && dependencies.Any(affected.Contains)))
                        expanded |= affected.Add(document.Owner);
            } while (expanded);
            return new AuthoringReferenceReplacementPlan(request, changes,
                inputs.Select(input => new AuthoringReadRevision(input.Key, AuthoringCommits.Revision(input.Value))).ToArray(),
                files.ToArray(), affected.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }
    }
}
