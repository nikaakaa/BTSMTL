using System;
using System.IO;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.Fixed
{
    public sealed partial class FixedCombatRuntime
    {
        internal sealed class StateBank
        {
            public readonly FixedCombatAttackState[] Attacks;
            public readonly FixedCombatHalt[] Halts;
            public readonly FixedCombatTimeSlowState[] SelfTimeSlows;
            public FixedCombatTimeSlowState WorldTimeSlow;
            public int WorldTimeSlowOwner;
            public readonly FixedCombatResultStorage Pending;
            public ulong NextResultId;
            public ulong LastTick;

            public StateBank(FixedCombatRuntime owner)
            {
                Attacks = new FixedCombatAttackState[owner.m_AttackStates.Length];
                for (int i = 0; i < Attacks.Length; i++)
                    Attacks[i] = new FixedCombatAttackState(owner.m_AttackStates[i].ActorIndex, owner.m_Participants.Length);
                Halts = new FixedCombatHalt[owner.m_Halts.Length];
                SelfTimeSlows = new FixedCombatTimeSlowState[owner.m_SelfTimeSlows.Length];
                Pending = new FixedCombatResultStorage(owner.m_ResultCapacity);
            }
        }

        internal StateBank CreateStateBank() => new StateBank(this);

        internal void ReleaseStateBank(StateBank bank)
        {
            for (int index = 0; index < bank.Attacks.Length; index++)
                bank.Attacks[index].Active = false;
            bank.Pending.Clear();
        }

        internal int StateTextCapacity => checked(3 * m_AttackStates.Length + 3 * m_ResultCapacity);

        internal void CaptureTo(StateBank bank)
        {
            for (int i = 0; i < m_AttackStates.Length; i++)
                bank.Attacks[i].CopyFrom(m_AttackStates[i]);
            Array.Copy(m_Halts, bank.Halts, m_Halts.Length);
            Array.Copy(m_SelfTimeSlows, bank.SelfTimeSlows, m_SelfTimeSlows.Length);
            bank.WorldTimeSlow = m_WorldTimeSlow;
            bank.WorldTimeSlowOwner = m_WorldTimeSlowOwner;
            bank.Pending.CopyFrom(m_Pending);
            bank.NextResultId = m_NextResultId;
            bank.LastTick = m_LastTick;
        }

        internal void RestoreFrom(StateBank bank)
        {
            for (int i = 0; i < m_AttackStates.Length; i++)
                m_AttackStates[i].CopyFrom(bank.Attacks[i]);
            Array.Copy(bank.Halts, m_Halts, m_Halts.Length);
            Array.Copy(bank.SelfTimeSlows, m_SelfTimeSlows, m_SelfTimeSlows.Length);
            m_WorldTimeSlow = bank.WorldTimeSlow;
            m_WorldTimeSlowOwner = bank.WorldTimeSlowOwner;
            m_Pending.CopyFrom(bank.Pending);
            m_NextResultId = bank.NextResultId;
            m_LastTick = bank.LastTick;
        }

        internal void WriteState(CanonicalWriter writer)
        {
            writer.WriteInt32(7);
            writer.WriteUInt64(m_LastTick);
            writer.WriteUInt64(m_NextResultId);
            writer.WriteInt32(m_WorldTimeSlowOwner);
            WriteTimeSlow(writer, in m_WorldTimeSlow);
            for (int i = 0; i < m_SelfTimeSlows.Length; i++)
                WriteTimeSlow(writer, in m_SelfTimeSlows[i]);
            writer.WriteInt32(m_AttackStates.Length);
            for (int i = 0; i < m_AttackStates.Length; i++)
            {
                FixedCombatAttackState state = m_AttackStates[i];
                writer.WriteBoolean(state.Active);
                if (!state.Active)
                    continue;
                writer.WriteInt32(state.DefinitionIndex);
                WriteIdentity(writer, state.Identity);
                writer.WriteText(state.EventId);
                writer.WriteVector3(state.Origin);
                writer.WriteYaw(state.Yaw);
                writer.WriteInt32(state.TotalCount);
                writer.WriteInt32(state.HitCount);
                writer.WriteUInt64(state.LastQueryTick);
                for (int target = 0; target < state.TargetCounts.Length; target++)
                {
                    writer.WriteInt32(state.TargetCounts[target]);
                    writer.WriteUInt64(state.TargetTicks[target]);
                }
            }
            writer.WriteInt32(m_Halts.Length);
            for (int i = 0; i < m_Halts.Length; i++)
            {
                writer.WriteInt32(m_Halts[i].ActorIndex);
                writer.WriteUInt64(m_Halts[i].ActionInstanceId);
                writer.WriteUInt64(m_Halts[i].ResumeTick);
            }
            writer.WriteInt32(m_Pending.Count);
            for (int i = 0; i < m_Pending.Count; i++)
            {
                CombatHitResult hit = m_Pending[i];
                writer.WriteUInt64(hit.ResultId);
                writer.WriteUInt64(hit.SourceTick);
                writer.WriteInt32(hit.DefinitionIndex);
                writer.WriteInt32(m_ParticipantIndices[hit.TargetActor]);
                writer.WriteVector3(hit.TargetPosition);
                writer.WriteYaw(hit.TargetYaw);
                writer.WriteInt64(hit.ReceiverMinimumY.Raw);
                writer.WriteInt64(hit.ReceiverMaximumY.Raw);
                writer.WriteVector3(hit.SourceTargetSnapshot.Position);
                writer.WriteYaw(hit.SourceTargetSnapshot.Yaw);
                WriteIdentity(writer, hit.Attack);
                writer.WriteText(hit.AttackEventId);
                writer.WriteUInt64(hit.TargetActionInstanceId);
                writer.WriteByte((byte)hit.Disposition);
                writer.WriteUInt32(hit.HitEffectId);
                writer.WriteByte((byte)hit.ContactFields);
                writer.WriteInt64(hit.ContactTime.Raw);
                writer.WriteVector3(hit.Point);
                writer.WriteVector3(hit.Normal);
            }
        }

        internal void ReadState(CanonicalReader reader, StateBank bank)
        {
            if (reader.ReadInt32() != 7)
                throw new InvalidDataException("Combat state version is unsupported.");
            bank.LastTick = reader.ReadUInt64();
            bank.NextResultId = reader.ReadUInt64();
            bank.WorldTimeSlowOwner = reader.ReadInt32();
            bank.WorldTimeSlow = ReadTimeSlow(reader);
            for (int i = 0; i < bank.SelfTimeSlows.Length; i++)
                bank.SelfTimeSlows[i] = ReadTimeSlow(reader);
            if (reader.ReadInt32() != bank.Attacks.Length)
                throw new InvalidDataException("Combat attack storage does not match the prepared roster.");
            for (int i = 0; i < bank.Attacks.Length; i++)
            {
                FixedCombatAttackState state = bank.Attacks[i];
                state.Active = reader.ReadBoolean();
                if (!state.Active)
                    continue;
                state.DefinitionIndex = reader.ReadInt32();
                state.Identity = ReadIdentity(reader, state.DefinitionIndex);
                state.EventId = reader.ReadText();
                state.Origin = reader.ReadVector3();
                state.Yaw = reader.ReadYaw();
                state.TotalCount = reader.ReadInt32();
                state.HitCount = reader.ReadInt32();
                state.LastQueryTick = reader.ReadUInt64();
                for (int target = 0; target < state.TargetCounts.Length; target++)
                {
                    state.TargetCounts[target] = reader.ReadInt32();
                    state.TargetTicks[target] = reader.ReadUInt64();
                }
            }
            if (reader.ReadInt32() != bank.Halts.Length)
                throw new InvalidDataException("Combat halt storage does not match the prepared roster.");
            for (int i = 0; i < bank.Halts.Length; i++)
            {
                bank.Halts[i].ActorIndex = reader.ReadInt32();
                bank.Halts[i].ActionInstanceId = reader.ReadUInt64();
                bank.Halts[i].ResumeTick = reader.ReadUInt64();
            }
            bank.Pending.Clear();
            int resultCount = reader.ReadInt32();
            for (int i = 0; i < resultCount; i++)
            {
                CombatHitResult hit = bank.Pending.Append();
                hit.ResultId = reader.ReadUInt64();
                hit.SourceTick = reader.ReadUInt64();
                hit.DefinitionIndex = reader.ReadInt32();
                FixedPreparedAttack definition = m_Attacks[hit.DefinitionIndex];
                int targetIndex = reader.ReadInt32();
                int sourceIndex = definition.ActorIndex;
                hit.SourceActor = m_Participants[sourceIndex].ActorId;
                hit.TargetActor = m_Participants[targetIndex].ActorId;
                hit.TargetPosition = reader.ReadVector3();
                hit.TargetYaw = reader.ReadYaw();
                hit.ReceiverMinimumY = FixedScalar.FromRaw(reader.ReadInt64());
                hit.ReceiverMaximumY = FixedScalar.FromRaw(reader.ReadInt64());
                hit.SourceTargetSnapshot = new SimulationActionTargetSnapshot(
                    hit.SourceActor.Value, reader.ReadVector3(), reader.ReadYaw());
                hit.Attack = ReadIdentity(reader, hit.DefinitionIndex);
                hit.AttackEventId = reader.ReadText();
                hit.ContentId = definition.ContentId;
                hit.TargetActionInstanceId = reader.ReadUInt64();
                hit.Disposition = (CombatHitDisposition)reader.ReadByte();
                hit.Heavy = definition.Property.IsHeavyAttack;
                CharacterCombatRuntimeBinding source = m_Participants[sourceIndex].Character;
                CharacterCombatRuntimeBinding target = m_Participants[targetIndex].Character;
                hit.SourceFeedbackEvent = hit.Disposition == CombatHitDisposition.Evaded || m_Participants[sourceIndex].CharacterIndex < 0 ? string.Empty : source.LandedEvent;
                hit.TargetFeedbackEvent = m_Participants[targetIndex].CharacterIndex < 0 ? string.Empty : hit.Disposition == CombatHitDisposition.Evaded ? target.PerfectEvadeEvent :
                    hit.Disposition == CombatHitDisposition.Parried
                    ? hit.Heavy ? target.ParryHeavyEvent : target.ParryLightEvent
                    : hit.Heavy ? target.HitHeavyEvent : target.HitLightEvent;
                hit.HitType = definition.Property.HitType;
                hit.HitStrengthType = definition.Property.HitStrengthType;
                hit.DamageBreakLevel = definition.Property.DamageBreakLevel;
                SetFrameHalts(hit, targetIndex, definition.Property);
                hit.HitEffectId = reader.ReadUInt32();
                hit.ContactFields = (SweepContactFields)reader.ReadByte();
                hit.ContactTime = FixedScalar.FromRaw(reader.ReadInt64());
                hit.Point = reader.ReadVector3();
                hit.Normal = reader.ReadVector3();
            }
            reader.RequireComplete();
        }

        static void WriteTimeSlow(CanonicalWriter writer, in FixedCombatTimeSlowState state)
        {
            writer.WriteBoolean(state.Active);
            writer.WriteInt64(state.Age.Raw);
            writer.WriteInt64(state.Rate.Raw);
        }

        static FixedCombatTimeSlowState ReadTimeSlow(CanonicalReader reader) => new FixedCombatTimeSlowState
        {
            Active = reader.ReadBoolean(),
            Age = FixedScalar.FromRaw(reader.ReadInt64()),
            Rate = FixedScalar.FromRaw(reader.ReadInt64())
        };

        static void WriteIdentity(CanonicalWriter writer, in CombatAttackIdentity identity)
        {
            writer.WriteByte((byte)identity.SourceKind);
            if (identity.SourceKind == CombatAttackSourceKind.LocalEnemyAttack)
            {
                writer.WriteText(identity.LocalActor.Value);
                writer.WriteUInt64(identity.LocalReleaseSequence);
                writer.WriteText(identity.LocalWindowId);
                return;
            }
            writer.WriteUInt64(identity.ActionInstanceId);
            writer.WriteUInt64(identity.SegmentGeneration);
            writer.WriteText(identity.SkillId.Value);
            writer.WriteInt32(identity.Operation.Value);
            writer.WriteText(identity.ClipId);
            writer.WriteUInt64(identity.PlaybackGeneration);
            writer.WriteUInt64(identity.BranchRevision);
            writer.WriteInt32(identity.Cycle);
            writer.WriteUInt64(identity.TimelineRuntimeHandle);
        }

        CombatAttackIdentity ReadIdentity(CanonicalReader reader, int definitionIndex)
        {
            CombatAttackSourceKind kind = (CombatAttackSourceKind)reader.ReadByte();
            if (kind == CombatAttackSourceKind.LocalEnemyAttack)
                return new CombatAttackIdentity(new ActorId(reader.ReadText()), reader.ReadUInt64(), reader.ReadText());
            if (kind != CombatAttackSourceKind.CharacterAction)
                throw new InvalidDataException("Combat attack source kind is unsupported.");
            return new CombatAttackIdentity(reader.ReadUInt64(), reader.ReadUInt64(), new CharacterSkillId(m_Characters.Roster[m_Participants[m_Attacks[definitionIndex].ActorIndex].CharacterIndex].DeclaredIdentityReader(reader)),
                new OperationHandle(reader.ReadInt32()), reader.ReadText(), reader.ReadUInt64(),
                reader.ReadUInt64(), reader.ReadInt32(), reader.ReadUInt64());
        }
    }
}
