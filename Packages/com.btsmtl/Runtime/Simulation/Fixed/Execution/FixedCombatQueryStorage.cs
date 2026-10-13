using System;
using System.Collections.Generic;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.Fixed
{
    [CanonicalContract]
    [CanonicalBorrowedObject(typeof(FixedCombatResultStorage))]
    public sealed partial class CombatHitResult
    {
        public ulong ResultId { get; internal set; }
        public ulong SourceTick { get; internal set; }
        ActorId m_SourceActor;
        public ActorId SourceActor
        {
            get => m_SourceActor;
            internal set { value.Value.Retain(); m_SourceActor.Value.Release(); m_SourceActor = value; }
        }
        SimulationActionTargetSnapshot m_SourceTargetSnapshot;
        public SimulationActionTargetSnapshot SourceTargetSnapshot
        {
            get => m_SourceTargetSnapshot;
            internal set { value.Retain(); m_SourceTargetSnapshot.Release(); m_SourceTargetSnapshot = value; }
        }
        ActorId m_TargetActor;
        public ActorId TargetActor
        {
            get => m_TargetActor;
            internal set { value.Value.Retain(); m_TargetActor.Value.Release(); m_TargetActor = value; }
        }
        public FixedVector3 TargetPosition { get; internal set; }
        public FixedYaw TargetYaw { get; internal set; }
        public FixedScalar ReceiverMinimumY { get; internal set; }
        public FixedScalar ReceiverMaximumY { get; internal set; }
        CombatAttackIdentity m_Attack;
        public CombatAttackIdentity Attack
        {
            get => m_Attack;
            internal set { value.Retain(); m_Attack.Release(); m_Attack = value; }
        }
        [CanonicalDeclaredText]
        public string ContentId { get; internal set; }
        CanonicalTextValue m_AttackEventId;
        public CanonicalTextValue AttackEventId
        {
            get => m_AttackEventId;
            internal set { value.Retain(); m_AttackEventId.Release(); m_AttackEventId = value; }
        }
        [CanonicalDeclaredText]
        public string SourceFeedbackEvent { get; internal set; }
        [CanonicalDeclaredText]
        public string TargetFeedbackEvent { get; internal set; }
        public ulong TargetActionInstanceId { get; internal set; }
        public CombatHitDisposition Disposition { get; internal set; }
        public bool Heavy { get; internal set; }
        public int HitType { get; internal set; }
        public int HitStrengthType { get; internal set; }
        public int DamageBreakLevel { get; internal set; }
        public int FrameHalt { get; internal set; }
        public int AttackerFrameHalt { get; internal set; }
        public uint HitEffectId { get; internal set; }
        public SweepContactFields ContactFields { get; internal set; }
        public FixedScalar ContactTime { get; internal set; }
        public FixedVector3 Point { get; internal set; }
        public FixedVector3 Normal { get; internal set; }
        internal int DefinitionIndex { get; set; }

        internal void ClearText() => CanonicalClear(this);

        internal void CopyFrom(CombatHitResult value) => CanonicalCopyInto(value, this);
    }

    internal sealed class FixedCombatQuery
    {
        CombatAttackIdentity m_Identity;
        public CombatAttackIdentity Identity
        {
            get => m_Identity;
            set { value.Retain(); m_Identity.Release(); m_Identity = value; }
        }
        TimelineActionContextIdentity m_Action;
        public TimelineActionContextIdentity Action
        {
            get => m_Action;
            set { value.Retain(); m_Action.Release(); m_Action = value; }
        }
        public PortableAttackDefinition Definition;
        public ulong IntervalEffectHandle;
        public GameplayEffectScope IntervalScope;
        GameplayEffectHolderIdentity m_IntervalHolder;
        public GameplayEffectHolderIdentity IntervalHolder
        {
            get => m_IntervalHolder;
            set { value.Retain(); m_IntervalHolder.Release(); m_IntervalHolder = value; }
        }
        CanonicalTextValue m_NodeId;
        public CanonicalTextValue NodeId
        {
            get => m_NodeId;
            set { value.Retain(); m_NodeId.Release(); m_NodeId = value; }
        }
        public string AttackEventId;
        public ulong RequestId;
        public FixedVector3 Origin;
        public FixedYaw Yaw;
        public FixedScalar PreviousTime;
        public FixedScalar Time;
        public FixedScalar WindowStartTime;
        public FixedScalar BodyStartFraction;
        public FixedScalar BodyEndFraction;
        SimulationEventHeader m_Header;
        public SimulationEventHeader Header
        {
            get => m_Header;
            set { value.Retain(); m_Header.Release(); m_Header = value; }
        }
        public FixedLocalEnemyAttackWindow LocalWindow;
        internal void ClearText()
        {
            Identity = default; Action = default; NodeId = default; Header = default;
            IntervalEffectHandle = 0; IntervalScope = default; IntervalHolder = default;
        }
    }

    internal sealed class FixedCombatQueryStorage
    {
        readonly FixedCombatQuery[] m_Queries;
        public FixedCombatQueryStorage(int capacity)
        {
            m_Queries = new FixedCombatQuery[capacity];
            for (int i = 0; i < capacity; i++)
                m_Queries[i] = new FixedCombatQuery();
        }
        public int Count { get; private set; }
        public FixedCombatQuery this[int index] => m_Queries[index];
        public FixedCombatQuery Append()
        {
            if (Count == m_Queries.Length)
                throw new InvalidOperationException("Combat attack request capacity exceeded.");
            return m_Queries[Count++];
        }
        public void Clear()
        {
            Truncate(0);
        }
        internal void Truncate(int count)
        {
            for (int index = count; index < Count; index++)
                m_Queries[index].ClearText();
            Count = count;
        }
    }

    [CanonicalBorrowedSequence(typeof(FixedCombatResultStorage), HoldStorage = false)]
    public sealed class FixedCombatResultStorage : IReadOnlyList<CombatHitResult>
    {
        readonly CombatHitResult[] m_Results;
        public FixedCombatResultStorage(int capacity)
        {
            m_Results = new CombatHitResult[capacity];
            for (int i = 0; i < capacity; i++)
                m_Results[i] = new CombatHitResult();
        }
        public int Count { get; private set; }
        public int Capacity => m_Results.Length;
        public ReadOnlySpan<CombatHitResult> Span => m_Results.AsSpan(0, Count);
        public CombatHitResult this[int index] => m_Results[index];
        public CombatHitResult Reserve() => Append();

        public Span<CombatHitResult> Reserve(int count, out FixedCombatResultStorage items)
        {
            if (count < 0 || count > Capacity)
                throw new InvalidOperationException("Combat accepted result capacity exceeded.");
            Clear();
            Count = count;
            items = this;
            return m_Results.AsSpan(0, count);
        }

        public CombatHitResult Append()
        {
            if (Count == m_Results.Length)
                throw new InvalidOperationException("Combat accepted result capacity exceeded.");
            return m_Results[Count++];
        }
        public void Clear()
        {
            for (int index = 0; index < Count; index++)
                m_Results[index].ClearText();
            Count = 0;
        }
        public void CopyFrom(FixedCombatResultStorage value)
        {
            Clear();
            for (int i = 0; i < value.Count; i++)
                Append().CopyFrom(value[i]);
        }
        public IEnumerator<CombatHitResult> GetEnumerator() => ((IEnumerable<CombatHitResult>)new ArraySegment<CombatHitResult>(m_Results, 0, Count)).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
