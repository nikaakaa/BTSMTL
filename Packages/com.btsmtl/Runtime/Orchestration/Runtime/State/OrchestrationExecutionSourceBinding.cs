using System;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationExecutionSourceBinding<TValue> where TValue : unmanaged
    {
        readonly bool[][] m_GraphMembers;

        OrchestrationExecutionSourceBinding(OrchestrationProgram<TValue> program,
            int candidateOwnerOperationIndex, int sourceOperationIndex)
        {
            Program = program;
            CandidateOwnerOperationIndex = candidateOwnerOperationIndex;
            SourceOperationIndex = sourceOperationIndex;
            SourceStateOperationIndex = program.Operation(sourceOperationIndex).ParentStateOperationIndex;
            m_GraphMembers = new bool[program.OperationCount][];
            for (int slotIndex = 0; slotIndex < program.BlackboardCount; slotIndex++)
            {
                ref readonly OrchestrationBlackboardSlot slot = ref program.Blackboard(slotIndex);
                int owner = slot.OwnerOperationIndex;
                if (slot.ScopeKind != OrchestrationScopeKind.Graph || owner < 0 || m_GraphMembers[owner] != null)
                    continue;
                var members = new bool[program.OperationCount];
                CollectGraphMembers(owner, program.Operation(owner).ExecutionFrameOwnerOperationIndex, members);
                m_GraphMembers[owner] = members;
            }
        }

        public OrchestrationProgram<TValue> Program { get; }
        public int CandidateOwnerOperationIndex { get; }
        public int SourceOperationIndex { get; }
        public int SourceStateOperationIndex { get; }

        public static OrchestrationExecutionSourceBinding<TValue> Prepare(OrchestrationProgram<TValue> program,
            int candidateOwnerOperationIndex, int sourceOperationIndex)
        {
            for (int index = 0; index < program.OperationCount; index++)
            {
                ref readonly OrchestrationOperation operation = ref program.Operation(index);
                if (operation.ExecutionFrameOwnerOperationIndex != candidateOwnerOperationIndex)
                    continue;
                if (operation.Code == OrchestrationOperationCode.BlackboardSet)
                    RequireCandidateWrite(program, candidateOwnerOperationIndex, index, operation.ValueSlot);
                foreach (ref readonly OrchestrationBlackboardOutputBinding output in program.OutputAssignments(index))
                    RequireCandidateWrite(program, candidateOwnerOperationIndex, index, output.ValueSlot);
            }
            return new OrchestrationExecutionSourceBinding<TValue>(program, candidateOwnerOperationIndex, sourceOperationIndex);
        }

        static void RequireCandidateWrite(OrchestrationProgram<TValue> program,
            int candidateOwnerOperationIndex, int operationIndex, int valueSlot)
        {
            ref readonly OrchestrationBlackboardSlot slot = ref program.Blackboard(valueSlot);
            if (slot.ScopeKind == OrchestrationScopeKind.Character ||
                slot.ExecutionFrameOwnerOperationIndex != candidateOwnerOperationIndex)
                throw new InvalidOperationException($"Operation '{operationIndex}' writes source Blackboard '{valueSlot}' ({slot.ScopeKind}/{slot.CompiledOwnerIndex}); source-bound execution may only write its candidate owner '{candidateOwnerOperationIndex}'.");
        }

        internal bool ContainsSourceState(int stateOperationIndex)
        {
            for (int state = SourceStateOperationIndex; state >= 0;
                state = Program.Operation(state).ParentStateOperationIndex)
                if (state == stateOperationIndex)
                    return true;
            return false;
        }

        internal bool ContainsSourceGraph(int graphOwnerOperationIndex, int sourceOperationIndex) =>
            m_GraphMembers[graphOwnerOperationIndex][sourceOperationIndex];

        void CollectGraphMembers(int operationIndex, int frameOwner, bool[] members)
        {
            if (members[operationIndex] || Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex != frameOwner)
                return;
            members[operationIndex] = true;
            foreach (int edge in Program.Outgoing(operationIndex, OrchestrationFlowKind.Child))
                CollectGraphMembers(Program.Edge(edge).TargetOperationIndex, frameOwner, members);
            foreach (int edge in Program.Outgoing(operationIndex, OrchestrationFlowKind.Enter))
                CollectGraphMembers(Program.Edge(edge).TargetOperationIndex, frameOwner, members);
            foreach (int edge in Program.Outgoing(operationIndex, OrchestrationFlowKind.Exit))
                CollectGraphMembers(Program.Edge(edge).TargetOperationIndex, frameOwner, members);
            if (Program.Operation(operationIndex).Code == OrchestrationOperationCode.StateMachine)
                for (int state = 0; state < Program.OperationCount; state++)
                    if (Program.Operation(state).Code == OrchestrationOperationCode.State &&
                        Program.Operation(state).StateMachineOwnerOperationIndex == operationIndex)
                        CollectGraphMembers(state, frameOwner, members);
        }
    }

    public readonly ref struct OrchestrationExecutionSource<TValue> where TValue : unmanaged
    {
        internal OrchestrationExecutionSource(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            ReadOnlySpan<TValue> characterValues, ReadOnlySpan<OrchestrationBlackboardMetadata> characterBlackboards,
            int frameSlot, ulong tick, in OrchestrationStopContext stopContext)
        {
            Frames = frames;
            CharacterValues = characterValues;
            CharacterBlackboards = characterBlackboards;
            FrameSlot = frameSlot;
            Tick = tick;
            StopContext = stopContext;
        }

        public ReadOnlyOrchestrationExecutionFrames<TValue> Frames { get; }
        public ReadOnlySpan<TValue> CharacterValues { get; }
        public ReadOnlySpan<OrchestrationBlackboardMetadata> CharacterBlackboards { get; }
        public int FrameSlot { get; }
        public ulong Tick { get; }
        public OrchestrationStopContext StopContext { get; }
        public ref readonly OrchestrationExecutionFrameState Header => ref Frames.Header(FrameSlot);
    }
}
