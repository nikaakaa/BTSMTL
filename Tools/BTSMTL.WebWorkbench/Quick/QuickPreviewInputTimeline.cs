using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public sealed record PreviewQuickTimelineInputs(PreviewQuickKeyWrite[] Keys, PreviewQuickConditionPoint[] Conditions);

public static class QuickPreviewInputTimeline
{
    public static PreviewQuickTimelineInputs Read(PreviewQuickInputPlan source, int schemeIndex, IPreviewInputKeyAdapter keys)
    {
        string timelineId = (string)source.Value["Schemes"]![schemeIndex]!["InputTimeline"]!;
        var writes = new List<PreviewQuickKeyWrite>();
        var conditions = new List<PreviewQuickConditionPoint>();
        foreach (JToken track in source.Timeline(timelineId)["m_Tracks"]!)
        {
            if ((bool)track["m_PersistentMuted"]!) continue;
            foreach (JToken clip in track["m_Clips"]!)
                Collect((string)clip["m_AssetTree"]!, "TimelineBody", Seconds(clip["m_StartTimeFPRawValue"]!),
                    Seconds(clip["m_EndTimeFPRawValue"]!), $"{source.DocumentPath} / {track["Name"]} / Clip {clip["m_AuthoringId"]}");
            foreach (JToken marker in track["m_Markers"]!)
                Collect((string)marker["m_Graph"]!, "TimelineTrigger", Seconds(marker["m_TimeFPRawValue"]!),
                    Seconds(marker["m_TimeFPRawValue"]!), $"{source.DocumentPath} / {track["Name"]} / Marker {marker["m_AuthoringId"]}");
        }
        writes.Sort((left, right) => left.Time.CompareTo(right.Time));
        for (int i = 0; i < writes.Count; i++)
            for (int j = i + 1; j < writes.Count && writes[j].Time == writes[i].Time; j++)
                if (writes[j].Key == writes[i].Key && !PlayerInputPreparation.Same(writes[i].State, writes[j].State))
                    throw new InvalidDataException($"按键 '{writes[i].Key}' 在 {writes[i].Time:0.###} 秒同时写入不同状态：{writes[i].Element} 与 {writes[j].Element}。");
        conditions.Sort((left, right) => left.Time.CompareTo(right.Time));
        for (int i = 0; i < conditions.Count; i++)
            for (int j = i + 1; j < conditions.Count && conditions[j].Time == conditions[i].Time; j++)
                if (conditions[j].Key == conditions[i].Key && (conditions[j].RestoreDefault != conditions[i].RestoreDefault ||
                        !conditions[i].RestoreDefault && conditions[j].Satisfied != conditions[i].Satisfied))
                    throw new InvalidDataException($"条件 '{conditions[i].Key}' 在 {conditions[i].Time:0.###} 秒同时写入不同状态：{conditions[i].Element} 与 {conditions[j].Element}。");
        return new(writes.ToArray(), conditions.ToArray());

        void Collect(string graphId, string role, float start, float end, string element)
        {
            JObject graph = source.Graph(graphId);
            if ((string)graph["Role"]! != role)
                throw new InvalidDataException($"{element} 引用的图 '{graph["name"]}' 不是 {role}。");
            var nodes = graph["allNodes"]!.ToDictionary(node => (string)node["UID"]!, StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken node in graph["allNodes"]!)
            {
                float? time = (string)node["type"]! switch
                {
                    "BtsmtlSkillTimelineEnableFlowNode" or "BtsmtlSkillRootFlowNode" => start,
                    "BtsmtlSkillTimelineDisableFlowNode" or "BtsmtlSkillTimelineDestroyFlowNode" => end,
                    _ => null
                };
                if (time is not { } at) continue;
                var pending = new Stack<JToken>();
                pending.Push(node);
                while (pending.Count != 0)
                    foreach (JToken edge in pending.Pop()["outConnections"]!)
                    {
                        string targetId = (string)edge["targetNode"]!;
                        if (!visited.Add(targetId)) continue;
                        JObject target = (JObject)nodes[targetId];
                        string type = (string)target["type"]!;
                        string location = $"{element} / 图 {graphId} / 节点 {targetId}";
                        if (type == BtsmtlPreviewConditionNode.StoredType)
                        {
                            if (!source.AllowsConditions)
                                throw new InvalidDataException($"{location} 使用预览条件；场景输入方案只能写预览按键。");
                            conditions.Add(new((string)target["m_Condition"]!, at, (bool)target["m_Satisfied"]!,
                                (bool)target["m_RestoreDefault"]!, location));
                        }
                        else if (keys.ReadKey(target, at, location) is { } write) writes.Add(write);
                        else throw new InvalidDataException($"{location} 回调下只能挂预览按键或预览条件，发现 '{type}'。");
                        pending.Push(target);
                    }
            }
        }
    }
    static float Seconds(JToken raw) => OrchestrationTime.FromRaw((long)raw).AsSingle;
}
