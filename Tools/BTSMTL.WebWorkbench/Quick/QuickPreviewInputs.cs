using System.Text.Json;
using BTSMTL.Authoring.Workspace;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

sealed record QuickPreviewInputSchedule(PreviewQuickScheme Scheme, string Revision, double RangeEnd,
    PreviewQuickInputPoint[] Points, PreviewQuickConditionPoint[] Conditions, TimelineStaticReadIntervals Intervals,
    JObject Body, string InputSource);

sealed class QuickPreviewInputs
{
    const double DefaultRangeEnd = 10;
    static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web);
    sealed record Selection(PreviewQuickScheme Scheme, PreviewQuickInputPlan? Plan, int Index);
    readonly Selection[] m_Selections;
    readonly IPreviewQuickEnvironment m_Actor;

    public QuickPreviewInputs(IPreviewQuickCapture capture, IPreviewQuickEnvironment actor)
    {
        m_Actor = actor;
        var plans = capture.InputPlans();
        PreviewQuickInputPlan? defaultPlan = plans.SingleOrDefault(plan => plan.IsRootDefault);
        int index = 0;
        if (defaultPlan != null)
        {
            int count = defaultPlan.Value["Schemes"]!.Count();
            if (count == 0)
                defaultPlan = null;
            else
                index = Math.Clamp((int)defaultPlan.Value["Selected"]!, 0, count - 1);
        }
        var defaultInput = new PreviewQuickRootInputs();
        var rootDefault = new Selection(new(defaultInput, "根默认输入",
            Hash(capture.Identity.Revision, "rootDefaults", defaultPlan?.Revision ?? ""),
            (string?)defaultPlan?.Value["Schemes"]![index]!["InputTimeline"]), defaultPlan, index);
        m_Selections = [rootDefault, .. plans.SelectMany(plan => plan.Value["Schemes"]!.Select((scheme, ordinal) =>
        {
            string schemeId = (string)scheme["AuthoringId"]!;
            var selection = new PreviewQuickDocumentInput(plan.DocumentPath,
                plan.SavedRevision, plan.Id, schemeId);
            string revision = Hash(capture.Identity.Revision, plan.DocumentPath, plan.Id, schemeId, plan.Revision);
            return new Selection(new(selection, (string)scheme["Name"]!, revision, (string?)scheme["InputTimeline"]), plan, ordinal);
        }))];
    }

    public PreviewQuickScheme[] Schemes() => m_Selections.Select(selection => selection.Scheme).ToArray();

    public QuickPreviewInputSchedule Resolve(PreviewQuickInput request, double? rangeEnd)
    {
        // 跟随当前内容（Revision 为 null）时按文档、输入与方案身份取本次捕获的方案，保存修订以本次捕获为准。
        Selection selected = m_Selections.SingleOrDefault(selection => request.Revision == null
                ? selection.Scheme.Source is PreviewQuickDocumentInput current && request.Source is PreviewQuickDocumentInput requested
                    ? current with { SavedRevision = requested.SavedRevision } == requested
                    : selection.Scheme.Source == request.Source
                : selection.Scheme.Source == request.Source)
            ?? throw new InvalidOperationException("所选输入方案不属于当前预览根或其作者修订已变化。");
        if (request.Revision != null && request.Revision != selected.Scheme.Revision)
            throw new InvalidOperationException("所选输入方案已变化，请重新读取快预览来源。");
        if (request.Points == null && request.Conditions != null)
            throw new ArgumentException("试用条件需要和试用输入点一起提供。");
        if (rangeEnd is { } end && (!double.IsFinite(end) || end < 0))
            throw new ArgumentException("预览范围终点需要有限的非负秒数。");

        JToken? scheme = selected.Plan?.Value["Schemes"]![selected.Index];
        double range = rangeEnd is > 0 ? rangeEnd.Value : (double?)scheme?["RangeEnd"] ?? DefaultRangeEnd;
        if (!float.IsFinite((float)range) || range <= 0)
            throw new ArgumentException("预览范围终点需要大于零的有限秒数。");
        PreviewQuickInputPoint[] points;
        PreviewQuickConditionPoint[] conditions;
        if (request.Points != null)
        {
            points = request.Points.OrderBy(point => point.Time).ToArray();
            conditions = request.Conditions?.OrderBy(point => point.Time).ToArray() ?? [];
        }
        else if (selected.Plan != null)
        {
            var authored = QuickPreviewInputTimeline.Read(selected.Plan, selected.Index, m_Actor.Keys);
            points = m_Actor.Keys.ConvertKeys(authored.Keys);
            conditions = authored.Conditions;
        }
        else
        {
            points = [];
            conditions = [];
        }

        points = points.Select(point => point with { Time = (float)point.Time }).ToArray();
        conditions = conditions.Select(point => point with { Time = (float)point.Time }).ToArray();
        var descriptors = m_Actor.Inputs.ToDictionary(input => input.InputId, StringComparer.Ordinal);
        TimelineStaticInputPoint[] values = points.Select(point => Parse(point, descriptors)).ToArray();
        var conditionValues = conditions.Select(point => new TimelineStaticConditionPoint(point.Key, (float)point.Time,
            point.Satisfied, point.RestoreDefault));
        var intervals = new TimelineStaticReadIntervals(values, conditionValues,
            id => m_Actor.InputKinds.TryGetValue(id, out OrchestrationValueKind kind) ? kind : null, m_Actor.Requests, range);
        JObject body = AuthoringJsonText.Parse(JsonSerializer.Serialize(new { points, conditions }, s_Json));
        body["rangeEnd"] = range;
        body["start"] = (bool?)scheme?["HasStart"] == true ? new JObject
        {
            ["position"] = scheme!["StartPosition"]!.DeepClone(), ["yaw"] = scheme["StartYaw"]!.DeepClone()
        } : null;
        body["target"] = (bool?)scheme?["HasTarget"] == true ? new JObject
        {
            ["position"] = scheme!["TargetPosition"]!.DeepClone(), ["euler"] = scheme["TargetEuler"]!.DeepClone()
        } : null;
        string revision = Hash(selected.Scheme.Revision, AuthoringJsonText.Text(body));
        return new(selected.Scheme, revision, range, points, conditions, intervals, body,
            request.Points != null ? "trial" : "scheme:" + selected.Scheme.Name);
    }

    static string Hash(params string[] parts) => WorkbenchArtifacts.Hash(System.Text.Encoding.UTF8.GetBytes(AuthoringJsonText.Compact(new JArray(parts))));

    static TimelineStaticInputPoint Parse(PreviewQuickInputPoint point, IReadOnlyDictionary<string, GraphHostInputDescriptor> descriptors)
    {
        if (!descriptors.TryGetValue(point.InputId, out var descriptor))
            throw new ArgumentException($"预览输入 '{point.InputId}' 不属于角色的正式输入声明。");
        float time = (float)point.Time;
        return descriptor.ValueKind switch
        {
            GraphHostValueKind.Bool => new(point.InputId, time, boolean: point.Value.GetBoolean()),
            GraphHostValueKind.Float32 => new(point.InputId, time, scalar: point.Value.GetSingle()),
            GraphHostValueKind.Vector2 when point.Value.GetArrayLength() == 2 =>
                new(point.InputId, time, x: point.Value[0].GetSingle(), y: point.Value[1].GetSingle()),
            _ => throw new ArgumentException($"预览输入 '{point.InputId}' 需要 '{descriptor.ValueKind}' 的正式数值格式。")
        };
    }
}
