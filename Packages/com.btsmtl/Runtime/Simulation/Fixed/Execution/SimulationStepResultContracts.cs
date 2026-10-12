using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public readonly struct CharacterBodySample
    {
        public CharacterBodySample(
            ActorId actorId,
            SimulationTick tick,
            WorldBodyState beforeBody,
            WorldBodyState finalBody,
            FixedVector3 appliedDisplacement,
            FixedScalar appliedYawDegrees,
            FixedScalar worldTimeScale,
            FixedScalar ownerTimeScale)
        {
            if (!actorId.IsValid || !tick.IsValid || beforeBody.ActorId != actorId || finalBody.ActorId != actorId)
                throw new ArgumentException("Body sample identity is incomplete.");
            ActorId = actorId;
            Tick = tick;
            BeforeBody = beforeBody;
            FinalBody = finalBody;
            AppliedDisplacement = appliedDisplacement;
            AppliedYawDegrees = appliedYawDegrees;
            WorldTimeScale = worldTimeScale;
            OwnerTimeScale = ownerTimeScale;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public WorldBodyState BeforeBody { get; }
        public WorldBodyState FinalBody { get; }
        public FixedVector3 AppliedDisplacement { get; }
        public FixedScalar AppliedYawDegrees { get; }
        public FixedScalar WorldTimeScale { get; }
        public FixedScalar OwnerTimeScale { get; }
    }

    internal interface IFixedPipelineProductValueRelease
    {
        void ReleaseOwnedValue();
    }

    public sealed class SimulationActorTickResult : IFixedPipelineProductValueRelease
    {
        FixedCharacterEvaluationOutput m_Output;
        SimulationActorBinding m_OutputOwner;

        internal SimulationActorTickResult()
        {
        }

        SimulationActorTickResult Reset(
            ActorId actorId,
            SimulationTick tick,
            FixedCharacterRuntimeState state,
            CharacterBodySample bodySample,
            CharacterMotionRequest motion,
            FixedCharacterEvaluationOutput output,
            SimulationActorBinding outputOwner)
        {
            State = state;
            ActorId = actorId;
            Tick = tick;
            BodySample = bodySample;
            Motion = motion;
            m_Output = output;
            m_OutputOwner = outputOwner;
            return this;
        }

        internal static SimulationActorTickResult FromOwnedOutputs(
            ActorId actorId,
            SimulationTick tick,
            FixedCharacterRuntimeState state,
            CharacterBodySample bodySample,
            CharacterMotionRequest motion,
            FixedCharacterEvaluationOutput output,
            SimulationActorBinding outputOwner)
        {
            return output.TickResult.Reset(
                actorId,
                tick,
                state,
                bodySample,
                motion,
                output,
                outputOwner);
        }

        public ActorId ActorId { get; private set; }
        public SimulationTick Tick { get; private set; }
        public FixedCharacterRuntimeState State { get; private set; }
        public CharacterBodySample BodySample { get; private set; }
        public CharacterMotionRequest Motion { get; private set; }
        public GameplayCommandBatchAdmission IngressAdmission => m_Output.IngressAdmission;
        public IReadOnlyList<GameplayCommandBatchAdmission> CommandBatchRejections => m_Output.CommandBatchRejections;
        public IReadOnlyList<GameplayFact> GameplayFacts => m_Output.Facts;
        public IReadOnlyList<CombatHitResult> AttackResults => m_Output.AttackResults;
        public IReadOnlyList<PresentationCommand> PresentationCommands => m_Output.Presentation;
        public IReadOnlyList<SimulationTraceRecord> TraceRecords => m_Output.Trace;
#if UNITY_EDITOR
        public long EvaluationStateElapsedStopwatchTicks => m_Output.EvaluationStateElapsedStopwatchTicks;
        public long EvaluationInputsElapsedStopwatchTicks => m_Output.EvaluationInputsElapsedStopwatchTicks;
        public long EvaluationInputProjectionOwnerElapsedStopwatchTicks => m_Output.EvaluationInputProjectionOwnerElapsedStopwatchTicks;
        public long EvaluationInputProjectionValueElapsedStopwatchTicks => m_Output.EvaluationInputProjectionValueElapsedStopwatchTicks;
        public long EvaluationInputProjectionStampElapsedStopwatchTicks => m_Output.EvaluationInputProjectionStampElapsedStopwatchTicks;
        public long EvaluationTraceHeaderElapsedStopwatchTicks => m_Output.EvaluationTraceHeaderElapsedStopwatchTicks;
        public long EvaluationTraceStoreElapsedStopwatchTicks => m_Output.EvaluationTraceStoreElapsedStopwatchTicks;
        public int EvaluationTraceHeaderCalls => m_Output.EvaluationTraceHeaderCalls;
        public int EvaluationTraceStoreCalls => m_Output.EvaluationTraceStoreCalls;
        public long EvaluationInputSnapshotPrepareElapsedStopwatchTicks => m_Output.EvaluationInputSnapshotPrepareElapsedStopwatchTicks;
        public long EvaluationInputSnapshotDiscardElapsedStopwatchTicks => m_Output.EvaluationInputSnapshotDiscardElapsedStopwatchTicks;
        public long EvaluationInputBindingReadElapsedStopwatchTicks => m_Output.EvaluationInputBindingReadElapsedStopwatchTicks;
        public long EvaluationInputBindingProjectElapsedStopwatchTicks => m_Output.EvaluationInputBindingProjectElapsedStopwatchTicks;
        public int EvaluationInputBindingCalls => m_Output.EvaluationInputBindingCalls;
        public long EvaluationInputsPrepareElapsedStopwatchTicks => m_Output.EvaluationInputsPrepareElapsedStopwatchTicks;
        public long EvaluationInputsIngressElapsedStopwatchTicks => m_Output.EvaluationInputsIngressElapsedStopwatchTicks;
        public long EvaluationInputsBindingsElapsedStopwatchTicks => m_Output.EvaluationInputsBindingsElapsedStopwatchTicks;
        public long EvaluationInputsBlackboardElapsedStopwatchTicks => m_Output.EvaluationInputsBlackboardElapsedStopwatchTicks;
        public long EvaluationControlElapsedStopwatchTicks => m_Output.EvaluationControlElapsedStopwatchTicks;
        public long EvaluationAbilitiesElapsedStopwatchTicks => m_Output.EvaluationAbilitiesElapsedStopwatchTicks;
        public long EvaluationAbilitiesTickElapsedStopwatchTicks => m_Output.EvaluationAbilitiesTickElapsedStopwatchTicks;
        public long EvaluationAbilitiesExistingElapsedStopwatchTicks => m_Output.EvaluationAbilitiesExistingElapsedStopwatchTicks;
        public long EvaluationAbilitiesPendingElapsedStopwatchTicks => m_Output.EvaluationAbilitiesPendingElapsedStopwatchTicks;
        public long EvaluationAbilitiesActiveElapsedStopwatchTicks => m_Output.EvaluationAbilitiesActiveElapsedStopwatchTicks;
        public long EvaluationAbilitiesActiveTriggersElapsedStopwatchTicks => m_Output.EvaluationAbilitiesActiveTriggersElapsedStopwatchTicks;
        public long EvaluationAbilitiesActiveControlElapsedStopwatchTicks => m_Output.EvaluationAbilitiesActiveControlElapsedStopwatchTicks;
        public long EvaluationAbilitiesActiveOutputElapsedStopwatchTicks => m_Output.EvaluationAbilitiesActiveOutputElapsedStopwatchTicks;
        public int EvaluationAbilitiesActiveControlCalls => m_Output.EvaluationAbilitiesActiveControlCalls;
        public long EvaluationAbilitiesMotionElapsedStopwatchTicks => m_Output.EvaluationAbilitiesMotionElapsedStopwatchTicks;
        public long EvaluationAbilitiesRulesElapsedStopwatchTicks => m_Output.EvaluationAbilitiesRulesElapsedStopwatchTicks;
        public long EvaluationMotionElapsedStopwatchTicks => m_Output.EvaluationMotionElapsedStopwatchTicks;
        public long EvaluationCommitElapsedStopwatchTicks => m_Output.EvaluationCommitElapsedStopwatchTicks;
        public long ValueObservationElapsedStopwatchTicks => m_Output.ValueTraces.ObservationElapsedStopwatchTicks;
        public int ValueObservationCalls => m_Output.ValueTraces.ObservationCalls;
#endif

        void IFixedPipelineProductValueRelease.ReleaseOwnedValue()
        {
            if (m_Output == null)
                return;
            m_OutputOwner.ReturnEvaluationOutput(m_Output);
            m_Output = null;
            m_OutputOwner = null;
        }

    }
}
