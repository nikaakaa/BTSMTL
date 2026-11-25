using BTSMTL.Authoring.Graph;
using BTSMTL.Timeline;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public static class AuthoringTimelineTemplates
{
    public static JObject Track(JObject element, TimelineTrackContract contract)
    {
        element["structureMembers"] = new JArray(TimelineJson.TrackStructureMembers);
        element["contract"] = new JObject
        {
            ["overlap"] = contract.OverlapPolicy.ToString(), ["domain"] = contract.ExecutionPolicy.Domain.ToString(),
            ["domains"] = Domains(contract.SupportsExecutionDomain), ["clipKinds"] = new JArray(contract.AllowedClipKinds)
        };
        return element;
    }

    public static JObject Clip(JObject element, Clip clip, TimelineClipDefinition contract, string? referenceCurves)
    {
        Type type = clip.GetType();
        element["structureMembers"] = new JArray(TimelineJson.ClipStructureMembers);
        element["contract"] = new JObject
        {
            ["trackKind"] = contract.TrackKind, ["domains"] = Domains(contract.SupportsExecutionDomain)
        };
        element["curves"] = new JArray(clip is SampledClip ? TimelineSampledCurveChannel.All.Select(channel => new JObject
        {
            ["member"] = channel.Member, ["name"] = channel.DisplayName, ["role"] = channel.Role.ToString(),
            ["color"] = $"rgb({channel.Red} {channel.Green} {channel.Blue})",
            ["min"] = channel.ValueDomain.Minimum, ["max"] = channel.ValueDomain.Maximum,
            ["defaultStart"] = channel.DefaultStart, ["defaultEnd"] = channel.DefaultEnd
        }) : Enumerable.Empty<JObject>());
        element["tickQuantized"] = (clip.Capabilities & ClipCapabilities.TickQuantized) != 0;
        element["clipInable"] = (clip.Capabilities & ClipCapabilities.ClipInable) != 0;
        element["referenceCurves"] = referenceCurves;
        GraphAuthoringMember? source = GraphAuthoringMemberBindings.Contract(type).Fields.SingleOrDefault(field =>
            field.Annotation<TimelineAuthoringPropertyAttribute>() is { IsSourceAsset: true, SourceAssetKind: not null });
        element["sourceAsset"] = source == null ? null : new JObject
        {
            ["serializedMemberPath"] = new JArray(source.SerializedMemberPath),
            ["choiceKind"] = source.Annotation<TimelineAuthoringPropertyAttribute>().SourceAssetKind
        };
        return element;
    }

    static JArray Domains(Func<TimelineExecutionDomain, bool> supported) =>
        new(Enum.GetValues<TimelineExecutionDomain>().Where(supported).Select(domain => domain.ToString()));
}
