using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly ref struct OrchestrationTimelineCommittedView<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        readonly ReadOnlySpan<OrchestrationTimelinePlaybackState<TTime>> m_Playbacks;
        readonly ReadOnlySpan<long> m_TreeExitFirstCycles;
        readonly ReadOnlySpan<ulong> m_TreeExitBits;
        readonly ReadOnlyMemory<OrchestrationTimelinePreparedContent<TTime>> m_Contents;
        readonly int m_TreeExitWindowsPerPlayback;
        readonly int m_TreeExitWordsPerPlayback;

        public OrchestrationTimelineCommittedView(ReadOnlyOrchestrationStateView committed,
            ReadOnlyMemory<OrchestrationTimelinePreparedContent<TTime>> contents,
            in OrchestrationTimelineStateLayout<OrchestrationTimelinePlaybackState<TTime>> layout,
            in OrchestrationTimelinePreparedCapacity capacity)
        {
            m_Playbacks = committed.Get(layout.Playbacks);
            m_TreeExitFirstCycles = committed.Get(layout.TreeExitFirstCycles);
            m_TreeExitBits = committed.Get(layout.TreeExitBits);
            m_Contents = contents;
            m_TreeExitWindowsPerPlayback = capacity.TreeExitWindowsPerPlayback;
            m_TreeExitWordsPerPlayback = capacity.TreeExitWordsPerPlayback;
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
                if (playback.Start.ContentIdentityId == contentIdentityId)
                {
                    OrchestrationTimelinePreparedContent<TTime> content = Content(in playback.Start);
                    if (OrchestrationTimelineStateQueries<TTime>.IsActiveClip<TClock>(content,
                        in playback, TreeExits(i, content), clipOrdinal))
                        return true;
                }
            }
            return false;
        }

        public int CopyActiveClips<TClock>(int slot, Span<int> destination)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            ref readonly OrchestrationTimelinePlaybackState<TTime> playback = ref m_Playbacks[slot];
            OrchestrationTimelinePreparedContent<TTime> content = Content(in playback.Start);
            TimelineTreeExitView exits = TreeExits(slot, content);
            int count = 0;
            int position = content.Lifetimes.FindNext<TClock>(0, playback.Progress.CursorTime, true);
            while (position >= 0)
            {
                int ordinal = content.Lifetimes.StableOrder.Span[position];
                if (OrchestrationTimelineStateQueries<TTime>.IsActiveClip<TClock>(content,
                    in playback, exits, ordinal))
                    destination[count++] = ordinal;
                position = content.Lifetimes.FindNext<TClock>(position + 1, playback.Progress.CursorTime, true);
            }
            return count;
        }

        public int CopyActiveTrees<TClock>(int slot, Span<TimelineSchedulingBoundary<TTime>> destination)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            ref readonly OrchestrationTimelinePlaybackState<TTime> playback = ref m_Playbacks[slot];
            OrchestrationTimelinePreparedContent<TTime> content = Content(in playback.Start);
            return OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(content,
                in playback, TreeExits(slot, content), destination);
        }

        OrchestrationTimelinePreparedContent<TTime> Content(in OrchestrationTimelineStart<TTime> start) =>
            OrchestrationTimelineStateQueries<TTime>.RequireContent(m_Contents.Span, in start);

        TimelineTreeExitView TreeExits(int slot, OrchestrationTimelinePreparedContent<TTime> content) =>
            new(content.TreeCycleWindows.Span,
                m_TreeExitFirstCycles.Slice(checked(slot * m_TreeExitWindowsPerPlayback), m_TreeExitWindowsPerPlayback),
                m_TreeExitBits.Slice(checked(slot * m_TreeExitWordsPerPlayback), m_TreeExitWordsPerPlayback));
    }
}
