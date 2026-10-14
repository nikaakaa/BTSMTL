using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    [ValueOperations]
    public readonly partial struct CharacterBodySample
    {
        public CharacterBodySample(
            ActorId actorId,
            SimulationTick tick,
            WorldBodyState beforeBody,
            WorldBodyState finalBody,
            Float32Vector3 appliedDisplacement,
            Float32Scalar appliedYawDegrees)
        {
            ActorId = actorId;
            Tick = tick;
            BeforeBody = beforeBody;
            FinalBody = finalBody;
            AppliedDisplacement = appliedDisplacement;
            AppliedYawDegrees = appliedYawDegrees;
            RequireRecord();
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public WorldBodyState BeforeBody { get; }
        public WorldBodyState FinalBody { get; }
        public Float32Vector3 AppliedDisplacement { get; }
        public Float32Scalar AppliedYawDegrees { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!ActorId.IsValid || !Tick.IsValid || BeforeBody.ActorId != ActorId || FinalBody.ActorId != ActorId)
                throw new ArgumentException("Body sample identity is incomplete.");
        }
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
            Motion.ActionOwnerIdentity.Retain();
            Motion.GameplayResultOwnerIdentity.Retain();
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
            SimulationActorTickResult result = output.TickResult.Reset(
                actorId,
                tick,
                state,
                bodySample,
                motion,
                output,
                outputOwner);
            outputOwner.MotionContributions.Clear();
            return result;
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
            Motion.ActionOwnerIdentity.Release();
            Motion.GameplayResultOwnerIdentity.Release();
            m_OutputOwner.ReturnEvaluationOutput(m_Output);
            m_Output = null;
            m_OutputOwner = null;
        }
    }
}
