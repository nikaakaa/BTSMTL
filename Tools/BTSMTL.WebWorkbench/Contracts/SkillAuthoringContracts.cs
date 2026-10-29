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
public sealed record SkillAuthoringTypeQuery([property: JsonRequired] string Type,
    JsonElement? Configuration = null, string? GraphId = null, int Offset = 0, int Limit = 128);
