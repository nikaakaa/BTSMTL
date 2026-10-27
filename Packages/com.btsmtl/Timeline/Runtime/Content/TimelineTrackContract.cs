using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BTSMTL.Timeline
{
    public sealed class TimelineTrackContract
    {
        readonly ReadOnlyCollection<string> m_AllowedClipKinds;

        public TimelineTrackContract(
            string kind,
            TimelineTrackOverlapPolicy overlapPolicy,
            TimelineContentKind contentKind,
            TimelineExecutionDomain executionDomain,
            TimelineOutputKind outputKind,
            TimelineLogicSampleExtension? logicSampleExtension,
            params string[] allowedClipKinds)
            : this(
                kind,
                overlapPolicy,
                contentKind,
                executionDomain,
                outputKind,
                TimelineExecutionDomains.ToMask(executionDomain),
                logicSampleExtension,
                allowedClipKinds)
        {
        }

        public TimelineTrackContract(
            string kind,
            TimelineTrackOverlapPolicy overlapPolicy,
            TimelineContentKind contentKind,
            TimelineExecutionDomain executionDomain,
            TimelineOutputKind outputKind,
            TimelineExecutionDomainMask allowedExecutionDomains,
            TimelineLogicSampleExtension? logicSampleExtension,
            params string[] allowedClipKinds)
        {
            Kind = Require(kind, nameof(kind));
            if (allowedClipKinds == null || allowedClipKinds.Length == 0)
                throw new ArgumentException("Timeline Track contract requires clip kinds.", nameof(allowedClipKinds));
            m_AllowedClipKinds = Array.AsReadOnly((string[])allowedClipKinds.Clone());
            OverlapPolicy = overlapPolicy;
            ContentKind = contentKind;
            ExecutionPolicy = new TimelineClipExecutionPolicy(executionDomain, outputKind);
            AllowedExecutionDomains = allowedExecutionDomains;
            LogicSampleExtension = logicSampleExtension;
            if (AllowedExecutionDomains == TimelineExecutionDomainMask.None ||
                (AllowedExecutionDomains & TimelineExecutionDomains.ToMask(executionDomain)) == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(allowedExecutionDomains));
            }
            bool requiresLogicSampleExtension = contentKind == TimelineContentKind.Sampled &&
                (allowedExecutionDomains & TimelineExecutionDomainMask.Logic) != 0;
            if (requiresLogicSampleExtension != logicSampleExtension.HasValue)
                throw new ArgumentException(requiresLogicSampleExtension
                    ? $"Sampled Timeline Track '{Kind}' requires a Logic sample extension."
                    : $"Timeline Track '{Kind}' does not declare sampled Logic execution.",
                    nameof(logicSampleExtension));
        }

        public string Kind { get; }
        public TimelineTrackOverlapPolicy OverlapPolicy { get; }
        public TimelineContentKind ContentKind { get; }
        public TimelineClipExecutionPolicy ExecutionPolicy { get; }
        public TimelineExecutionDomainMask AllowedExecutionDomains { get; }
        public TimelineLogicSampleExtension? LogicSampleExtension { get; }
        public IReadOnlyList<string> AllowedClipKinds => m_AllowedClipKinds;

        public bool AllowsClip(string clipKind)
        {
            for (int i = 0; i < m_AllowedClipKinds.Count; i++)
            {
                if (string.Equals(m_AllowedClipKinds[i], clipKind, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public bool SupportsExecutionDomain(TimelineExecutionDomain domain) =>
            TimelineExecutionDomains.IsValid(domain) &&
            (AllowedExecutionDomains & TimelineExecutionDomains.ToMask(domain)) != 0;

        static string Require(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Timeline Track contract identity is required.", name)
                : value.Trim();
        }
    }
}
