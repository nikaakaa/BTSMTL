using System;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs
{
    public sealed class GraphStateTransaction
    {
        readonly StableHash m_LayoutIdentity;
        readonly GraphTextStorage m_FirstTexts;
        readonly GraphTextStorage m_SecondTexts;

        public GraphStateTransaction(StableHash layoutIdentity,
            GraphTextStorage firstTexts, GraphTextStorage secondTexts)
        {
            m_LayoutIdentity = layoutIdentity;
            m_FirstTexts = firstTexts;
            m_SecondTexts = secondTexts;
        }

        public int CommittedBank { get; private set; }
        public int CandidateBank => 1 - CommittedBank;
        public bool HasCandidate { get; private set; }
        public GraphTextStorage CommittedTexts => CommittedBank == 0 ? m_FirstTexts : m_SecondTexts;
        public GraphTextStorage CandidateTexts => CommittedBank == 0 ? m_SecondTexts : m_FirstTexts;

        public ReadOnlySpan<byte> BindCommitted(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) =>
            CommittedBank == 0 ? first : second;

        public Span<byte> BindCandidate(Span<byte> first, Span<byte> second) =>
            CommittedBank == 0 ? second : first;

        public void Begin(Span<byte> first, Span<byte> second)
        {
            BindCommitted(first, second).CopyTo(BindCandidate(first, second));
            HasCandidate = true;
        }

        public void Commit()
        {
            CommittedBank = CandidateBank;
            HasCandidate = false;
        }

        public void Discard()
        {
            CandidateTexts.Reset();
            HasCandidate = false;
        }

        public void WriteRecordedState<TWriter>(ref TWriter writer,
            ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(m_LayoutIdentity);
            ReadOnlySpan<byte> memory = BindCommitted(first, second);
            writer.Write(memory.Length);
            writer.WriteSpan<byte>(memory);
            CommittedTexts.WriteRecordedState(ref writer);
        }

        public void RestoreRecordedState(ref RecordedRuntimeStateReader reader,
            Span<byte> first, Span<byte> second)
        {
            StableHash identity = reader.Read<StableHash>();
            int length = reader.Read<int>();
            if (identity != m_LayoutIdentity || length != first.Length)
                throw new InvalidOperationException("录制状态的布局身份或字节数与当前实例不匹配。");
            reader.ReadSpan<byte>(length).CopyTo(BindCandidate(first, second));
            CandidateTexts.RestoreRecordedState(ref reader);
            HasCandidate = true;
        }
    }
}
