using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

public sealed record SkillAuthoringFile([property: JsonRequired] string Path,
    [property: JsonRequired] string Content);
public sealed record SkillAuthoringFileRevision(string Path, string Revision);
public sealed record SkillAuthoringSnapshot(string Skill, string Revision, SkillAuthoringFile[] Files);
public sealed record SkillAuthoringChanges(string Skill, string Revision, bool Changed,
    SkillAuthoringFileRevision[] Files);
public sealed record SkillAuthoringWrite([property: JsonRequired] SkillAuthoringFile[] Files,
    [property: JsonRequired] string[] RemovedFiles);
public sealed record SkillAuthoringEditingView(
    [property: JsonRequired] IReadOnlyDictionary<string, SkillAuthoringWrite> Skills,
    string? PreviewDefinitionGuid = null);
public sealed record SkillAuthoringTypeQuery([property: JsonRequired] string Type,
    JsonElement? Configuration = null, string? GraphId = null, int Offset = 0, int Limit = 128,
    SkillAuthoringEditingView? EditingView = null);
public sealed record SkillAuthoringPageQuery(int Offset = 0, int Limit = 128, string? GraphId = null,
    SkillAuthoringEditingView? EditingView = null);
public sealed record SkillAuthoringNodeQuery([property: JsonRequired] string GraphId,
    [property: JsonRequired] string NodeId, int Offset = 0, int Limit = 128,
    SkillAuthoringEditingView? EditingView = null);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SkillAuthoringProfileEntryTarget), "ProfileEntry")]
[JsonDerivedType(typeof(SkillAuthoringNodeTarget), "Node")]
public abstract record SkillAuthoringReferenceTarget;
public sealed record SkillAuthoringProfileEntryTarget([property: JsonRequired] string CarrierIdentity,
    [property: JsonRequired] string ProfileSource, [property: JsonRequired] string Domain,
    [property: JsonRequired] string EntryKind, [property: JsonRequired] string Key,
    string? ResourceIdentity = null) : SkillAuthoringReferenceTarget;
public sealed record SkillAuthoringNodeTarget([property: JsonRequired] string GraphId,
    [property: JsonRequired] string NodeId) : SkillAuthoringReferenceTarget;
public sealed record SkillAuthoringReferenceQuery(string? Identity = null,
    int Offset = 0, int Limit = 128, SkillAuthoringEditingView? EditingView = null,
    SkillAuthoringReferenceTarget? Target = null);
