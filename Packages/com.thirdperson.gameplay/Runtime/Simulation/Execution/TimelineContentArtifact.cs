using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using BTSMTL.Timeline;
using Photon.Deterministic;

namespace ThirdPersonSimulation
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class TimelineContentArtifact
    {
        readonly ReadOnlyCollection<TimelineContentUnit> m_Timelines;

        public TimelineContentArtifact(IEnumerable<TimelineContentUnit> timelines, StableHash contentHash)
        {
            var values = new List<TimelineContentUnit>(timelines);
            values.Sort((left, right) => string.CompareOrdinal(left.Identity, right.Identity));
            for (int i = 1; i < values.Count; i++)
                if (values[i - 1].Identity == values[i].Identity)
                    throw new ArgumentException("Timeline artifact contains duplicate content identity.", nameof(timelines));
            m_Timelines = values.AsReadOnly();
            ContentHash = contentHash;
        }

        public IReadOnlyList<TimelineContentUnit> Timelines => m_Timelines;
        public StableHash ContentHash { get; }

        TimelineContentArtifact(TimelineContentArtifact source, StableHash contentHash)
        {
            m_Timelines = source.m_Timelines;
            ContentHash = contentHash;
        }

        public TimelineContentArtifact WithContentHash(StableHash contentHash) => new TimelineContentArtifact(this, contentHash);

        public TimelineContentUnit Require(CanonicalTextValue identity)
        {
            for (int i = 0; i < m_Timelines.Count; i++)
                if (identity.Equals(m_Timelines[i].Identity))
                    return m_Timelines[i];
            throw new KeyNotFoundException($"Timeline '{identity}' is absent from the published artifact.");
        }
    }

}
