using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    [ValueSnapshot(CopyMethod = nameof(CopyFrom))]
    public sealed partial class FixedAbilityFrameEvents : IFixedAbilityFrameEventCapturePort, IDisposable
    {
        readonly IReadOnlyList<ActorId> m_Actors;
        [ValueSnapshot] readonly AbilityInputEvent[][] m_Events;
        [ValueSnapshot] readonly int[] m_Counts;
        readonly int[] m_Capacities;

        public FixedAbilityFrameEvents(IReadOnlyList<ActorId> actors, IReadOnlyList<int> capacities)
        {
            m_Actors = actors;
            m_Capacities = new int[actors.Count];
            m_Events = new AbilityInputEvent[actors.Count][];
            m_Counts = new int[actors.Count];
            for (int actor = 0; actor < actors.Count; actor++)
            {
                m_Capacities[actor] = capacities[actor];
                m_Events[actor] = new AbilityInputEvent[capacities[actor]];
            }
        }

        public FixedAbilityFrameEvents(FixedAbilityFrameEvents source) : this(source.m_Actors, source.m_Capacities)
        {
            CopyFrom(source);
        }

        int ActorIndex(ActorId actorId)
        {
            for (int actor = 0; actor < m_Actors.Count; actor++)
                if (m_Actors[actor] == actorId) return actor;
            throw new InvalidOperationException("技能事件观察的 Actor 不属于该帧 roster。");
        }

        public void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress)
        {
            int actor = ActorIndex(actorId);
            Clear(actor);
            for (int index = 0; index < ingress.Length; index++)
                if (FixedAbilityFrameEventSemantics.TryProject(actorId, in ingress[index], out AbilityInputEvent value))
                {
                    value.Retain();
                    m_Events[actor][m_Counts[actor]++] = value;
                }
        }

        public bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId)
        {
            int actor = ActorIndex(actorId);
            return FixedAbilityFrameEventSemantics.HasActionEvent(
                new ReadOnlySpan<AbilityInputEvent>(m_Events[actor], 0, m_Counts[actor]),
                actorId, instanceId, eventId, targetId);
        }

        public bool HasGameplayEvent(ActorId actorId, string eventId)
        {
            int actor = ActorIndex(actorId);
            return FixedAbilityFrameEventSemantics.HasGameplayEvent(
                new ReadOnlySpan<AbilityInputEvent>(m_Events[actor], 0, m_Counts[actor]), actorId, eventId);
        }

        public partial void CopyFrom(FixedAbilityFrameEvents source);

        void Clear(int actor)
        {
            for (int index = 0; index < m_Counts[actor]; index++)
                m_Events[actor][index].Release();
            Array.Clear(m_Events[actor], 0, m_Counts[actor]);
            m_Counts[actor] = 0;
        }

        public void Dispose()
        {
            for (int actor = 0; actor < m_Actors.Count; actor++) Clear(actor);
        }
    }

    static class FixedAbilityFrameEventSemantics
    {
        internal static bool TryProject(ActorId actorId, in SimulationIngress ingress, out AbilityInputEvent value)
        {
            if (ingress.Header.Kind == SimulationIngressKind.ActionEvent)
            {
                value = new AbilityInputEvent(1, actorId, default, ingress.ActionEvent.ActionInstanceId,
                    0, ingress.ActionEvent.EventId, default, default, default, 0);
                return true;
            }
            if (ingress.Header.Kind == SimulationIngressKind.GameplayResult && !ingress.GameplayResult.HasEffect)
            {
                CombatHitResult hit = ingress.GameplayResult.Hit;
                value = new AbilityInputEvent(2, hit.SourceActor, hit.TargetActor, hit.Attack.ActionInstanceId,
                    hit.TargetActionInstanceId, default, hit.AttackEventId, hit.SourceFeedbackEvent,
                    hit.TargetFeedbackEvent, (byte)hit.Disposition);
                return true;
            }
            value = default;
            return false;
        }

        internal static bool HasActionEvent(ReadOnlySpan<AbilityInputEvent> events, ActorId actorId,
            ulong instanceId, string eventId, CanonicalTextValue targetId)
        {
            for (int index = 0; index < events.Length; index++)
            {
                ref readonly AbilityInputEvent value = ref events[index];
                if (value.Kind == 1 && value.SourceInstance == instanceId && value.EventId.Equals(eventId)) return true;
                if (value.Kind == 2 &&
                    (value.SourceActor == actorId && value.SourceInstance == instanceId && value.Disposition == (byte)CombatHitDisposition.Hit &&
                        (value.SourceFeedback.Equals(eventId) || value.AttackEvent.Equals(eventId)) ||
                    value.TargetActor == actorId && value.TargetInstance == instanceId &&
                        (value.Disposition != (byte)CombatHitDisposition.Parried || value.SourceActor.Value.Equals(targetId)) &&
                        value.TargetFeedback.Equals(eventId))) return true;
            }
            return false;
        }

        internal static bool HasGameplayEvent(ReadOnlySpan<AbilityInputEvent> events, ActorId actorId, string eventId)
        {
            for (int index = 0; index < events.Length; index++)
            {
                ref readonly AbilityInputEvent value = ref events[index];
                if (value.Kind == 1 && value.EventId.Equals(eventId) ||
                    value.Kind == 2 && value.TargetActor == actorId && value.TargetFeedback.Equals(eventId)) return true;
            }
            return false;
        }
    }
}
