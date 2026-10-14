using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public static class CommittedActorPoseSchema
    {
        public const string Id = "committed-actor-pose-observation";
        public const int Version = 2;
        public static readonly StableHash CapabilityHash = StableHash.Compute(Id, Version.ToString());
    }

    public readonly struct CommittedActorPose<TPosition, TYaw>
    {
        public CommittedActorPose(ActorId actorId, TPosition position, TYaw yaw)
        {
            ActorId = actorId;
            Position = position;
            Yaw = yaw;
        }

        public ActorId ActorId { get; }
        public TPosition Position { get; }
        public TYaw Yaw { get; }
    }

    public class CommittedActorPoseSnapshot<TPosition, TYaw>
    {
        readonly ReadOnlyCollection<CommittedActorPose<TPosition, TYaw>> m_Actors;
        readonly CommittedActorPose<TPosition, TYaw>[] m_Values;
        readonly Dictionary<ActorId, int> m_Indices;

        public CommittedActorPoseSnapshot(
            ulong observationTick,
            IEnumerable<CommittedActorPose<TPosition, TYaw>> actors)
        {
            var values = actors == null
                ? new List<CommittedActorPose<TPosition, TYaw>>()
                : new List<CommittedActorPose<TPosition, TYaw>>(actors);
            values.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
            if (values.Count == 0)
                throw new ArgumentException("Committed observation roster cannot be empty.", nameof(actors));
            m_Indices = new Dictionary<ActorId, int>(values.Count);
            var identity = new string[values.Count + 2];
            identity[0] = CommittedActorPoseSchema.Id;
            identity[1] = CommittedActorPoseSchema.Version.ToString();
            for (int i = 0; i < values.Count; i++)
            {
                if (!values[i].ActorId.IsValid || i > 0 && values[i - 1].ActorId == values[i].ActorId)
                    throw new ArgumentException("Committed observation roster contains an invalid or duplicate ActorId.", nameof(actors));
                m_Indices.Add(values[i].ActorId, i);
                identity[i + 2] = values[i].ActorId.ToString();
            }
            ObservationTick = observationTick;
            m_Values = values.ToArray();
            m_Actors = Array.AsReadOnly(m_Values);
            RosterHash = StableHash.Compute(identity);
        }

        public ulong ObservationTick { get; private set; }
        public StableHash RosterHash { get; }
        public IReadOnlyList<CommittedActorPose<TPosition, TYaw>> Actors => m_Actors;

        public void UpdateActor(ActorId actorId, TPosition position, TYaw yaw) =>
            m_Values[m_Indices[actorId]] = new CommittedActorPose<TPosition, TYaw>(actorId, position, yaw);

        public void PublishTick(ulong tick) => ObservationTick = tick;

        public bool TryGetActor(ActorId actorId, out CommittedActorPose<TPosition, TYaw> observation)
        {
            if (m_Indices.TryGetValue(actorId, out int index))
            {
                observation = m_Actors[index];
                return true;
            }
            observation = default;
            return false;
        }

        public CommittedActorPose<TPosition, TYaw> GetRequiredActor(ActorId actorId)
        {
            if (!TryGetActor(actorId, out CommittedActorPose<TPosition, TYaw> observation))
                throw new InvalidOperationException($"Committed observation Tick '{ObservationTick}' has no Actor '{actorId}'.");
            return observation;
        }
    }

    public interface ICommittedActorPoseReadPort<TPosition, TYaw> : ISimulationRuntimePort
    {
        CommittedActorPoseSnapshot<TPosition, TYaw> Read();
    }
}
