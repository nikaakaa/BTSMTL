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
            m_Kind = (int)kind;
            Order = order;
            Priority = priority;
            m_AbortPolicy = (int)abortPolicy;
            ConditionOperationIndex = conditionOperationIndex;
        }

        public int IdentityId { get; }
        public int SourceOperationIndex { get; }
        public int TargetOperationIndex { get; }
        public int SourcePortId { get; }
        public int TargetPortId { get; }
        readonly int m_Kind;
        public OrchestrationFlowKind Kind => (OrchestrationFlowKind)m_Kind;
        public int Order { get; }
        public int Priority { get; }
        readonly int m_AbortPolicy;
        public OrchestrationAbortPolicy AbortPolicy => (OrchestrationAbortPolicy)m_AbortPolicy;
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
            m_ValueKind = (ushort)valueKind;
            m_Source = (ushort)source;
            SourceIndex = sourceIndex;
            SourceOutputPortIndex = sourceOutputPortIndex;
        }

        public int TargetPortIndex { get; }
        readonly ushort m_ValueKind;
        readonly ushort m_Source;
        public OrchestrationValueKind ValueKind => (OrchestrationValueKind)m_ValueKind;
        public OrchestrationValueInputSource Source => (OrchestrationValueInputSource)m_Source;
        public int SourceIndex { get; }
        public int SourceOutputPortIndex { get; }
    }
}
