using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BTSMTL.Timeline
{
    public class TimelineDefinitionCatalog<TClip> where TClip : TimelineClipDefinition
    {
        readonly ReadOnlyCollection<TimelineTrackContract> m_Tracks;
        readonly ReadOnlyCollection<TClip> m_Clips;
        readonly Dictionary<string, TimelineTrackContract> m_TracksByKind;
        readonly Dictionary<string, TClip> m_ClipsByKind;

        public TimelineDefinitionCatalog(IEnumerable<TimelineTrackContract> tracks, IEnumerable<TClip> clips)
        {
            m_Tracks = new ReadOnlyCollection<TimelineTrackContract>(new List<TimelineTrackContract>(tracks));
            m_Clips = new ReadOnlyCollection<TClip>(new List<TClip>(clips));
            m_TracksByKind = BuildTrackIndex(m_Tracks);
            m_ClipsByKind = BuildClipIndex(m_Clips);
            foreach (TClip clip in m_Clips)
                if (!m_TracksByKind.ContainsKey(clip.TrackKind))
                    throw new InvalidOperationException($"Timeline Clip contract '{clip.Kind}' references missing Track contract '{clip.TrackKind}'.");
        }

        public IReadOnlyList<TimelineTrackContract> Tracks => m_Tracks;
        public IReadOnlyList<TClip> Clips => m_Clips;

        public bool TryGetTrack(string kind, out TimelineTrackContract contract) =>
            m_TracksByKind.TryGetValue(kind ?? string.Empty, out contract);

        public bool TryGetClip(string kind, out TClip contract) =>
            m_ClipsByKind.TryGetValue(kind ?? string.Empty, out contract);

        public TimelineTrackContract RequireTrack(string kind) => TryGetTrack(kind, out TimelineTrackContract contract)
            ? contract : throw new InvalidOperationException($"Unknown Timeline Track contract kind '{kind}'.");

        public TClip RequireClip(string kind) => TryGetClip(kind, out TClip contract)
            ? contract : throw new InvalidOperationException($"Unknown Timeline Clip contract kind '{kind}'.");

        public void RequireClipPlacement(string trackKind, string clipKind, TimelineExecutionDomain executionDomain,
            string trackIdentity, string clipIdentity)
        {
            TimelineTrackContract track = RequireTrack(trackKind);
            TClip clip = RequireClip(clipKind);
            if (!track.AllowsClip(clip.Kind) || !string.Equals(track.Kind, clip.TrackKind, StringComparison.Ordinal))
                throw new InvalidOperationException($"Timeline Clip '{clipKind}' cannot be added to Track '{trackKind}'.");
            if (!track.SupportsExecutionDomain(executionDomain) || !clip.SupportsExecutionDomain(executionDomain))
                throw new InvalidOperationException(
                    $"Timeline Clip '{clipIdentity}' cannot use execution domain '{executionDomain}' on Track '{trackIdentity}'.");
        }

        static Dictionary<string, TimelineTrackContract> BuildTrackIndex(IReadOnlyList<TimelineTrackContract> contracts)
        {
            var result = new Dictionary<string, TimelineTrackContract>(StringComparer.Ordinal);
            for (int i = 0; i < contracts.Count; i++)
            {
                string kind = contracts[i].Kind;
                if (!result.TryAdd(kind, contracts[i]))
                    throw new InvalidOperationException($"Timeline Track contract '{kind}' is registered more than once.");
            }
            return result;
        }

        static Dictionary<string, TClip> BuildClipIndex(IReadOnlyList<TClip> contracts)
        {
            var result = new Dictionary<string, TClip>(StringComparer.Ordinal);
            for (int i = 0; i < contracts.Count; i++)
            {
                string kind = contracts[i].Kind;
                if (!result.TryAdd(kind, contracts[i]))
                    throw new InvalidOperationException($"Timeline Clip contract '{kind}' is registered more than once.");
            }
            return result;
        }
    }
}
