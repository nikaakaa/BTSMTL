using Photon.Deterministic;
using System;

namespace ThirdPersonSimulation.Collision
{
    public sealed class SweepResultBuffer<TSource> where TSource : struct
    {
        readonly SweepCandidate[] m_Candidates;
        int m_Count;
        TSource m_Source;

        public SweepResultBuffer(int capacity) { m_Candidates = new SweepCandidate[capacity]; }
        public int Capacity => m_Candidates.Length;
        public SweepQueryStatus Status { get; private set; }
        public ref readonly TSource Source => ref m_Source;
        public SweepBackendDescriptor Backend { get; private set; }
        public SweepTargetModel TargetModel { get; private set; }
        public FP TargetPoseTime { get; private set; }
        public bool TimesComparable { get; private set; }
        public int FailureSegmentIndex { get; private set; }
        public SweepTargetIdentity FailureTarget { get; private set; }
        public int FailureNativeObjectId { get; private set; }

        public ReadOnlySpan<SweepCandidate> Candidates
        {
            get
            {
                if (Status != SweepQueryStatus.Complete)
                    throw new InvalidOperationException("Sweep results are not complete.");
                return m_Candidates.AsSpan(0, m_Count);
            }
        }

        public void Begin(in SweepQuery<TSource> query, SweepBackendDescriptor backend)
        {
            if (Status != SweepQueryStatus.Available)
                throw new InvalidOperationException("Sweep results must be released before reuse.");
            m_Source = query.Source;
            Backend = backend;
            TargetModel = query.TargetModel;
            TargetPoseTime = query.TargetPoseTime;
            TimesComparable = true;
            FailureSegmentIndex = -1;
            FailureTarget = default;
            FailureNativeObjectId = 0;
            Status = SweepQueryStatus.Writing;
        }

        public bool Add(in SweepCandidate candidate)
        {
            TimesComparable &= (candidate.Fields & SweepContactFields.Time) != 0;
            for (int i = 0; i < m_Count; i++)
            {
                ref readonly SweepCandidate existing = ref m_Candidates[i];
                if (existing.Target.TargetId != candidate.Target.TargetId)
                    continue;
                if (Compare(candidate, in existing, true) < 0)
                    m_Candidates[i] = candidate;
                return true;
            }
            if (m_Count == m_Candidates.Length)
                return false;
            m_Candidates[m_Count++] = candidate;
            return true;
        }

        public SweepQueryStatus Complete()
        {
            for (int i = 1; i < m_Count; i++)
            {
                SweepCandidate candidate = m_Candidates[i];
                int j = i - 1;
                while (j >= 0 && Compare(candidate, m_Candidates[j], TimesComparable) < 0)
                {
                    m_Candidates[j + 1] = m_Candidates[j];
                    j--;
                }
                m_Candidates[j + 1] = candidate;
            }
            Status = SweepQueryStatus.Complete;
            return Status;
        }

        public SweepQueryStatus Fail(SweepQueryStatus status, int segmentIndex, SweepTargetIdentity target, int nativeObjectId = 0)
        {
            Status = status;
            FailureSegmentIndex = segmentIndex;
            FailureTarget = target;
            FailureNativeObjectId = nativeObjectId;
            m_Count = 0;
            return Status;
        }

        public void Release()
        {
            m_Count = 0;
            m_Source = default;
            Status = SweepQueryStatus.Available;
        }

        static int Compare(in SweepCandidate left, in SweepCandidate right, bool compareTimes)
        {
            bool leftHasTime = (left.Fields & SweepContactFields.Time) != 0;
            bool rightHasTime = (right.Fields & SweepContactFields.Time) != 0;
            if (compareTimes && leftHasTime && rightHasTime)
            {
                int time = left.Time.CompareTo(right.Time);
                if (time != 0)
                    return time;
            }
            int target = left.Target.TargetId.CompareTo(right.Target.TargetId);
            if (target != 0)
                return target;
            int targetShape = left.Target.ShapeId.CompareTo(right.Target.ShapeId);
            if (targetShape != 0)
                return targetShape;
            int track = left.TrackId.CompareTo(right.TrackId);
            if (track != 0)
                return track;
            int shape = left.ShapeId.CompareTo(right.ShapeId);
            if (shape != 0)
                return shape;
            int segment = left.SegmentIndex.CompareTo(right.SegmentIndex);
            if (segment != 0)
                return segment;
            int sourcePart = left.SourcePartIndex.CompareTo(right.SourcePartIndex);
            return sourcePart != 0 ? sourcePart : left.TargetPartIndex.CompareTo(right.TargetPartIndex);
        }
    }
}
