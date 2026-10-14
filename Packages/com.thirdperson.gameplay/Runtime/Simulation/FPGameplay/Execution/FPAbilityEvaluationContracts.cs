using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPCharacterEvaluationResult
    {
        readonly SimulationActorBinding m_Owner;
        FPCharacterEvaluationOutput m_Output;
        readonly List<AbilityTimelineAdvancePending> m_TimelineAdvances;
        readonly List<AbilityTimelineStopPending> m_TimelineStops;
        readonly List<AbilityTimelineRuntimeSnapshot> m_TimelineSnapshots;
        readonly IAbilityTimelineRuntime m_TimelineRuntime;
        FPCharacterRuntimeState m_CandidateState;
        bool m_Consumed;
        bool m_OutputsCommitted;
        bool m_OutputsTaken;

        internal FPCharacterEvaluationResult(
            SimulationActorBinding owner,
            ActorId actorId,
            IAbilityTimelineRuntime timelineRuntime,
            int timelineRequestCapacity)
        {
            m_Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            ActorId = actorId;
            m_TimelineRuntime = timelineRuntime;
            m_TimelineAdvances = new List<AbilityTimelineAdvancePending>(timelineRequestCapacity);
            m_TimelineStops = new List<AbilityTimelineStopPending>(timelineRequestCapacity);
            m_TimelineSnapshots = new List<AbilityTimelineRuntimeSnapshot>(
                checked(FPTimelineSnapshotPageBank.MaximumSnapshotCount + timelineRequestCapacity));
        }

        internal FPCharacterEvaluationResult Reset(
            SimulationTick tick,
            FPCharacterRuntimeState candidateState,
            FPCharacterEvaluationOutput output,
            List<AbilityTimelineAdvancePending> timelineAdvances,
            List<AbilityTimelineStopPending> timelineStops)
        {
            if (!tick.IsValid)
                throw new ArgumentException("FP Character evaluation result identity is incomplete.");
            m_CandidateState = candidateState;
            if (candidateState.LastCompletedTick != tick.Value)
                throw new InvalidOperationException("FP Character evaluation result binding is invalid.");
            Tick = tick;
            m_Output = output ?? throw new ArgumentNullException(nameof(output));
            m_TimelineAdvances.AddRange(timelineAdvances);
            m_TimelineStops.AddRange(timelineStops);
            m_Consumed = false;
            m_OutputsCommitted = false;
            m_OutputsTaken = false;
            return this;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; private set; }
        internal FPCharacterRuntimeState CandidateState => m_CandidateState;
        internal FPCombatQueryStorage AttackQueries => m_Owner.CombatQueries;
        internal FPCharacterEvaluationOutput Output => m_Output;

        internal void Consume()
        {
            if (m_Consumed)
                throw new InvalidOperationException("FP Character evaluation result has already been consumed.");
            m_Output.FinalizeGameplayEffects();
            CompleteTimelineOutputs(true);
            if (m_TimelineAdvances.Count != 0 && m_TimelineRuntime == null)
                throw new InvalidOperationException("FP Character evaluation has Timeline advances without a Timeline runtime.");
            FPCharacterRuntimeState candidateState = m_CandidateState.WithTimelineOutputs(
                m_TimelineRuntime,
                m_TimelineAdvances,
                m_TimelineStops,
                m_TimelineSnapshots);
            if (!candidateState.SharesStateWith(m_CandidateState))
            {
                m_Output.ReplaceStatePageOwner(candidateState);
                m_CandidateState = candidateState;
            }
            m_OutputsCommitted = true;
            m_Consumed = true;
            m_TimelineAdvances.Clear();
            m_TimelineStops.Clear();
            m_TimelineSnapshots.Clear();
        }

        internal void TakeOutputs(
            out FPCharacterEvaluationOutput output)
        {
            if (!m_OutputsCommitted || m_OutputsTaken)
                throw new InvalidOperationException("FP Character evaluation outputs are not available for transfer.");
            output = m_Output ?? throw new InvalidOperationException("FP Character evaluation output has already been transferred.");
            m_Output = null;
            m_OutputsTaken = true;
        }

        internal void DiscardUnconsumed()
        {
            if (!m_Consumed)
                CompleteTimelineOutputs(false);
            m_Owner.MotionContributions.Clear();
            if (m_Output != null)
            {
                m_Owner.ReturnEvaluationOutput(m_Output);
                m_Output = null;
            }
            m_Consumed = true;
            m_TimelineAdvances.Clear();
            m_TimelineStops.Clear();
            m_TimelineSnapshots.Clear();
        }

        void CompleteTimelineOutputs(bool commit)
        {
            if (m_TimelineRuntime == null)
                return;
            for (int i = 0; i < m_TimelineAdvances.Count; i++)
            {
                if (!m_TimelineAdvances[i].IsValid)
                    throw new InvalidOperationException("FP Character evaluation has an empty Timeline advance.");
                if (commit)
                    m_TimelineRuntime.Commit(m_TimelineAdvances[i]);
                else
                    m_TimelineRuntime.Discard(m_TimelineAdvances[i]);
            }
            for (int i = 0; i < m_TimelineStops.Count; i++)
            {
                if (!m_TimelineStops[i].IsValid)
                    throw new InvalidOperationException("FP Character evaluation has an empty Timeline stop.");
                if (commit)
                    m_TimelineRuntime.CommitStop(m_TimelineStops[i]);
                else
                    m_TimelineRuntime.DiscardStop(m_TimelineStops[i]);
            }
        }
    }
}
