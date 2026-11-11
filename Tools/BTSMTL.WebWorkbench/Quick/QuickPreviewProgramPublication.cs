using BTSMTL.Compilation.Analysis;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

sealed record QuickPreviewProgramPublication(JArray Programs, IReadOnlySet<string> RuntimeConditions)
{
    public static QuickPreviewProgramPublication Build(IPreviewQuickCompiledContent content,
        IReadOnlyDictionary<string, OrchestrationStaticQuery> queries)
    {
        var conditions = new HashSet<string>(StringComparer.Ordinal);
        var programs = new JArray();
        foreach (PreviewQuickProgramInput input in content.Programs)
        {
            OrchestrationStaticQuery query = queries[input.GraphId];
            var program = (JObject)input.Parameters.DeepClone();
            program["programGraphId"] = input.GraphId;
            program["invocationConditions"] = new JArray(input.Invocations.Select(invocation => new JObject
            {
                ["entryOperation"] = invocation.EntryOperation, ["operation"] = invocation.TargetOperation,
                ["condition"] = Condition(query.DescribeInvocationPath(invocation.EntryOperation, invocation.TargetOperation), conditions)
            }));
            program["valueConditions"] = new JArray(input.ConditionOperations.Select(operation => new JObject
            {
                ["operation"] = operation, ["condition"] = Condition(query.DescribeCondition(operation), conditions)
            }));
            program["states"] = new JArray(query.States.Select(state => new JObject
            {
                ["operation"] = state.OperationIndex, ["timelineOperation"] = state.TimelineOperationIndex,
                ["timelineDeterminesRootCompletion"] = state.TimelineDeterminesRootCompletion,
                ["clips"] = state.Timeline == null ? null : new JArray(state.Timeline.Clips.Where(clip => clip.ProvidedStateKeys.Count != 0)
                    .Select(clip => new JObject { ["start"] = clip.Start.RawValue, ["end"] = clip.End.RawValue, ["providedStateKeys"] = new JArray(clip.ProvidedStateKeys) }))
            }));
            programs.Add(program);
        }
        return new(programs, conditions);
    }

    static JObject Condition(OrchestrationStaticCondition condition, HashSet<string> conditions)
    {
        var value = new JObject { ["kind"] = condition.Kind.ToString() };
        switch (condition.Kind)
        {
            case OrchestrationStaticConditionKind.Constant: value["value"] = condition.ConstantValue; break;
            case OrchestrationStaticConditionKind.All:
            case OrchestrationStaticConditionKind.Any:
            case OrchestrationStaticConditionKind.Not:
                value["children"] = new JArray(condition.Children.Select(child => Condition(child, conditions)));
                break;
            case OrchestrationStaticConditionKind.Read:
                var read = condition.Read;
                if (read.Source == OrchestrationStaticReadSource.RuntimeData) conditions.Add(read.Key);
                value["read"] = new JObject { ["operationIndex"] = read.OperationIndex, ["source"] = read.Source.ToString(),
                    ["key"] = read.Key, ["undeterminable"] = read.Undeterminable, ["label"] = read.Label };
                break;
            case OrchestrationStaticConditionKind.TimelineTime:
                value["comparison"] = condition.Comparison.ToString(); value["thresholdRaw"] = condition.Threshold.RawValue;
                break;
        }
        return value;
    }
}
