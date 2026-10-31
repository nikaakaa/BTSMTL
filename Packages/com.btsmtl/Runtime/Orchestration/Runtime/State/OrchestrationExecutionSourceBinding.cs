using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExecutionSourceBindingView
    {
        readonly ReadOnlySpan<ulong> m_GraphMembers;
        readonly int m_OperationCount;

        internal OrchestrationExecutionSourceBindingView(int sourceOperationIndex, int sourceStateOperationIndex,
            int sourceStateRootOperationIndex, int sourceStateRootOwnerOperationIndex, bool sameProgram,
            int operationCount, ReadOnlySpan<ulong> graphMembers)
        {
            HasSource = true;
            SourceOperationIndex = sourceOperationIndex;
            SourceStateOperationIndex = sourceStateOperationIndex;
            SourceStateRootOperationIndex = sourceStateRootOperationIndex;
            SourceStateRootOwnerOperationIndex = sourceStateRootOwnerOperationIndex;
            SameProgram = sameProgram;
            m_OperationCount = operationCount;
            m_GraphMembers = graphMembers;
        }

        public bool HasSource { get; }
        public int SourceOperationIndex { get; }
        public int SourceStateOperationIndex { get; }
        public int SourceStateRootOperationIndex { get; }
        public int SourceStateRootOwnerOperationIndex { get; }
        public bool SameProgram { get; }

        internal bool ContainsSourceState<TValue>(in OrchestrationProgramView<TValue> program, int stateOperationIndex)
            where TValue : unmanaged
        {
            if (!SameProgram)
                return false;
            for (int state = SourceStateOperationIndex; state >= 0;
                state = program.Operation(state).ParentStateOperationIndex)
                if (state == stateOperationIndex)
                    return true;
            return false;
        }

        internal bool ContainsSourceGraph(int graphOwnerOperationIndex, int sourceOperationIndex)
        {
            if (!SameProgram)
                return false;
            int bit = graphOwnerOperationIndex * m_OperationCount + sourceOperationIndex;
            return (m_GraphMembers[bit >> 6] & (1UL << (bit & 63))) != 0;
        }
    }

    public sealed class OrchestrationExecutionSourceBinding<TValue> where TValue : unmanaged
    {
        readonly ulong[] m_GraphMembers;
        readonly int m_SourceStateOperationIndex;
        readonly int m_SourceStateRootOperationIndex;
        readonly int m_SourceStateRootOwnerOperationIndex;

        OrchestrationExecutionSourceBinding(OrchestrationProgram<TValue> program,
            int candidateOwnerOperationIndex, OrchestrationProgram<TValue> sourceProgram, int sourceOperationIndex)
        {
            Program = program;
            CandidateOwnerOperationIndex = candidateOwnerOperationIndex;
            SourceOperationIndex = sourceOperationIndex;
            SourceProgram = sourceProgram;
            m_SourceStateOperationIndex = sourceProgram.Operation(sourceOperationIndex).ParentStateOperationIndex;
            int edge = m_SourceStateOperationIndex < 0 ? -1 : sourceProgram.View.StateRootEdge(m_SourceStateOperationIndex);
            m_SourceStateRootOperationIndex = edge < 0 ? -1 : sourceProgram.Edge(edge).TargetOperationIndex;
            m_SourceStateRootOwnerOperationIndex = m_SourceStateRootOperationIndex < 0 ? -1 :
                sourceProgram.Operation(m_SourceStateRootOperationIndex).ExecutionFrameOwnerOperationIndex;
            int operationCount = program.OperationCount;
            m_GraphMembers = new ulong[(operationCount * operationCount + 63) / 64];
            var collected = new bool[operationCount];
            for (int slotIndex = 0; slotIndex < program.BlackboardCount; slotIndex++)
            {
                ref readonly OrchestrationBlackboardSlot slot = ref program.Blackboard(slotIndex);
                int owner = slot.OwnerOperationIndex;
                if (slot.ScopeKind != OrchestrationScopeKind.Graph || owner < 0 || collected[owner])
                    continue;
                collected[owner] = true;
                CollectGraphMembers(owner, program.Operation(owner).ExecutionFrameOwnerOperationIndex, owner);
            }
        }

        public OrchestrationProgram<TValue> Program { get; }
        public OrchestrationProgram<TValue> SourceProgram { get; }
        public int CandidateOwnerOperationIndex { get; }
        public int SourceOperationIndex { get; }

        public OrchestrationExecutionSourceBindingView View =>
            new OrchestrationExecutionSourceBindingView(SourceOperationIndex, m_SourceStateOperationIndex,
                m_SourceStateRootOperationIndex, m_SourceStateRootOwnerOperationIndex, ReferenceEquals(Program, SourceProgram),
                Program.OperationCount, m_GraphMembers);

        public static OrchestrationExecutionSourceBinding<TValue> Prepare(OrchestrationProgram<TValue> program,
            int candidateOwnerOperationIndex, OrchestrationProgram<TValue> sourceProgram, int sourceOperationIndex,
            bool candidateWritesOnly)
        {
            for (int index = 0; candidateWritesOnly && index < program.OperationCount; index++)
            {
                ref readonly OrchestrationOperation operation = ref program.Operation(index);
                if (operation.ExecutionFrameOwnerOperationIndex != candidateOwnerOperationIndex)
                    continue;
                if (operation.Code == OrchestrationOperationCode.BlackboardSet)
                    RequireCandidateWrite(program, candidateOwnerOperationIndex, index,
                        program.Payload<OrchestrationBlackboardSetPayload>(index).Slot);
                if (operation.Code == OrchestrationOperationCode.Extension)
                    foreach (ref readonly OrchestrationBlackboardOutputBinding output in program.OutputAssignments(index))
                        RequireCandidateWrite(program, candidateOwnerOperationIndex, index, output.ValueSlot);
            }
            return new OrchestrationExecutionSourceBinding<TValue>(program, candidateOwnerOperationIndex, sourceProgram, sourceOperationIndex);
        }

        static void RequireCandidateWrite(OrchestrationProgram<TValue> program,
            int candidateOwnerOperationIndex, int operationIndex, int valueSlot)
        {
            ref readonly OrchestrationBlackboardSlot slot = ref program.Blackboard(valueSlot);
            if (slot.ScopeKind == OrchestrationScopeKind.Character ||
                slot.ExecutionFrameOwnerOperationIndex != candidateOwnerOperationIndex)
                throw new InvalidOperationException($"Operation '{operationIndex}' writes source Blackboard '{valueSlot}' ({slot.ScopeKind}/{slot.CompiledOwnerIndex}); source-bound execution may only write its candidate owner '{candidateOwnerOperationIndex}'.");
        }

        void CollectGraphMembers(int operationIndex, int frameOwner, int graphOwner)
        {
            int bit = graphOwner * Program.OperationCount + operationIndex;
            ulong mask = 1UL << (bit & 63);
            if ((m_GraphMembers[bit >> 6] & mask) != 0 ||
                Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex != frameOwner)
                return;
            m_GraphMembers[bit >> 6] |= mask;
            foreach (int edge in Program.Outgoing(operationIndex, OrchestrationFlowKind.Child))
                CollectGraphMembers(Program.Edge(edge).TargetOperationIndex, frameOwner, graphOwner);
            foreach (int edge in Program.Outgoing(operationIndex, OrchestrationFlowKind.Enter))
                CollectGraphMembers(Program.Edge(edge).TargetOperationIndex, frameOwner, graphOwner);
            foreach (int edge in Program.Outgoing(operationIndex, OrchestrationFlowKind.Exit))
                CollectGraphMembers(Program.Edge(edge).TargetOperationIndex, frameOwner, graphOwner);
            if (Program.Operation(operationIndex).Code == OrchestrationOperationCode.StateMachine)
                for (int state = 0; state < Program.OperationCount; state++)
                    if (Program.Operation(state).Code == OrchestrationOperationCode.State &&
                        Program.Payload<OrchestrationStatePayload>(state).MachineIndex == operationIndex)
                        CollectGraphMembers(state, frameOwner, graphOwner);
        }
    }
}
