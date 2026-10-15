using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly ref struct OrchestrationTimelineCommittedView<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        readonly ReadOnlySpan<OrchestrationTimelinePlaybackState<TTime>> m_Playbacks;
        readonly ReadOnlySpan<OrchestrationTimelineTreeExitRange> m_TreeExits;
        readonly ReadOnlyMemory<OrchestrationTimelinePreparedContent<TTime>> m_Contents;
        readonly int m_TreeExitsPerPlayback;

        public OrchestrationTimelineCommittedView(ReadOnlyOrchestrationStateView committed,
            ReadOnlyMemory<OrchestrationTimelinePreparedContent<TTime>> contents,
            in OrchestrationTimelineStateLayout<OrchestrationTimelinePlaybackState<TTime>> layout,
            in OrchestrationTimelinePreparedCapacity capacity)
        {
            m_Playbacks = committed.Get(layout.Playbacks);
            m_TreeExits = committed.Get(layout.TreeExits);
            m_Contents = contents;
            m_TreeExitsPerPlayback = capacity.TreeExitsPerPlayback;
        }

        public ReadOnlySpan<OrchestrationTimelinePlaybackState<TTime>> Playbacks => m_Playbacks;

        public ref readonly OrchestrationTimelinePlaybackState<TTime> Playback(int slot) =>
            ref m_Playbacks[slot];

        public bool TryFindPlayback(ulong playbackIdentity, out int slot)
        {
            for (int i = 0; i < m_Playbacks.Length; i++)
                if (m_Playbacks[i].Start.PlaybackIdentity == playbackIdentity)
                {
                    slot = i;
                    return true;
                }
            slot = -1;
            return false;
        }

        public bool IsActive(int contentIdentityId)
        {
            for (int i = 0; i < m_Playbacks.Length; i++)
            {
                ref readonly OrchestrationTimelinePlaybackState<TTime> playback = ref m_Playbacks[i];
                if (playback.Start.ContentIdentityId == contentIdentityId &&
                    OrchestrationTimelineStateQueries<TTime>.IsActive(in playback))
                    return true;
            }
            return false;
        }

        public bool IsActive<TClock>(int contentIdentityId, int clipOrdinal)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            for (int i = 0; i < m_Playbacks.Length; i++)
            {
                ref readonly OrchestrationTimelinePlaybackState<TTime> playback = ref m_Playbacks[i];
                if (playback.Start.ContentIdentityId == contentIdentityId &&
                    OrchestrationTimelineStateQueries<TTime>.IsActiveClip<TClock>(Content(in playback.Start),
                        in playback, TreeExits(i), clipOrdinal))
                    return true;
            }
            return false;
        }

        public int CopyActiveClips<TClock>(int slot, Span<int> destination)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            ref readonly OrchestrationTimelinePlaybackState<TTime> playback = ref m_Playbacks[slot];
            OrchestrationTimelinePreparedContent<TTime> content = Content(in playback.Start);
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exits = TreeExits(slot);
            int count = 0;
            for (int ordinal = 0; ordinal < content.Clips.Length; ordinal++)
                if (OrchestrationTimelineStateQueries<TTime>.IsActiveClip<TClock>(content,
                    in playback, exits, ordinal))
                    destination[count++] = ordinal;
            return count;
        }

        public int CopyActiveTrees<TClock>(int slot, Span<TimelineSchedulingBoundary<TTime>> destination)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            ref readonly OrchestrationTimelinePlaybackState<TTime> playback = ref m_Playbacks[slot];
            return OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(Content(in playback.Start),
                in playback, TreeExits(slot), destination);
        }

        public int SectionOrdinal<TClock>(int slot)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            ref readonly OrchestrationTimelinePlaybackState<TTime> playback = ref m_Playbacks[slot];
            ReadOnlySpan<TimelineSchedulingSection<TTime>> sections = Content(in playback.Start).Sections.Span;
            int lower = 0;
            int upper = sections.Length;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                if (default(TClock).LessThanOrEqual(sections[middle].Time, playback.Progress.CursorTime))
                    lower = middle + 1;
                else
                    upper = middle;
            }
            return lower == 0 ? -1 : sections[lower - 1].SectionIndex;
        }

        OrchestrationTimelinePreparedContent<TTime> Content(in OrchestrationTimelineStart<TTime> start) =>
            OrchestrationTimelineStateQueries<TTime>.RequireContent(m_Contents.Span, in start);

        ReadOnlySpan<OrchestrationTimelineTreeExitRange> TreeExits(int slot) =>
            m_TreeExits.Slice(checked(slot * m_TreeExitsPerPlayback), m_Playbacks[slot].TreeExitCount);
    }
}
