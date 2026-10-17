using System;
using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationExecutionSourceSnapshot<TValue> where TValue : unmanaged
    {
        readonly OrchestrationExecutionFrameState[] m_Frames;
        readonly OrchestrationExecutionRosterState[] m_Roster = new OrchestrationExecutionRosterState[1];
        readonly OrchestrationOperationState[] m_Operations;
        readonly TValue[] m_Values;
        readonly OrchestrationBlackboardMetadata[] m_Blackboards;
        readonly TValue[] m_CharacterValues;
        readonly OrchestrationBlackboardMetadata[] m_CharacterBlackboards;
        readonly int m_OperationCount;
        readonly int m_ValueCount;
        int m_CapturedOperationCount;
        int m_CapturedValueCount;
        ulong m_Tick;
        OrchestrationStopContext m_StopContext;

        public OrchestrationExecutionSourceSnapshot(int frameCapacity, int operationCount,
            int valueCount, int characterValueCount, int characterBlackboardCount)
        {
            m_Frames = new OrchestrationExecutionFrameState[frameCapacity];
            m_Operations = new OrchestrationOperationState[checked(frameCapacity * operationCount)];
            m_Values = new TValue[checked(frameCapacity * valueCount)];
            m_Blackboards = new OrchestrationBlackboardMetadata[checked(frameCapacity * valueCount)];
            m_CharacterValues = new TValue[characterValueCount];
            m_CharacterBlackboards = new OrchestrationBlackboardMetadata[characterBlackboardCount];
            m_OperationCount = operationCount;
            m_ValueCount = valueCount;
        }

        public int StorageSizeBytes => checked(MemoryMarshal.AsBytes(m_Frames.AsSpan()).Length +
            MemoryMarshal.AsBytes(m_Roster.AsSpan()).Length + MemoryMarshal.AsBytes(m_Operations.AsSpan()).Length +
            MemoryMarshal.AsBytes(m_Values.AsSpan()).Length + MemoryMarshal.AsBytes(m_Blackboards.AsSpan()).Length +
            MemoryMarshal.AsBytes(m_CharacterValues.AsSpan()).Length +
            MemoryMarshal.AsBytes(m_CharacterBlackboards.AsSpan()).Length);

        public void Capture(OrchestrationExecutionSource<TValue> source, int operationCount, int valueCount)
        {
            m_CapturedOperationCount = operationCount;
            m_CapturedValueCount = valueCount;
            int count = 0;
            for (int sourceSlot = source.FrameSlot; sourceSlot >= 0; count++)
            {
                ref readonly OrchestrationExecutionFrameState header = ref source.Frames.Header(sourceSlot);
                m_Frames[count] = header;
                m_Frames[count].ParentFrameIndex = header.ParentFrameIndex < 0 ? -1 : count + 1;
                source.Frames.Operations(sourceSlot).Slice(0, operationCount)
                    .CopyTo(m_Operations.AsSpan(count * m_OperationCount, operationCount));
                source.Frames.Values(sourceSlot).Slice(0, valueCount)
                    .CopyTo(m_Values.AsSpan(count * m_ValueCount, valueCount));
                source.Frames.BlackboardMetadata(sourceSlot).Slice(0, valueCount)
                    .CopyTo(m_Blackboards.AsSpan(count * m_ValueCount, valueCount));
                sourceSlot = header.ParentFrameIndex;
            }
            m_Roster[0].Count = count;
            source.CharacterValues.CopyTo(m_CharacterValues);
            source.CharacterBlackboards.CopyTo(m_CharacterBlackboards);
            m_Tick = source.Tick;
            m_StopContext = source.StopContext;
        }

        public OrchestrationExecutionSource<TValue> Borrow()
        {
            int count = m_Roster[0].Count;
            var frames = new ReadOnlyOrchestrationExecutionFrames<TValue>(
                m_Frames.AsSpan(0, count), m_Roster,
                m_Operations.AsSpan(0, count * m_OperationCount),
                m_Values.AsSpan(0, count * m_ValueCount),
                m_Blackboards.AsSpan(0, count * m_ValueCount));
            return new OrchestrationExecutionSource<TValue>(frames, m_CharacterValues,
                m_CharacterBlackboards, 0, m_Tick, in m_StopContext);
        }

        public void WriteRecordedState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            int count = m_Roster[0].Count;
            writer.Write(count);
            writer.Write(m_CapturedOperationCount);
            writer.Write(m_CapturedValueCount);
            writer.Write(m_Tick);
            writer.Write(m_StopContext);
            writer.WriteSpan<OrchestrationExecutionFrameState>(m_Frames.AsSpan(0, count));
            for (int slot = 0; slot < count; slot++)
            {
                writer.WriteSpan<OrchestrationOperationState>(m_Operations.AsSpan(
                    slot * m_OperationCount, m_CapturedOperationCount));
                writer.WriteSpan<TValue>(m_Values.AsSpan(slot * m_ValueCount, m_CapturedValueCount));
                writer.WriteSpan<OrchestrationBlackboardMetadata>(m_Blackboards.AsSpan(
                    slot * m_ValueCount, m_CapturedValueCount));
            }
            writer.WriteSpan<TValue>(m_CharacterValues);
            writer.WriteSpan<OrchestrationBlackboardMetadata>(m_CharacterBlackboards);
        }

        public void RestoreRecordedState(ref RecordedRuntimeStateReader reader)
        {
            int count = reader.Read<int>();
            m_Roster[0].Count = count;
            m_CapturedOperationCount = reader.Read<int>();
            m_CapturedValueCount = reader.Read<int>();
            m_Tick = reader.Read<ulong>();
            m_StopContext = reader.Read<OrchestrationStopContext>();
            reader.ReadSpan<OrchestrationExecutionFrameState>(count).CopyTo(m_Frames);
            for (int slot = 0; slot < count; slot++)
            {
                reader.ReadSpan<OrchestrationOperationState>(m_CapturedOperationCount).CopyTo(
                    m_Operations.AsSpan(slot * m_OperationCount, m_CapturedOperationCount));
                reader.ReadSpan<TValue>(m_CapturedValueCount).CopyTo(
                    m_Values.AsSpan(slot * m_ValueCount, m_CapturedValueCount));
                reader.ReadSpan<OrchestrationBlackboardMetadata>(m_CapturedValueCount).CopyTo(
                    m_Blackboards.AsSpan(slot * m_ValueCount, m_CapturedValueCount));
            }
            reader.ReadSpan<TValue>(m_CharacterValues.Length).CopyTo(m_CharacterValues);
            reader.ReadSpan<OrchestrationBlackboardMetadata>(m_CharacterBlackboards.Length).CopyTo(m_CharacterBlackboards);
        }
    }
}
