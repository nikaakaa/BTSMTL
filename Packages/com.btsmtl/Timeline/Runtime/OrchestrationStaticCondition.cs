using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation.Analysis
{
    public enum OrchestrationStaticReadSource : byte
    {
        ClipState = 1,
        HostInput = 2,
        RuntimeData = 3,
        StateCompletion = 4,
        TimelineTime = 5
    }

    public readonly struct OrchestrationStaticRead : IEquatable<OrchestrationStaticRead>
    {
        public OrchestrationStaticRead(int operationIndex, OrchestrationStaticReadSource source, string key,
            bool undeterminable, string label)
        {
            OperationIndex = operationIndex;
            Source = source;
            Key = key;
            Undeterminable = undeterminable;
            Label = label;
        }

        public int OperationIndex { get; }
        public OrchestrationStaticReadSource Source { get; }
        // 状态键、宿主输入身份，或运行数据的稳定描述。
        public string Key { get; }
        public bool Undeterminable { get; }
        public string Label { get; }

        public bool Equals(OrchestrationStaticRead other) => OperationIndex == other.OperationIndex &&
            Source == other.Source && string.Equals(Key, other.Key, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is OrchestrationStaticRead other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(OperationIndex, (int)Source, Key);
    }

    public enum OrchestrationStaticConditionKind : byte
    {
        Constant,
        All,
        Any,
        Not,
        Read,
        StateRootCompleted,
        // 当前 Timeline 本地时刻与常量比较：本地时刻 Comparison Threshold。
        TimelineTime
    }

    public sealed class OrchestrationStaticCondition
    {
        public OrchestrationStaticCondition(OrchestrationStaticConditionKind kind,
            IReadOnlyList<OrchestrationStaticCondition> children, OrchestrationStaticRead read = default,
            bool constantValue = false, OrchestrationComparisonKind comparison = default,
            OrchestrationTime threshold = default)
        {
            Kind = kind;
            Children = children;
            Read = read;
            ConstantValue = constantValue;
            Comparison = comparison;
            Threshold = threshold;
        }

        public OrchestrationComparisonKind Comparison { get; }
        public OrchestrationTime Threshold { get; }

        public OrchestrationStaticConditionKind Kind { get; }
        public IReadOnlyList<OrchestrationStaticCondition> Children { get; }
        public OrchestrationStaticRead Read { get; }
        public bool ConstantValue { get; }

        public static OrchestrationStaticCondition Constant(bool value) =>
            new(OrchestrationStaticConditionKind.Constant, Array.Empty<OrchestrationStaticCondition>(), constantValue: value);

        public static OrchestrationStaticCondition All(params OrchestrationStaticCondition[] children) => Combine(true, children);
        public static OrchestrationStaticCondition Any(params OrchestrationStaticCondition[] children) => Combine(false, children);
        public static OrchestrationStaticCondition Not(OrchestrationStaticCondition condition) => Negate(condition);
        public static OrchestrationStaticCondition StateKey(int operationIndex, string key, string label) =>
            new(OrchestrationStaticConditionKind.Read, Array.Empty<OrchestrationStaticCondition>(),
                new OrchestrationStaticRead(operationIndex, OrchestrationStaticReadSource.ClipState, key, false, label));

        // 宿主声明的谓词可直接由宿主输入推出：按输入方案中该输入的成立区间求值。
        public static OrchestrationStaticCondition HostInput(int operationIndex, string inputId) =>
            new(OrchestrationStaticConditionKind.Read, Array.Empty<OrchestrationStaticCondition>(),
                new OrchestrationStaticRead(operationIndex, OrchestrationStaticReadSource.HostInput, inputId, false, inputId));

        // 宿主声明了含义的运行数据：同一含义键在不同节点上是同一条件。
        public static OrchestrationStaticCondition RuntimeData(int operationIndex, string key, string label) =>
            new(OrchestrationStaticConditionKind.Read, Array.Empty<OrchestrationStaticCondition>(),
                new OrchestrationStaticRead(operationIndex, OrchestrationStaticReadSource.RuntimeData, key, false, label));

        internal static OrchestrationStaticCondition Combine(bool all, params OrchestrationStaticCondition[] children) =>
            Combine(all, (IReadOnlyList<OrchestrationStaticCondition>)children);

        internal static OrchestrationStaticCondition Combine(bool all, IReadOnlyList<OrchestrationStaticCondition> children)
        {
            var result = new List<OrchestrationStaticCondition>();
            foreach (OrchestrationStaticCondition child in children)
            {
                if (child.Kind == OrchestrationStaticConditionKind.Constant)
                {
                    if (child.ConstantValue != all)
                        return Constant(!all);
                    continue;
                }
                result.Add(child);
            }
            return result.Count == 0 ? Constant(all) : result.Count == 1 ? result[0] :
                new OrchestrationStaticCondition(all ? OrchestrationStaticConditionKind.All : OrchestrationStaticConditionKind.Any, result);
        }

        internal static OrchestrationStaticCondition Negate(OrchestrationStaticCondition condition) =>
            condition.Kind == OrchestrationStaticConditionKind.Constant ? Constant(!condition.ConstantValue) :
            condition.Kind == OrchestrationStaticConditionKind.Not ? condition.Children[0] :
            new OrchestrationStaticCondition(OrchestrationStaticConditionKind.Not, new[] { condition });
    }
}
