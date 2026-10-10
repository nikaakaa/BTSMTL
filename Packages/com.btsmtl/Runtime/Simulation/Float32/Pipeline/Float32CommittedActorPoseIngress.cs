using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public static class Float32CommittedActorPoseIngress
    {
        public static Float32LocalInputFrame Read(
            this IFloat32LocalInputSourcePort source,
            SimulationTickSourceIdentity tickSource,
            SimulationTick simulationTick,
            SimulationNumericProfile numericProfile,
            int tickRate,
            IReadOnlyList<SimulationActorBinding> roster,
            CommittedActorPoseSnapshot<Float32Vector3, Float32Yaw> committedObservation)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            return source.Read(
                tickSource,
                simulationTick,
                numericProfile,
                tickRate,
                roster,
                committedObservation);
        }
    }

    public sealed class Float32CommittedActorPoseReadPort : ICommittedActorPoseReadPort<Float32Vector3, Float32Yaw>
    {
        readonly SimulationWorldStateStore m_StateStore;
        readonly CommittedActorPoseSnapshot<Float32Vector3, Float32Yaw> m_Observation;

        public Float32CommittedActorPoseReadPort(
            SimulationComponentIdentity backend,
            SimulationWorldStateStore stateStore)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is invalid.", nameof(backend));
            m_StateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            SimulationWorldStateSet initial = stateStore.Current;
            var actors = new CommittedActorPose<Float32Vector3, Float32Yaw>[initial.WorldState.Bodies.Count];
            for (int i = 0; i < actors.Length; i++)
            {
                WorldBodyState body = initial.WorldState.Bodies[i];
                actors[i] = new CommittedActorPose<Float32Vector3, Float32Yaw>(body.ActorId, body.Position, body.Yaw);
            }
            m_Observation = new CommittedActorPoseSnapshot<Float32Vector3, Float32Yaw>(initial.LastCompletedTick, actors);
            Descriptor = Float32PipelineRuntimePortDescriptor.Create(
                Float32PipelineRuntimePortIds.CommittedObservation,
                Float32PipelineRuntimePortIds.CommittedObservationSchema,
                backend.ComponentId,
                CommittedActorPoseSchema.CapabilityHash,
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }

        public CommittedActorPoseSnapshot<Float32Vector3, Float32Yaw> Read()
        {
            SimulationWorldStateSet state = m_StateStore.Current;
            for (int i = 0; i < state.WorldState.Bodies.Count; i++)
            {
                WorldBodyState body = state.WorldState.Bodies[i];
                m_Observation.UpdateActor(body.ActorId, body.Position, body.Yaw);
            }
            m_Observation.PublishTick(state.LastCompletedTick);
            return m_Observation;
        }
    }
}
