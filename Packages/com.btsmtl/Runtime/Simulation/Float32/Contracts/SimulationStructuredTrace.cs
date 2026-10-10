using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public enum SimulationStructuredTraceKind : byte { None, Motion, EquipmentChange, EquipmentSlot, EquipmentPending, EquipmentResolved, Condition }

    public readonly struct SimulationConditionTrace
    {
        internal SimulationConditionTrace(SimulationOperationCode operation, in AbilityStateValue value)
        {
            Operation = operation; Kind = value.Kind; Boolean = value.Boolean;
            Scalar = value.Kind == ProgramStateValueKind.Yaw ? value.Yaw.Degrees : value.Scalar;
            X = value.Kind == ProgramStateValueKind.Vector2 ? value.Vector2.X : value.Vector3.X;
            Y = value.Kind == ProgramStateValueKind.Vector2 ? value.Vector2.Y : value.Vector3.Y;
            Z = value.Vector3.Z;
        }
        public readonly SimulationOperationCode Operation;
        public readonly ProgramStateValueKind Kind;
        public readonly bool Boolean;
        public readonly Float32Scalar Scalar, X, Y, Z;
    }

    public readonly struct SimulationMotionTrace
    {
        public SimulationMotionTrace(SimulationMotionTraceDetailKind kind, Float32Vector3 displacement,
            Float32Scalar yaw, in CommittedMovementPlaybackClock clock, CanonicalTextValue owner = default,
            int channel = 0, int blend = 0, int priority = 0, Float32Scalar weight = default,
            Float32Vector2 planarBasis = default, bool claimsLowerChannels = false, bool hasMotion = false,
            int participatingSources = 0, ulong fingerprint = 0)
        {
            Kind = kind; Displacement = displacement; Yaw = yaw; Clock = clock; Owner = owner;
            Channel = channel; Blend = blend; Priority = priority; Weight = weight; PlanarBasis = planarBasis;
            ClaimsLowerChannels = claimsLowerChannels; HasMotion = hasMotion;
            ParticipatingSources = participatingSources; Fingerprint = fingerprint;
        }
        public readonly SimulationMotionTraceDetailKind Kind;
        public readonly Float32Vector3 Displacement;
        public readonly Float32Scalar Yaw, Weight;
        public readonly CommittedMovementPlaybackClock Clock;
        public readonly CanonicalTextValue Owner;
        public readonly int Channel, Blend, Priority, ParticipatingSources;
        public readonly Float32Vector2 PlanarBasis;
        public readonly bool ClaimsLowerChannels, HasMotion;
        public readonly ulong Fingerprint;
    }

    public readonly struct SimulationEquipmentChangeTrace
    {
        internal SimulationEquipmentChangeTrace(SimulationOperationCode operation, in EquipmentChangeOutcome result)
        { Operation = operation; Succeeded = result.Succeeded; ChangeId = result.ChangeId; Failure = result.Failure; }
        public readonly SimulationOperationCode Operation;
        public readonly bool Succeeded;
        public readonly EquipmentChangeId ChangeId;
        public readonly EquipmentChangeFailure Failure;
    }

    public readonly struct SimulationEquipmentSlotTrace
    {
        readonly SimulationEquipmentTraceHandles m_Handles;
        internal SimulationEquipmentSlotTrace(SimulationEquipmentTraceHandles handles) : this() { m_Handles = handles; }
        SimulationEquipmentSlotTrace(in EquipmentSlotState value, SimulationEquipmentTraceHandles handles)
        {
            SlotId = value.SlotId; EquipmentId = value.EquipmentId; FeatureId = value.FeatureId;
            Revision = value.Revision; Generation = value.Generation; TagSource = value.TagSource; m_Handles = handles;
        }
        public readonly EquipmentSlotId SlotId;
        public readonly EquipmentId EquipmentId;
        public readonly EquipmentFeatureId FeatureId;
        public readonly ulong Revision, Generation;
        public readonly GameplayTagSourceIdentityValue TagSource;
        public IReadOnlyList<ulong> PassiveEffectHandles => m_Handles;
        internal SimulationEquipmentSlotTrace Capture(in EquipmentSlotState value)
        { m_Handles.Capture(value.PassiveEffectHandles); return new SimulationEquipmentSlotTrace(in value, m_Handles); }
        internal SimulationEquipmentSlotTrace Clear() { m_Handles.Clear(); return new SimulationEquipmentSlotTrace(m_Handles); }
    }

    internal sealed class SimulationEquipmentTraceHandles : IReadOnlyList<ulong>
    {
        readonly ulong[] m_Values;
        internal SimulationEquipmentTraceHandles(int count) => m_Values = new ulong[count];
        public int Count { get; private set; }
        public ulong this[int index] => m_Values[index];
        internal void Capture(IReadOnlyList<ulong> values)
        { Count = values.Count; for (int index = 0; index < Count; index++) m_Values[index] = values[index]; }
        internal void Clear() { Array.Clear(m_Values, 0, Count); Count = 0; }
        public IEnumerator<ulong> GetEnumerator() { for (int index = 0; index < Count; index++) yield return m_Values[index]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct SimulationStructuredTrace
    {
        readonly SimulationStructuredTraceStorage m_Storage;
        readonly int m_Index;
        internal SimulationStructuredTrace(SimulationStructuredTraceStorage storage, int index, SimulationStructuredTraceKind kind)
        { m_Storage = storage; m_Index = index; Kind = kind; }
        public SimulationStructuredTraceKind Kind { get; }
        public ref readonly SimulationMotionTrace Motion => ref m_Storage.Motion(m_Index);
        public ref readonly SimulationEquipmentChangeTrace EquipmentChange => ref m_Storage.EquipmentChange(m_Index);
        public ref readonly SimulationConditionTrace Condition => ref m_Storage.Condition(m_Index);
        public ref readonly SimulationEquipmentSlotTrace EquipmentSlot => ref m_Storage.EquipmentSlot(m_Index);
        public ref readonly PendingEquipmentChange EquipmentPending => ref m_Storage.EquipmentPending(m_Index);
        internal void Retain()
        {
            if (Kind == SimulationStructuredTraceKind.Motion) Motion.Owner.Retain();
            else if (Kind == SimulationStructuredTraceKind.EquipmentSlot)
            { EquipmentSlot.SlotId.Retain(); EquipmentSlot.TagSource.Retain(); }
            else if (Kind == SimulationStructuredTraceKind.EquipmentPending || Kind == SimulationStructuredTraceKind.EquipmentResolved)
                EquipmentPending.SlotId.Retain();
        }
        internal void Release()
        {
            if (Kind == SimulationStructuredTraceKind.Motion) Motion.Owner.Release();
            else if (Kind == SimulationStructuredTraceKind.EquipmentSlot)
            { EquipmentSlot.TagSource.Release(); EquipmentSlot.SlotId.Release(); }
            else if (Kind == SimulationStructuredTraceKind.EquipmentPending || Kind == SimulationStructuredTraceKind.EquipmentResolved)
                EquipmentPending.SlotId.Release();
        }
    }

    internal sealed class SimulationStructuredTraceStorage
    {
        readonly SimulationMotionTrace[] m_Motion;
        readonly SimulationEquipmentChangeTrace[] m_Changes;
        readonly SimulationConditionTrace[] m_Conditions;
        readonly SimulationEquipmentSlotTrace[] m_Slots;
        readonly PendingEquipmentChange[] m_Pending;
        readonly int[] m_MotionStarts, m_ChangeStarts, m_SlotStarts, m_ConditionStarts;
        readonly int[] m_MotionCounts, m_ChangeCounts, m_SlotCounts, m_PendingCounts, m_ConditionCounts;
        int m_CharacterMotionCount;

        internal SimulationStructuredTraceStorage(IReadOnlyList<Float32GameplayAbilityExecutionInstallation> installations, int traceRecordsPerTick)
        {
            int count = installations.Count;
            m_MotionStarts = new int[count]; m_ChangeStarts = new int[count]; m_SlotStarts = new int[count];
            m_MotionCounts = new int[count]; m_ChangeCounts = new int[count]; m_SlotCounts = new int[count]; m_PendingCounts = new int[count];
            m_ConditionStarts = new int[count]; m_ConditionCounts = new int[count];
            int motions = 4, changes = 0, slots = 0, conditions = 0;
            for (int index = 0; index < count; index++)
            {
                Float32GameplayAbilityExecutionInstallation installation = installations[index];
                m_MotionStarts[index] = motions; m_ChangeStarts[index] = changes; m_SlotStarts[index] = slots; m_ConditionStarts[index] = conditions;
                bool hasMotion = false, hasChange = false, hasCondition = false;
                for (int operation = 0; operation < installation.Layout.Operations.Count; operation++)
                {
                    SimulationOperationCode code = installation.Layout.Operations[operation].Code;
                    hasMotion |= code == SimulationOperationCode.LocomotionInputMotion;
                    hasChange |= code == SimulationOperationCode.RequestEquipmentChange || code == SimulationOperationCode.BeginEquipmentChange ||
                        code == SimulationOperationCode.CommitEquipmentChange || code == SimulationOperationCode.CancelEquipmentChange;
                    hasCondition |= code == SimulationOperationCode.InputVector2 || code == SimulationOperationCode.InputVector2Magnitude ||
                        code == SimulationOperationCode.MoveFacingAngle || code == SimulationOperationCode.CharacterStateRead ||
                        code == SimulationOperationCode.Compare || code == SimulationOperationCode.And || code == SimulationOperationCode.Or ||
                        code == SimulationOperationCode.Not || code == SimulationOperationCode.ConditionResult;
                }
                int executions = Math.Min(installation.Control.MaximumOperationEvaluations, checked(traceRecordsPerTick + 1));
                if (hasMotion) motions = checked(motions + executions);
                if (hasChange) changes = checked(changes + executions);
                if (hasCondition) conditions = checked(conditions + executions);
                if (installation.EquipmentLayout != null) slots = checked(slots + installation.EquipmentLayout.Slots.Count);
            }
            m_Motion = new SimulationMotionTrace[motions]; m_Changes = new SimulationEquipmentChangeTrace[changes];
            m_Conditions = new SimulationConditionTrace[conditions];
            m_Slots = new SimulationEquipmentSlotTrace[slots]; m_Pending = new PendingEquipmentChange[checked(count * 2)];
            for (int invocation = 0; invocation < count; invocation++)
            {
                EquipmentProgramLayout layout = installations[invocation].EquipmentLayout;
                if (layout == null) continue;
                int maximum = 0;
                for (int feature = 0; feature < layout.Features.Count; feature++) maximum = Math.Max(maximum, layout.Features[feature].PassiveEffects.Count);
                for (int slot = 0; slot < layout.Slots.Count; slot++)
                    m_Slots[m_SlotStarts[invocation] + slot] = new SimulationEquipmentSlotTrace(new SimulationEquipmentTraceHandles(maximum));
            }
        }

        internal ref readonly SimulationMotionTrace Motion(int index) => ref m_Motion[index];
        internal ref readonly SimulationEquipmentChangeTrace EquipmentChange(int index) => ref m_Changes[index];
        internal ref readonly SimulationConditionTrace Condition(int index) => ref m_Conditions[index];
        internal ref readonly SimulationEquipmentSlotTrace EquipmentSlot(int index) => ref m_Slots[index];
        internal ref readonly PendingEquipmentChange EquipmentPending(int index) => ref m_Pending[index];
        internal SimulationStructuredTrace CaptureMotion(int invocation, in SimulationMotionTrace value)
        {
            int index = m_MotionStarts[invocation] + m_MotionCounts[invocation]++;
            m_Motion[index] = value; return new SimulationStructuredTrace(this, index, SimulationStructuredTraceKind.Motion);
        }
        internal SimulationStructuredTrace CaptureCharacterMotion(in SimulationMotionTrace value)
        {
            int index = m_CharacterMotionCount++;
            m_Motion[index] = value; return new SimulationStructuredTrace(this, index, SimulationStructuredTraceKind.Motion);
        }
        internal SimulationStructuredTrace CaptureChange(int invocation, SimulationOperationCode operation, in EquipmentChangeOutcome value)
        {
            int index = m_ChangeStarts[invocation] + m_ChangeCounts[invocation]++;
            m_Changes[index] = new SimulationEquipmentChangeTrace(operation, in value);
            return new SimulationStructuredTrace(this, index, SimulationStructuredTraceKind.EquipmentChange);
        }
        internal SimulationStructuredTrace CaptureCondition(int invocation, SimulationOperationCode operation, in AbilityStateValue value)
        {
            int index = m_ConditionStarts[invocation] + m_ConditionCounts[invocation]++;
            m_Conditions[index] = new SimulationConditionTrace(operation, in value);
            return new SimulationStructuredTrace(this, index, SimulationStructuredTraceKind.Condition);
        }
        internal SimulationStructuredTrace CaptureSlot(int invocation, in EquipmentSlotState value)
        {
            int index = m_SlotStarts[invocation] + m_SlotCounts[invocation]++;
            m_Slots[index] = m_Slots[index].Capture(in value); return new SimulationStructuredTrace(this, index, SimulationStructuredTraceKind.EquipmentSlot);
        }
        internal SimulationStructuredTrace CapturePending(int invocation, in PendingEquipmentChange value, bool resolved)
        {
            int index = invocation * 2 + m_PendingCounts[invocation]++;
            m_Pending[index] = value;
            return new SimulationStructuredTrace(this, index, resolved ? SimulationStructuredTraceKind.EquipmentResolved : SimulationStructuredTraceKind.EquipmentPending);
        }
        internal void Clear()
        {
            Array.Clear(m_Motion, 0, m_CharacterMotionCount); m_CharacterMotionCount = 0;
            for (int invocation = 0; invocation < m_MotionCounts.Length; invocation++)
            {
                Array.Clear(m_Motion, m_MotionStarts[invocation], m_MotionCounts[invocation]);
                Array.Clear(m_Changes, m_ChangeStarts[invocation], m_ChangeCounts[invocation]);
                Array.Clear(m_Conditions, m_ConditionStarts[invocation], m_ConditionCounts[invocation]);
                for (int slot = 0; slot < m_SlotCounts[invocation]; slot++)
                {
                    int index = m_SlotStarts[invocation] + slot;
                    m_Slots[index] = m_Slots[index].Clear();
                }
                Array.Clear(m_Pending, invocation * 2, m_PendingCounts[invocation]);
                m_MotionCounts[invocation] = 0; m_ChangeCounts[invocation] = 0; m_SlotCounts[invocation] = 0; m_PendingCounts[invocation] = 0;
                m_ConditionCounts[invocation] = 0;
            }
        }
    }
}
