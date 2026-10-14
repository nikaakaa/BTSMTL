using System;
using System.IO;

namespace ThirdPersonSimulation.Fixed
{
    public sealed partial class FixedCombatRuntime : IDisposable
    {
        static readonly StableHash CombatStateSchema = FixedCombatStateBank.CanonicalSchemaHash;

        internal FixedCombatStateBank CreateStateBank()
        {
            var attacks = new FixedCombatAttackState[m_State.Attacks.Length];
            for (int i = 0; i < attacks.Length; i++)
                attacks[i] = new FixedCombatAttackState(m_Participants.Length);
            return new FixedCombatStateBank(attacks, m_State.Halts.Length, m_State.SelfTimeSlows.Length, m_ResultCapacity);
        }

        internal void ReleaseStateBank(FixedCombatStateBank bank) => FixedCombatStateBank.CanonicalClear(bank);

        public void Dispose() => ReleaseStateBank(m_State);

        internal int StateTextCapacity => checked(
            FixedCombatAttackState.CanonicalMaximumTextReferences * m_State.Attacks.Length +
            CombatHitResult.CanonicalMaximumTextReferences * m_ResultCapacity);

        internal int GetMaximumStatePayloadBytes(int maximumSessionPayloadBytes)
        {
            int maximumCharacters = m_StateMaximumIdentityCharacters;
            for (int actor = 0; actor < m_Characters.Roster.Count; actor++)
                foreach (FixedGameplayAbilityExecutionInstallation installation in m_Characters.Roster[actor].AbilityInstallations.Installations)
                {
                    FixedGameplayAbilityExecutionData data = installation.Data;
                    foreach (ProgramExecutionSource entry in data.ExecutionSources)
                        maximumCharacters = Math.Max(maximumCharacters, entry.InvocationCallerClipId?.Length ?? 0);
                    foreach (ProgramConstant constant in data.Constants)
                        if (constant.Kind == ProgramConstantKind.String)
                            maximumCharacters = Math.Max(maximumCharacters, constant.Text.Length);
                }
            long payloadBytes = checked(sizeof(int) + CanonicalContractProtocol.HashEncodedSize +
                FixedCombatStateBank.CanonicalMeasureCapacity(m_State, maximumCharacters));
            return checked((int)Math.Min(maximumSessionPayloadBytes, payloadBytes));
        }

        internal void CaptureTo(FixedCombatStateBank bank) => FixedCombatStateBank.CanonicalCopyInto(m_State, bank);

        internal void RestoreFrom(FixedCombatStateBank bank) => FixedCombatStateBank.CanonicalCopyInto(bank, m_State);

        internal void WriteState(CanonicalWriter writer) => WriteState(writer, m_State);

        internal static void WriteState(CanonicalWriter writer, FixedCombatStateBank state)
        {
            writer.WriteInt32(8);
            writer.WriteHash(CombatStateSchema);
            FixedCombatStateBank.CanonicalWrite(writer, state);
        }

        internal void ReadState(CanonicalReader reader, FixedCombatStateBank bank)
        {
            if (reader.ReadInt32() != 8)
                throw new InvalidDataException("Combat state version is unsupported.");
            reader.RequireSchema(CombatStateSchema, "Combat state");
            reader.DeclaredIdentityReader = m_StateIdentityReader;
            FixedCombatStateBank.CanonicalReadInto(reader, bank);
            reader.RequireComplete();
        }

    }

    [CanonicalContract(OwnsMemberResources = true)]
    internal sealed partial class FixedCombatStateBank
    {
        public readonly FixedCombatAttackState[] Attacks;
        public readonly FixedCombatHalt[] Halts;
        public readonly FixedCombatTimeSlowState[] SelfTimeSlows;
        public FixedCombatTimeSlowState WorldTimeSlow;
        public int WorldTimeSlowOwner;
        public readonly FixedCombatResultStorage Pending;
        public ulong NextResultId;
        public ulong LastTick;

        internal FixedCombatStateBank(FixedCombatAttackState[] attacks, int haltCount, int selfTimeSlowCount, int resultCapacity)
        {
            Attacks = attacks;
            Halts = new FixedCombatHalt[haltCount];
            SelfTimeSlows = new FixedCombatTimeSlowState[selfTimeSlowCount];
            Pending = new FixedCombatResultStorage(resultCapacity);
            NextResultId = 1;
        }
    }
}
