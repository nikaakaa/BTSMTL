namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationFlowEdge
    {
        public OrchestrationFlowEdge(int identityId, int sourceOperationIndex,
            int targetOperationIndex, int sourcePortId, int targetPortId,
            OrchestrationFlowKind kind, int order, int priority,
            OrchestrationAbortPolicy abortPolicy, int conditionOperationIndex)
        {
            IdentityId = identityId;
            SourceOperationIndex = sourceOperationIndex;
            TargetOperationIndex = targetOperationIndex;
            SourcePortId = sourcePortId;
            TargetPortId = targetPortId;
            Kind = kind;
            Order = order;
            Priority = priority;
            AbortPolicy = abortPolicy;
            ConditionOperationIndex = conditionOperationIndex;
        }

        public int IdentityId { get; }
        public int SourceOperationIndex { get; }
        public int TargetOperationIndex { get; }
        public int SourcePortId { get; }
        public int TargetPortId { get; }
        public OrchestrationFlowKind Kind { get; }
        public int Order { get; }
        public int Priority { get; }
        public OrchestrationAbortPolicy AbortPolicy { get; }
        public int ConditionOperationIndex { get; }
        public bool HasCondition => ConditionOperationIndex >= 0;

        public static int CompareOrder(in OrchestrationFlowEdge left, in OrchestrationFlowEdge right)
        {
            int comparison = left.Kind == OrchestrationFlowKind.Transition
                ? left.Priority.CompareTo(right.Priority) : 0;
            if (comparison != 0)
                return comparison;
            comparison = left.Order.CompareTo(right.Order);
            if (comparison != 0)
                return comparison;
            comparison = left.SourcePortId.CompareTo(right.SourcePortId);
            return comparison != 0 ? comparison : left.IdentityId.CompareTo(right.IdentityId);
        }
    }

    public enum OrchestrationValueInputSource : byte
    {
        Operation = 1,
        Constant = 2,
        StateCompletionTransition = 3,
        Blackboard = 4
    }

    public readonly struct OrchestrationValueInputBinding
    {
        public OrchestrationValueInputBinding(int targetPortIndex,
            OrchestrationValueKind valueKind, OrchestrationValueInputSource source,
            int sourceIndex, int sourceOutputPortIndex)
        {
            TargetPortIndex = targetPortIndex;
            ValueKind = valueKind;
            Source = source;
            SourceIndex = sourceIndex;
            SourceOutputPortIndex = sourceOutputPortIndex;
        }

        public int TargetPortIndex { get; }
        public OrchestrationValueKind ValueKind { get; }
        public OrchestrationValueInputSource Source { get; }
        public int SourceIndex { get; }
        public int SourceOutputPortIndex { get; }
    }
}
