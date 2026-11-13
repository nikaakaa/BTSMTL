using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExecutionSourceBindingView
    {
        readonly ReadOnlySpan<ulong> m_GraphMembers;
        readonly ReadOnlySpan<int> m_GraphRows;
        readonly int m_GraphWordsPerRow;

        internal OrchestrationExecutionSourceBindingView(int sourceOperationIndex, int sourceStateOperationIndex,
            int sourceStateRootOperationIndex, int sourceStateRootOwnerOperationIndex, bool sameProgram,
            int graphWordsPerRow, ReadOnlySpan<int> graphRows, ReadOnlySpan<ulong> graphMembers)
        {
            HasSource = true;
            SourceOperationIndex = sourceOperationIndex;
            SourceStateOperationIndex = sourceStateOperationIndex;
            SourceStateRootOperationIndex = sourceStateRootOperationIndex;
            SourceStateRootOwnerOperationIndex = sourceStateRootOwnerOperationIndex;
            SameProgram = sameProgram;
            m_GraphWordsPerRow = graphWordsPerRow;
            m_GraphRows = graphRows;
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
            int row = m_GraphRows[graphOwnerOperationIndex];
            return row >= 0 && (m_GraphMembers[row * m_GraphWordsPerRow + (sourceOperationIndex >> 6)] &
                (1UL << (sourceOperationIndex & 63))) != 0;
        }
    }

    public sealed class OrchestrationExecutionSourceBinding<TValue> where TValue : unmanaged
    {
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
        }

        public OrchestrationProgram<TValue> Program { get; }
        public OrchestrationProgram<TValue> SourceProgram { get; }
        public int CandidateOwnerOperationIndex { get; }
        public int SourceOperationIndex { get; }

        public OrchestrationExecutionSourceBindingView View =>
            new OrchestrationExecutionSourceBindingView(SourceOperationIndex, m_SourceStateOperationIndex,
                m_SourceStateRootOperationIndex, m_SourceStateRootOwnerOperationIndex, ReferenceEquals(Program, SourceProgram),
                Program.SourceGraphWordsPerRow, Program.SourceGraphRows, Program.SourceGraphMembers);

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
            if (slot.ScopeKind is OrchestrationScopeKind.Character or OrchestrationScopeKind.ActionInstance ||
                slot.ExecutionFrameOwnerOperationIndex != candidateOwnerOperationIndex)
                throw new InvalidOperationException($"Operation '{operationIndex}' writes source Blackboard '{valueSlot}' ({slot.ScopeKind}/{slot.CompiledOwnerIndex}); source-bound execution may only write its candidate owner '{candidateOwnerOperationIndex}'.");
        }

    }
}
