using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.FPGameplay
{
    [ValueSnapshot(CopyMethod = nameof(CopyFrom), ClearMethod = nameof(ClearText))]
    [ValueBorrowedObject(typeof(FPCombatResultStorage))]
    public sealed partial class CombatHitResult
    {
        [ValueSnapshot] public ulong ResultId { get; internal set; }
        [ValueSnapshot] public ulong SourceTick { get; internal set; }
        [ValueSnapshot] ActorId m_SourceActor;
        public ActorId SourceActor
        {
            get => m_SourceActor;
            internal set { value.Value.Retain(); m_SourceActor.Value.Release(); m_SourceActor = value; }
        }
        [ValueSnapshot] SimulationActionTargetSnapshot m_SourceTargetSnapshot;
        public SimulationActionTargetSnapshot SourceTargetSnapshot
        {
            get => m_SourceTargetSnapshot;
            internal set { value.Retain(); m_SourceTargetSnapshot.Release(); m_SourceTargetSnapshot = value; }
        }
        [ValueSnapshot] ActorId m_TargetActor;
        public ActorId TargetActor
        {
            get => m_TargetActor;
            internal set { value.Value.Retain(); m_TargetActor.Value.Release(); m_TargetActor = value; }
        }
        [ValueSnapshot] public FPVector3 TargetPosition { get; internal set; }
        [ValueSnapshot] public FP TargetYaw { get; internal set; }
        [ValueSnapshot] public FP ReceiverMinimumY { get; internal set; }
        [ValueSnapshot] public FP ReceiverMaximumY { get; internal set; }
        [ValueSnapshot] CombatAttackIdentity m_Attack;
        public CombatAttackIdentity Attack
        {
            get => m_Attack;
            internal set { value.Retain(); m_Attack.Release(); m_Attack = value; }
        }
        [ValueSnapshot, ValueTextRules(Declared = true)] public string ContentId { get; internal set; }
        [ValueSnapshot] CanonicalTextValue m_AttackEventId;
        public CanonicalTextValue AttackEventId
        {
            get => m_AttackEventId;
            internal set { value.Retain(); m_AttackEventId.Release(); m_AttackEventId = value; }
        }
        [ValueSnapshot, ValueTextRules(Declared = true)] public string SourceFeedbackEvent { get; internal set; }
        [ValueSnapshot, ValueTextRules(Declared = true)] public string TargetFeedbackEvent { get; internal set; }
        [ValueSnapshot] public ulong TargetActionInstanceId { get; internal set; }
        [ValueSnapshot] public CombatHitDisposition Disposition { get; internal set; }
        [ValueSnapshot] public bool Heavy { get; internal set; }
        [ValueSnapshot] public int HitType { get; internal set; }
        [ValueSnapshot] public int HitStrengthType { get; internal set; }
        [ValueSnapshot] public int DamageBreakLevel { get; internal set; }
        [ValueSnapshot] public int FrameHalt { get; internal set; }
        [ValueSnapshot] public int AttackerFrameHalt { get; internal set; }
        [ValueSnapshot] public uint HitEffectId { get; internal set; }
        [ValueSnapshot] public SweepContactFields ContactFields { get; internal set; }
        [ValueSnapshot] public FP ContactTime { get; internal set; }
        [ValueSnapshot] public FPVector3 Point { get; internal set; }
        [ValueSnapshot] public FPVector3 Normal { get; internal set; }
        [ValueSnapshot] internal int DefinitionIndex { get; set; }

        internal partial void ClearText();

        public partial void CopyFrom(CombatHitResult value);
    }

    internal sealed class FPCombatQuery
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
        public FPVector3 Origin;
        public FP Yaw;
        public FP PreviousTime;
        public FP Time;
        public FP WindowStartTime;
        public FP BodyStartFraction;
        public FP BodyEndFraction;
        SimulationEventHeader m_Header;
        public SimulationEventHeader Header
        {
            get => m_Header;
            set { value.Retain(); m_Header.Release(); m_Header = value; }
        }
        public FPLocalEnemyAttackWindow LocalWindow;
        internal void ClearText()
        {
            Identity = default; Action = default; NodeId = default; Header = default;
            IntervalEffectHandle = 0; IntervalScope = default; IntervalHolder = default;
        }
    }

    internal sealed class FPCombatQueryStorage
    {
        readonly FPCombatQuery[] m_Queries;
        public FPCombatQueryStorage(int capacity)
        {
            m_Queries = new FPCombatQuery[capacity];
            for (int i = 0; i < capacity; i++)
                m_Queries[i] = new FPCombatQuery();
        }
        public int Count { get; private set; }
        public FPCombatQuery this[int index] => m_Queries[index];
        public FPCombatQuery Append()
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

    [ValueBorrowedSequence(typeof(FPCombatResultStorage), HoldStorage = false)]
    [ValueSnapshot(ClearMethod = nameof(Clear))]
    public sealed partial class FPCombatResultStorage : IReadOnlyList<CombatHitResult>
    {
        [ValueSnapshot] readonly CombatHitResult[] m_Results;
        public FPCombatResultStorage(int capacity)
        {
            m_Results = new CombatHitResult[capacity];
            for (int i = 0; i < capacity; i++)
                m_Results[i] = new CombatHitResult();
        }
        [ValueSnapshot] public int Count { get; private set; }
        public int Capacity => m_Results.Length;
        public ReadOnlySpan<CombatHitResult> Span => m_Results.AsSpan(0, Count);
        public CombatHitResult this[int index] => m_Results[index];
        public CombatHitResult Reserve() => Append();

        public Span<CombatHitResult> Reserve(int count, out FPCombatResultStorage items)
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
        public void CopyFrom(FPCombatResultStorage value)
        {
            Clear();
            for (int i = 0; i < value.Count; i++)
                Append().CopyFrom(value[i]);
        }
        public IEnumerator<CombatHitResult> GetEnumerator() => ((IEnumerable<CombatHitResult>)new ArraySegment<CombatHitResult>(m_Results, 0, Count)).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
