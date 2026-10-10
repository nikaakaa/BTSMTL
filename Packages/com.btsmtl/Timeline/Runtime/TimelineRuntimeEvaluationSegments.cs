using System;
using ThirdPersonSimulation.Fixed;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    readonly struct TimelineRuntimeEvaluationSegments
    {
        public const int MaximumCycleAdvance = AbilityTimelineRuntimeLimits.MaximumCycleAdvance;
        readonly FixedScalar m_Previous;
        readonly FixedScalar m_Current;
        readonly FixedScalar m_Duration;
        readonly int m_PreviousCycle;
        readonly int m_CurrentCycle;

        public TimelineRuntimeEvaluationSegments(FixedScalar previous, int previousCycle,
            FixedScalar current, int currentCycle, FixedScalar duration, bool loop)
        {
            if (currentCycle < previousCycle || currentCycle - previousCycle > MaximumCycleAdvance ||
                currentCycle == previousCycle && current < previous)
                throw new InvalidOperationException("Timeline interval has an invalid traversal range.");
            m_Previous = previous;
            m_Current = current;
            m_Duration = duration;
            m_PreviousCycle = previousCycle;
            m_CurrentCycle = currentCycle;
            Count = loop && duration > FixedScalar.Zero ? currentCycle - previousCycle + 1 : 1;
        }

        public int Count { get; }
        public TimelineRuntimeEvaluationSegment this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return new TimelineRuntimeEvaluationSegment(
                    index == 0 ? m_Previous : FixedScalar.Zero,
                    index == Count - 1 ? m_Current : m_Duration,
                    Count == 1 ? m_CurrentCycle : m_PreviousCycle + index);
            }
        }
    }

    readonly struct TimelineRuntimeEvaluationSegment
    {
        public TimelineRuntimeEvaluationSegment(FixedScalar previousTime, FixedScalar currentTime, int cycle)
        {
            PreviousTime = previousTime;
            CurrentTime = currentTime;
            Cycle = cycle;
        }

        public FixedScalar PreviousTime { get; }
        public FixedScalar CurrentTime { get; }
        public int Cycle { get; }
    }
}
