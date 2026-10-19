using System;
using System.Collections.Generic;

namespace BTSMTL.Compilation.Analysis
{
    public enum TimelineDoorDestinationKind : byte
    {
        NextSection = 1,
        Transition = 2
    }

    public sealed class TimelineDoorDestination
    {
        internal TimelineDoorDestination(OrchestrationStaticClip door, OrchestrationStaticSection section,
            OrchestrationStaticSection nextSection, OrchestrationStaticTransition transition, string stateKey)
        {
            Door = door;
            Section = section;
            NextSection = nextSection;
            Transition = transition;
            if (stateKey.Length != 0)
                m_StateKeys.Add(stateKey);
        }

        public TimelineDoorDestinationKind Kind => Transition != null
            ? TimelineDoorDestinationKind.Transition : TimelineDoorDestinationKind.NextSection;
        public OrchestrationStaticClip Door { get; }
        public OrchestrationStaticSection Section { get; }
        public OrchestrationStaticSection NextSection { get; }
        public OrchestrationStaticTransition Transition { get; }
        // 经门转移时，转移读取、门提供的状态键。
        public string StateKey => m_StateKeys.Count != 0 ? m_StateKeys[0] : string.Empty;
        public IReadOnlyList<string> StateKeys => m_StateKeys;
        internal readonly List<string> m_StateKeys = new();

        public string Label => Transition != null
            ? $"→{Transition.Target?.Name ?? Transition.Identity}"
            : $"→{NextSection?.AuthoringId}";
    }

    // 单个状态的门与去向：由静态查询即时推导，不保存副本；内容变化后重新构建。
    public sealed class TimelineStateDoors
    {
        readonly List<TimelineDoorDestination> m_Destinations = new();
        readonly List<OrchestrationStaticTransition> m_Anytime = new();
        readonly List<(OrchestrationStaticTransition Transition, OrchestrationStaticRead Read)> m_UnresolvedReads = new();

        TimelineStateDoors(OrchestrationStaticState state)
        {
            State = state;
        }

        public OrchestrationStaticState State { get; }
        public OrchestrationStaticTimeline Timeline => State.Timeline;
        public IReadOnlyList<TimelineDoorDestination> Destinations => m_Destinations;
        // 条件不读取任何 Clip 提供状态的转移：从状态节点边缘出发，随时可走。
        public IReadOnlyList<OrchestrationStaticTransition> Anytime => m_Anytime;
        // 读取了状态键但本 Timeline 没有 Clip 在入口无条件提供它：计入其它运行数据。
        public IReadOnlyList<(OrchestrationStaticTransition Transition, OrchestrationStaticRead Read)> UnresolvedStateReads => m_UnresolvedReads;

        public IEnumerable<TimelineDoorDestination> DestinationsOf(OrchestrationStaticClip door)
        {
            foreach (TimelineDoorDestination destination in m_Destinations)
                if (destination.Door == door)
                    yield return destination;
        }

        public IEnumerable<OrchestrationStaticClip> Doors
        {
            get
            {
                var seen = new HashSet<OrchestrationStaticClip>();
                foreach (TimelineDoorDestination destination in m_Destinations)
                    if (seen.Add(destination.Door))
                        yield return destination.Door;
            }
        }

        public static TimelineStateDoors Build(OrchestrationStaticState state)
        {
            var doors = new TimelineStateDoors(state);
            OrchestrationStaticTimeline timeline = state.Timeline;
            if (timeline != null)
                foreach (OrchestrationStaticClip clip in timeline.Clips)
                {
                    if (!clip.RequestsSectionContinue)
                        continue;
                    OrchestrationStaticSection section = timeline.SectionAt(clip.Start);
                    OrchestrationStaticSection next = section != null && section.Continuable
                        ? timeline.Section(section.NextSectionId) : null;
                    if (next != null)
                        doors.m_Destinations.Add(new TimelineDoorDestination(clip, section, next, null, string.Empty));
                }
            foreach (OrchestrationStaticTransition transition in state.Transitions)
            {
                bool gated = false;
                foreach (OrchestrationStaticRead read in transition.Reads)
                {
                    if (read.Source != OrchestrationStaticReadSource.ClipState)
                        continue;
                    bool provided = false;
                    if (timeline != null)
                        foreach (OrchestrationStaticClip clip in timeline.Clips)
                            if (Contains(clip.ProvidedStateKeys, read.Key))
                            {
                                provided = true;
                                TimelineDoorDestination existing = null;
                                foreach (TimelineDoorDestination destination in doors.m_Destinations)
                                    if (destination.Door == clip && destination.Transition == transition)
                                        existing = destination;
                                if (existing == null)
                                    doors.m_Destinations.Add(new TimelineDoorDestination(clip,
                                        timeline.SectionAt(clip.Start), null, transition, read.Key));
                                else if (!Contains(existing.StateKeys, read.Key))
                                    existing.m_StateKeys.Add(read.Key);
                            }
                    gated |= provided;
                    if (!provided)
                        doors.m_UnresolvedReads.Add((transition, TimelineDoorReads.EffectiveRead(timeline, in read)));
                }
                if (!gated)
                    doors.m_Anytime.Add(transition);
            }
            return doors;
        }

        static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }

    // 读取分类给推导与界面：Clip 提供的状态未被任何 Clip 提供时，按其它运行数据处理。
    public static class TimelineDoorReads
    {
        public static OrchestrationStaticReadSource EffectiveSource(OrchestrationStaticTimeline timeline,
            in OrchestrationStaticRead read)
        {
            if (read.Source != OrchestrationStaticReadSource.ClipState)
                return read.Source;
            if (timeline != null)
                foreach (OrchestrationStaticClip clip in timeline.Clips)
                    foreach (string key in clip.ProvidedStateKeys)
                        if (string.Equals(key, read.Key, StringComparison.Ordinal))
                            return OrchestrationStaticReadSource.ClipState;
            return OrchestrationStaticReadSource.RuntimeData;
        }

        public static OrchestrationStaticRead EffectiveRead(OrchestrationStaticTimeline timeline,
            in OrchestrationStaticRead read)
        {
            OrchestrationStaticReadSource source = EffectiveSource(timeline, in read);
            return source == read.Source ? read : new OrchestrationStaticRead(read.OperationIndex,
                source, read.Key, true, read.Label);
        }
    }
}
