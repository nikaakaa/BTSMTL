using System;
using System.Collections.Generic;
using BTSMTL.Diagnostics;
using BTSMTL.Diagnostics.Editor;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BTSMTL.Timeline.Tree.Editor")]

namespace BTSMTL.Timeline.Editor
{
    internal struct RuntimeDynamicTreeClipObservation
    {
        internal RuntimeDebugEventView Latest;
        RuntimeDebugEventView m_Exit;
        ulong m_EnterSequence;

        internal void Observe(RuntimeDebugEventView item)
        {
            if (item.Event.Sequence > Latest.Event.Sequence)
                Latest = item;
            if (item.Event.Kind == RuntimeTraceEventKind.TreeClipEntered)
                m_EnterSequence = Math.Max(m_EnterSequence, item.Event.Sequence);
            else if (item.Event.Kind == RuntimeTraceEventKind.TreeClipExited &&
                     item.Event.Sequence > m_Exit.Event.Sequence)
                m_Exit = item;
        }

        internal float EndTime(float currentTime)
        {
            if (Latest.Event.Kind is RuntimeTraceEventKind.TreeClipExited or RuntimeTraceEventKind.TreeClipDestroyed)
                return m_Exit.Event.Sequence > m_EnterSequence ? m_Exit.Event.Payload.Time : Latest.Event.Payload.Time;
            return currentTime;
        }
    }

    internal sealed class RuntimeTimelineObservationBuffer
    {
        internal List<RuntimeDebugEventView> EventBuffer { get; } = new List<RuntimeDebugEventView>();
        internal Dictionary<string, string> ActiveTracks { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        internal Dictionary<string, float> DynamicClipEnds { get; } = new Dictionary<string, float>(StringComparer.Ordinal);
        internal Dictionary<string, string> ActiveClips { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        internal Dictionary<string, RuntimeDebugEventView> LatestClips { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, RuntimeDynamicTreeClipObservation> DynamicClips { get; } = new(StringComparer.Ordinal);
    }
}
