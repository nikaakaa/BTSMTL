using System;

namespace ThirdPersonSimulation.Fixed
{
    internal readonly struct FixedAcceptedHitFact
    {
        internal FixedAcceptedHitFact(ulong actionInstance, ulong segmentGeneration, CanonicalTextValue eventId)
        {
            ActionInstance = actionInstance;
            SegmentGeneration = segmentGeneration;
            EventId = eventId;
        }
        internal readonly ulong ActionInstance;
        internal readonly ulong SegmentGeneration;
        internal readonly CanonicalTextValue EventId;
    }

    internal sealed class FixedCombatEvaluation
    {
        readonly ReadOnlyMemory<FixedCombatHalt> m_Halts;
        readonly int m_ActorIndex;
        internal readonly FixedAcceptedHitFact[] AcceptedHits;
        internal int HitCount;
        internal FixedScalar WorldTimeScale;
        internal FixedCombatTimeSlowState SelfTimeSlow;
        internal CharacterSkillId CounterSkill;
        internal bool SelfTimeSlowChanged;

        internal FixedCombatEvaluation(ReadOnlyMemory<FixedCombatHalt> halts, int actorIndex, int hitCapacity)
        {
            m_Halts = halts;
            m_ActorIndex = actorIndex;
            AcceptedHits = new FixedAcceptedHitFact[hitCapacity];
        }

        internal FixedScalar ActionTimeScale => WorldTimeScale * SelfTimeSlow.CurrentRate;

        internal void OnActionCommitted(CharacterSkillId skill)
        {
            if (SelfTimeSlow.Active && skill == CounterSkill)
            {
                SelfTimeSlow = default;
                SelfTimeSlowChanged = true;
            }
        }

        internal bool IsHalted(ulong actionInstanceId, ulong tick)
        {
            ReadOnlySpan<FixedCombatHalt> halts = m_Halts.Span;
            for (int i = 0; i < halts.Length; i++)
                if (halts[i].ActorIndex == m_ActorIndex && halts[i].ActionInstanceId == actionInstanceId &&
                    tick < halts[i].ResumeTick)
                    return true;
            return false;
        }

        internal bool HasAcceptedHit(ulong actionInstanceId, ulong segmentGeneration, string eventId)
        {
            for (int i = 0; i < HitCount; i++)
                if (AcceptedHits[i].ActionInstance == actionInstanceId &&
                    AcceptedHits[i].SegmentGeneration == segmentGeneration && AcceptedHits[i].EventId.Equals(eventId))
                    return true;
            return false;
        }
    }
}
