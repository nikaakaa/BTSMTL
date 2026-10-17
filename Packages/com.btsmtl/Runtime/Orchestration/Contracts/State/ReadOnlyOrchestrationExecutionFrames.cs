using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct ReadOnlyOrchestrationExecutionFrames<TValue> where TValue : unmanaged
    {
        readonly ReadOnlySpan<OrchestrationExecutionFrameState> m_Frames;
        readonly ReadOnlySpan<OrchestrationExecutionRosterState> m_Roster;
        readonly ReadOnlySpan<OrchestrationOperationState> m_Operations;
        readonly ReadOnlySpan<TValue> m_Values;
        readonly ReadOnlySpan<OrchestrationBlackboardMetadata> m_BlackboardMetadata;
        readonly int m_OperationsPerFrame;
        readonly int m_ValuesPerFrame;
        readonly int m_BlackboardMetadataPerFrame;

        internal ReadOnlyOrchestrationExecutionFrames(
            ReadOnlySpan<OrchestrationExecutionFrameState> frames,
            ReadOnlySpan<OrchestrationExecutionRosterState> roster,
            ReadOnlySpan<OrchestrationOperationState> operations,
            ReadOnlySpan<TValue> values,
            ReadOnlySpan<OrchestrationBlackboardMetadata> blackboardMetadata)
        {
            m_Frames = frames;
            m_Roster = roster;
            m_Operations = operations;
            m_Values = values;
            m_BlackboardMetadata = blackboardMetadata;
            m_OperationsPerFrame = operations.Length / frames.Length;
            m_ValuesPerFrame = values.Length / frames.Length;
            m_BlackboardMetadataPerFrame = blackboardMetadata.Length / frames.Length;
        }

        public int Count => m_Roster[0].Count;
        public int Capacity => m_Frames.Length;
        public ref readonly OrchestrationExecutionFrameState Header(int frameSlot) => ref m_Frames[frameSlot];
        internal ReadOnlySpan<OrchestrationExecutionFrameState> HeaderSpan(int frameSlot) => m_Frames.Slice(frameSlot, 1);
        public ReadOnlySpan<OrchestrationOperationState> Operations(int frameSlot) =>
            m_Operations.Slice(frameSlot * m_OperationsPerFrame, m_OperationsPerFrame);
        public ReadOnlySpan<TValue> Values(int frameSlot) =>
            m_Values.Slice(frameSlot * m_ValuesPerFrame, m_ValuesPerFrame);
        public ReadOnlySpan<OrchestrationBlackboardMetadata> BlackboardMetadata(int frameSlot) =>
            m_BlackboardMetadata.Slice(frameSlot * m_BlackboardMetadataPerFrame,
                m_BlackboardMetadataPerFrame);

        public int OwnerFrame(int frameSlot, int ownerOperationIndex)
        {
            while (m_Frames[frameSlot].OwnerOperationIndex != ownerOperationIndex)
                frameSlot = m_Frames[frameSlot].ParentFrameIndex;
            return frameSlot;
        }
    }
}
