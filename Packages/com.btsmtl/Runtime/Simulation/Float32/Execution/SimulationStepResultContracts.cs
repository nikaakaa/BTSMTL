using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public readonly struct CharacterBodySample
    {
        public CharacterBodySample(
            ActorId actorId,
            SimulationTick tick,
            WorldBodyState beforeBody,
            WorldBodyState finalBody,
            Float32Vector3 appliedDisplacement,
            Float32Scalar appliedYawDegrees)
        {
            if (!actorId.IsValid || !tick.IsValid || beforeBody.ActorId != actorId || finalBody.ActorId != actorId)
                throw new ArgumentException("Body sample identity is incomplete.");
            ActorId = actorId;
            Tick = tick;
            BeforeBody = beforeBody;
            FinalBody = finalBody;
            AppliedDisplacement = appliedDisplacement;
            AppliedYawDegrees = appliedYawDegrees;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public WorldBodyState BeforeBody { get; }
        public WorldBodyState FinalBody { get; }
        public Float32Vector3 AppliedDisplacement { get; }
        public Float32Scalar AppliedYawDegrees { get; }
    }

    internal interface IFloat32PipelineProductValueRelease
    {
        void ReleaseOwnedValue();
    }

    public sealed class SimulationActorTickResult : IFloat32PipelineProductValueRelease
    {
        Float32CharacterEvaluationOutput m_Output;
        SimulationActorBinding m_OutputOwner;

        internal SimulationActorTickResult()
        {
        }

        SimulationActorTickResult Reset(
            ActorId actorId,
            SimulationTick tick,
            Float32CharacterRuntimeState state,
            CharacterBodySample bodySample,
            CharacterMotionRequest motion,
            Float32CharacterEvaluationOutput output,
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
            Float32CharacterRuntimeState state,
            CharacterBodySample bodySample,
            CharacterMotionRequest motion,
            Float32CharacterEvaluationOutput output,
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
        public Float32CharacterRuntimeState State { get; private set; }
        public CharacterBodySample BodySample { get; private set; }
        public CharacterMotionRequest Motion { get; private set; }
        public GameplayCommandBatchAdmission IngressAdmission => m_Output.IngressAdmission;
        public IReadOnlyList<GameplayCommandBatchAdmission> CommandBatchRejections => m_Output.CommandBatchRejections;
        public IReadOnlyList<GameplayFact> GameplayFacts => m_Output.Facts;
        public IReadOnlyList<PresentationCommand> PresentationCommands => m_Output.Presentation;
        public IReadOnlyList<SimulationTraceRecord> TraceRecords => m_Output.Trace;

        void IFloat32PipelineProductValueRelease.ReleaseOwnedValue()
        {
            if (m_Output == null)
                return;
            m_OutputOwner.ReturnEvaluationOutput(m_Output);
            m_Output = null;
            m_OutputOwner = null;
        }
    }
}
