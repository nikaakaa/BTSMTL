using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public enum TimelineRuntimeNumericTarget : byte
    {
        Float32 = 0,
        FP = 1
    }

    public readonly struct TimelineRuntimeDependencyHandle : IEquatable<TimelineRuntimeDependencyHandle>
    {
        public TimelineRuntimeDependencyHandle(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public int Value { get; }
        public bool IsValid => Value > 0;
        public bool Equals(TimelineRuntimeDependencyHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TimelineRuntimeDependencyHandle other && Equals(other);
        public override int GetHashCode() => Value;
        public static TimelineRuntimeDependencyHandle Invalid => default;
    }

    public interface ITimelineRuntimeDependencyResolver
    {
        bool TryResolve(
            TimelineContentDependency dependency,
            TimelineRuntimeNumericTarget numericTarget,
            out TimelineRuntimeDependencyHandle handle,
            out string error);
    }

    public sealed class TimelineRuntimePreparedDependencies
    {
        readonly Dictionary<string, TimelineRuntimeDependencyHandle> m_Handles;

        internal TimelineRuntimePreparedDependencies(
            IReadOnlyList<TimelineContentDependency> dependencies,
            IReadOnlyList<TimelineRuntimeDependencyHandle> handles)
        {
            m_Handles = new Dictionary<string, TimelineRuntimeDependencyHandle>(StringComparer.Ordinal);
            for (int index = 0; index < dependencies.Count; index++)
                m_Handles.Add(dependencies[index].Identity, handles[index]);
        }

        public bool TryGetHandle(string dependencyIdentity, out TimelineRuntimeDependencyHandle handle)
        {
            if (m_Handles.TryGetValue(dependencyIdentity ?? string.Empty, out handle))
                return handle.IsValid;
            handle = TimelineRuntimeDependencyHandle.Invalid;
            return false;
        }
    }
}
