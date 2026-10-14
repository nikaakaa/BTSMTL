using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPCommittedActorPoseReadPort : ICommittedActorPoseReadPort<FPVector3, FP>
    {
        readonly SimulationWorldStateStore m_StateStore;
        readonly IReadOnlyList<FPLocalEnemyBinding> m_LocalEnemies;
        readonly CommittedActorPoseSnapshot<FPVector3, FP> m_Observation;

        public FPCommittedActorPoseReadPort(
            SimulationComponentIdentity backend,
            SimulationWorldStateStore stateStore,
            IReadOnlyList<FPLocalEnemyBinding> localEnemies)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is invalid.", nameof(backend));
            m_StateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            m_LocalEnemies = localEnemies;
            SimulationWorldStateSet initial = stateStore.Current;
            var actors = new CommittedActorPose<FPVector3, FP>[
                initial.WorldState.Bodies.Count + localEnemies.Count];
            for (int i = 0; i < initial.WorldState.Bodies.Count; i++)
            {
                WorldBodyState body = initial.WorldState.Bodies[i];
                actors[i] = new CommittedActorPose<FPVector3, FP>(body.ActorId, body.Position, body.Yaw);
            }
            for (int i = 0; i < localEnemies.Count; i++)
                actors[initial.WorldState.Bodies.Count + i] = localEnemies[i].Source.CommittedPose;
            m_Observation = new CommittedActorPoseSnapshot<FPVector3, FP>(initial.LastCompletedTick, actors);
            Descriptor = FPPipelineRuntimePortDescriptor.Create(
                FPPipelineRuntimePortIds.CommittedObservation,
                FPPipelineRuntimePortIds.CommittedObservationSchema,
                backend.ComponentId,
                CommittedActorPoseSchema.CapabilityHash,
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }

        public CommittedActorPoseSnapshot<FPVector3, FP> Read()
        {
            SimulationWorldStateSet state = m_StateStore.Current;
            for (int i = 0; i < state.WorldState.Bodies.Count; i++)
            {
                WorldBodyState body = state.WorldState.Bodies[i];
                m_Observation.UpdateActor(body.ActorId, body.Position, body.Yaw);
            }
            for (int i = 0; i < m_LocalEnemies.Count; i++)
            {
                CommittedActorPose<FPVector3, FP> pose = m_LocalEnemies[i].Source.CommittedPose;
                m_Observation.UpdateActor(pose.ActorId, pose.Position, pose.Yaw);
            }
            m_Observation.PublishTick(state.LastCompletedTick);
            return m_Observation;
        }
    }
}
