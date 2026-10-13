using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class Float32CharacterEvaluationResult
    {
        readonly SimulationActorBinding m_Owner;
        Float32CharacterEvaluationOutput m_Output;
        readonly List<AbilityTimelineAdvancePending> m_TimelineAdvances;
        readonly List<AbilityTimelineStopPending> m_TimelineStops;
        readonly IAbilityTimelineRuntime m_TimelineRuntime;
        Float32CharacterRuntimeState m_CandidateState;
        bool m_Consumed;
        bool m_OutputsCommitted;
        bool m_OutputsTaken;

        internal Float32CharacterEvaluationResult(
            SimulationActorBinding owner,
            ActorId actorId,
            IAbilityTimelineRuntime timelineRuntime,
            int timelineRequestCapacity)
        {
            m_Owner = owner;
            ActorId = actorId;
            m_TimelineRuntime = timelineRuntime;
            m_TimelineAdvances = new List<AbilityTimelineAdvancePending>(timelineRequestCapacity);
            m_TimelineStops = new List<AbilityTimelineStopPending>(timelineRequestCapacity);
        }

        internal Float32CharacterEvaluationResult Reset(
            SimulationTick tick,
            Float32CharacterRuntimeState candidateState,
            Float32CharacterEvaluationOutput output,
            List<AbilityTimelineAdvancePending> timelineAdvances,
            List<AbilityTimelineStopPending> timelineStops)
        {
            m_CandidateState = candidateState;
            Tick = tick;
            m_Output = output;
            m_TimelineAdvances.AddRange(timelineAdvances);
            m_TimelineStops.AddRange(timelineStops);
            m_Consumed = false;
            m_OutputsCommitted = false;
            m_OutputsTaken = false;
            return this;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; private set; }
        internal Float32CharacterRuntimeState CandidateState => m_CandidateState;

        internal void Consume()
        {
            if (m_Consumed)
                throw new InvalidOperationException("Float32 Character evaluation result has already been consumed.");
            m_Output.FinalizeGameplayEffects();
            CompleteTimelineOutputs(true);
            Float32CharacterRuntimeState candidateState = m_CandidateState;
            for (int i = 0; i < m_TimelineAdvances.Count; i++)
            {
                if (m_TimelineRuntime == null)
                    throw new InvalidOperationException("Float32 Character evaluation has Timeline advances without a Timeline runtime.");
                candidateState = candidateState.WithTimelineSnapshot(m_TimelineRuntime.Capture(m_TimelineAdvances[i].RuntimeHandle));
            }
            for (int i = 0; i < m_TimelineStops.Count; i++)
                candidateState = candidateState.WithoutTimelineSnapshot(m_TimelineStops[i].RuntimeHandle);
            candidateState = candidateState.WithoutUnownedTerminalTimelines();
            if (!ReferenceEquals(candidateState, m_CandidateState))
            {
                m_Output.ReplaceStatePageOwner(candidateState);
                m_CandidateState = candidateState;
            }
            m_OutputsCommitted = true;
            m_Consumed = true;
            m_TimelineAdvances.Clear();
            m_TimelineStops.Clear();
        }

        internal void TakeOutputs(out Float32CharacterEvaluationOutput output)
        {
            if (!m_OutputsCommitted || m_OutputsTaken)
                throw new InvalidOperationException("Float32 Character evaluation outputs are not available for transfer.");
            output = m_Output;
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
        }

        void CompleteTimelineOutputs(bool commit)
        {
            if (m_TimelineRuntime == null)
                return;
            for (int i = 0; i < m_TimelineAdvances.Count; i++)
            {
                if (!m_TimelineAdvances[i].IsValid)
                    throw new InvalidOperationException("Float32 Character evaluation has an empty Timeline advance.");
                if (commit)
                    m_TimelineRuntime.Commit(m_TimelineAdvances[i]);
                else
                    m_TimelineRuntime.Discard(m_TimelineAdvances[i]);
            }
            for (int i = 0; i < m_TimelineStops.Count; i++)
            {
                if (!m_TimelineStops[i].IsValid)
                    throw new InvalidOperationException("Float32 Character evaluation has an empty Timeline stop.");
                if (commit)
                    m_TimelineRuntime.CommitStop(m_TimelineStops[i]);
                else
                    m_TimelineRuntime.DiscardStop(m_TimelineStops[i]);
            }
        }
    }
}
