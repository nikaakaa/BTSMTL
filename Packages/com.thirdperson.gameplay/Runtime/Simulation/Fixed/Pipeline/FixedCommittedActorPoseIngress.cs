using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedCommittedActorPoseReadPort : ICommittedActorPoseReadPort<FixedVector3, FixedYaw>
    {
        readonly SimulationWorldStateStore m_StateStore;
        readonly IReadOnlyList<FixedLocalEnemyBinding> m_LocalEnemies;
        readonly CommittedActorPoseSnapshot<FixedVector3, FixedYaw> m_Observation;

        public FixedCommittedActorPoseReadPort(
            SimulationComponentIdentity backend,
            SimulationWorldStateStore stateStore,
            IReadOnlyList<FixedLocalEnemyBinding> localEnemies)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is invalid.", nameof(backend));
            m_StateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            m_LocalEnemies = localEnemies;
            SimulationWorldStateSet initial = stateStore.Current;
            var actors = new CommittedActorPose<FixedVector3, FixedYaw>[
                initial.WorldState.Bodies.Count + localEnemies.Count];
            for (int i = 0; i < initial.WorldState.Bodies.Count; i++)
            {
                WorldBodyState body = initial.WorldState.Bodies[i];
                actors[i] = new CommittedActorPose<FixedVector3, FixedYaw>(body.ActorId, body.Position, body.Yaw);
            }
            for (int i = 0; i < localEnemies.Count; i++)
                actors[initial.WorldState.Bodies.Count + i] = localEnemies[i].Source.CommittedPose;
            m_Observation = new CommittedActorPoseSnapshot<FixedVector3, FixedYaw>(initial.LastCompletedTick, actors);
            Descriptor = FixedPipelineRuntimePortDescriptor.Create(
                FixedPipelineRuntimePortIds.CommittedObservation,
                FixedPipelineRuntimePortIds.CommittedObservationSchema,
                backend.ComponentId,
                CommittedActorPoseSchema.CapabilityHash,
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }

        public CommittedActorPoseSnapshot<FixedVector3, FixedYaw> Read()
        {
            SimulationWorldStateSet state = m_StateStore.Current;
            for (int i = 0; i < state.WorldState.Bodies.Count; i++)
            {
                WorldBodyState body = state.WorldState.Bodies[i];
                m_Observation.UpdateActor(body.ActorId, body.Position, body.Yaw);
            }
            for (int i = 0; i < m_LocalEnemies.Count; i++)
            {
                CommittedActorPose<FixedVector3, FixedYaw> pose = m_LocalEnemies[i].Source.CommittedPose;
                m_Observation.UpdateActor(pose.ActorId, pose.Position, pose.Yaw);
            }
            m_Observation.PublishTick(state.LastCompletedTick);
            return m_Observation;
        }
    }
}
