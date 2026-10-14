using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
#if UNITY_EDITOR
using System;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.FPGameplay
{
    public readonly struct FPCombatContactDiagnostic
    {
        internal FPCombatContactDiagnostic(in SweepCandidate<FP> candidate,
            bool accepted, CombatHitDisposition disposition)
        {
            Candidate = candidate;
            Accepted = accepted;
            Disposition = disposition;
        }

        public SweepCandidate<FP> Candidate { get; }
        public bool Accepted { get; }
        public CombatHitDisposition Disposition { get; }
    }

    public sealed class FPCombatQueryDiagnostic
    {
        readonly SweepTarget<FP>[] m_Targets;
        readonly FPCombatContactDiagnostic[] m_Contacts;
        readonly SweepSegment<FP>[] m_Segments;

        internal FPCombatQueryDiagnostic(int targetCapacity, int segmentCapacity)
        {
            m_Targets = new SweepTarget<FP>[targetCapacity];
            m_Contacts = new FPCombatContactDiagnostic[targetCapacity];
            m_Segments = new SweepSegment<FP>[segmentCapacity];
        }

        public ActorId SourceActor { get; private set; }
        public string ContentId { get; private set; }
        public string AttackEventId { get; private set; }
        public CanonicalTextValue NodeId { get; private set; }
        public ulong IntervalEffectHandle { get; private set; }
        public GameplayEffectScope IntervalScope { get; private set; }
        public GameplayEffectHolderIdentity IntervalHolder { get; private set; }
        public int SegmentCount { get; private set; }
        public ReadOnlySpan<SweepSegment<FP>> Segments => m_Segments.AsSpan(0, SegmentCount);
        public SweepTargetModel TargetModel { get; private set; }
        public SweepFilter Filter { get; private set; }
        public int TargetCount { get; private set; }
        public int ContactCount { get; private set; }
        public ReadOnlySpan<SweepTarget<FP>> Targets => m_Targets.AsSpan(0, TargetCount);
        public ReadOnlySpan<FPCombatContactDiagnostic> Contacts => m_Contacts.AsSpan(0, ContactCount);

        internal void Set(ActorId sourceActor, string contentId, string eventId, CanonicalTextValue nodeId,
            ReadOnlySpan<SweepSegment<FP>> segments, SweepTargetModel targetModel, SweepFilter filter,
            ReadOnlySpan<SweepTarget<FP>> targets, ulong intervalEffectHandle,
            GameplayEffectScope intervalScope, GameplayEffectHolderIdentity intervalHolder)
        {
            sourceActor.Value.Retain();
            nodeId.Retain();
            intervalHolder.Retain();
            ClearText();
            SourceActor = sourceActor;
            ContentId = contentId;
            AttackEventId = eventId;
            NodeId = nodeId;
            IntervalEffectHandle = intervalEffectHandle;
            IntervalScope = intervalScope;
            IntervalHolder = intervalHolder;
            SegmentCount = segments.Length;
            segments.CopyTo(m_Segments);
            TargetModel = targetModel;
            Filter = filter;
            TargetCount = targets.Length;
            targets.CopyTo(m_Targets);
            ContactCount = 0;
        }

        internal void ClearText()
        {
            SourceActor.Value.Release();
            NodeId.Release();
            IntervalHolder.Release();
            SourceActor = default;
            NodeId = default;
            IntervalEffectHandle = 0;
            IntervalScope = default;
            IntervalHolder = default;
        }

        internal void AddContact(in SweepCandidate<FP> candidate,
            bool accepted, CombatHitDisposition disposition) =>
            m_Contacts[ContactCount++] = new FPCombatContactDiagnostic(in candidate, accepted, disposition);

        internal void CopyFrom(FPCombatQueryDiagnostic source)
        {
            Set(source.SourceActor, source.ContentId, source.AttackEventId, source.NodeId,
                source.Segments, source.TargetModel, source.Filter, source.Targets,
                source.IntervalEffectHandle, source.IntervalScope, source.IntervalHolder);
            source.Contacts.CopyTo(m_Contacts);
            ContactCount = source.ContactCount;
        }
    }

    public sealed class FPCombatQueryDiagnostics
    {
        sealed class Frame
        {
            internal Frame(int queries, int targets, int segments)
            {
                Queries = new FPCombatQueryDiagnostic[queries];
                for (int i = 0; i < queries; i++)
                    Queries[i] = new FPCombatQueryDiagnostic(targets, segments);
            }
            internal readonly FPCombatQueryDiagnostic[] Queries;
            internal ulong Tick;
            internal int Count;
            internal void Clear()
            {
                for (int index = 0; index < Count; index++)
                    Queries[index].ClearText();
                Count = 0;
            }
        }

        readonly ActorId m_Publisher;
        readonly SweepShape<FP>[] m_Shapes;
        Frame m_Writing;
        Frame m_Pending;
        readonly Frame m_Published;

        internal FPCombatQueryDiagnostics(ActorId publisher, SweepShape<FP>[] shapes,
            int queryCapacity, int targetCapacity, int segmentCapacity)
        {
            m_Publisher = publisher;
            m_Shapes = shapes;
            m_Writing = new Frame(queryCapacity, targetCapacity, segmentCapacity);
            m_Pending = new Frame(queryCapacity, targetCapacity, segmentCapacity);
            m_Published = new Frame(queryCapacity, targetCapacity, segmentCapacity);
        }

        public bool CaptureEnabled { get; set; }
        public ulong Revision { get; private set; }
        public ulong Tick => m_Published.Tick;
        public int Count => m_Published.Count;
        public ReadOnlySpan<SweepShape<FP>> Shapes => m_Shapes;
        public FPCombatQueryDiagnostic this[int index] => m_Published.Queries[index];

        internal void BeginTick(ulong tick)
        {
            m_Writing.Tick = tick;
            m_Writing.Clear();
        }

        internal FPCombatQueryDiagnostic Append(ActorId actor, string content, FPCombatQuery request,
            ReadOnlySpan<SweepSegment<FP>> segments, SweepTargetModel model, SweepFilter filter,
            ReadOnlySpan<SweepTarget<FP>> targets)
        {
            FPCombatQueryDiagnostic query = m_Writing.Queries[m_Writing.Count++];
            query.Set(actor, content, request.AttackEventId, request.NodeId, segments, model, filter, targets,
                request.IntervalEffectHandle, request.IntervalScope, request.IntervalHolder);
            return query;
        }

        internal void CompleteTick()
        {
            if (m_Writing.Count == 0)
                return;
            Frame reusable = m_Pending;
            m_Pending = m_Writing;
            m_Writing = reusable;
        }

        internal void Dispose()
        {
            m_Writing.Clear(); m_Pending.Clear(); m_Published.Clear();
        }

        public void PublishCommitted(ActorId actor, ulong tick)
        {
            if (!CaptureEnabled || actor != m_Publisher || tick != m_Pending.Tick || m_Pending.Count == 0)
                return;
            m_Published.Clear();
            for (int i = 0; i < m_Pending.Count; i++)
                m_Published.Queries[i].CopyFrom(m_Pending.Queries[i]);
            m_Published.Count = m_Pending.Count;
            m_Published.Tick = tick;
            Revision++;
        }
    }
}
#endif
