using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BTSMTL.Timeline
{
    public class TimelineClipDefinition
    {
        readonly ReadOnlyCollection<TimelineBindingRequirement> m_Bindings;

        public TimelineClipDefinition(
            string kind,
            string trackKind,
            TimelineClipExecutionPhase executionPhase,
            TimelineContentKind contentKind,
            bool supportsStop,
            bool requiresPositiveDuration,
            TimelineExecutionDomain executionDomain,
            TimelineOutputKind outputKind,
            params TimelineBindingRequirement[] bindings)
            : this(kind, trackKind, executionPhase, contentKind, supportsStop, requiresPositiveDuration,
                executionDomain, outputKind, TimelineExecutionDomains.ToMask(executionDomain), bindings)
        {
        }

        public TimelineClipDefinition(
            string kind,
            string trackKind,
            TimelineClipExecutionPhase executionPhase,
            TimelineContentKind contentKind,
            bool supportsStop,
            bool requiresPositiveDuration,
            TimelineExecutionDomain executionDomain,
            TimelineOutputKind outputKind,
            TimelineExecutionDomainMask allowedExecutionDomains,
            params TimelineBindingRequirement[] bindings)
        {
            Kind = Require(kind, nameof(kind));
            TrackKind = Require(trackKind, nameof(trackKind));
            SupportedExecutionPhases = executionPhase;
            DefaultExecutionPhase = executionPhase == TimelineClipExecutionPhase.DecisionAndCommit
                ? TimelineClipExecutionPhase.Commit
                : executionPhase;
            ContentKind = contentKind;
            SupportsStop = supportsStop;
            RequiresPositiveDuration = requiresPositiveDuration;
            ExecutionPolicy = new TimelineClipExecutionPolicy(executionDomain, outputKind);
            AllowedExecutionDomains = allowedExecutionDomains;
            if (AllowedExecutionDomains == TimelineExecutionDomainMask.None ||
                (AllowedExecutionDomains & TimelineExecutionDomains.ToMask(executionDomain)) == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(allowedExecutionDomains));
            }
            m_Bindings = Array.AsReadOnly(bindings == null
                ? Array.Empty<TimelineBindingRequirement>()
                : (TimelineBindingRequirement[])bindings.Clone());
        }

        public string Kind { get; }
        public string TrackKind { get; }
        public TimelineClipExecutionPhase SupportedExecutionPhases { get; }
        public TimelineClipExecutionPhase DefaultExecutionPhase { get; }
        public TimelineContentKind ContentKind { get; }
        public bool SupportsStop { get; }
        public bool RequiresPositiveDuration { get; }
        public TimelineClipExecutionPolicy ExecutionPolicy { get; }
        public TimelineExecutionDomainMask AllowedExecutionDomains { get; }
        public IReadOnlyList<TimelineBindingRequirement> Bindings => m_Bindings;

        public bool SupportsExecutionPhase(TimelineClipExecutionPhase phase) =>
            (SupportedExecutionPhases & phase) == phase;

        public bool SupportsExecutionDomain(TimelineExecutionDomain domain) =>
            TimelineExecutionDomains.IsValid(domain) &&
            (AllowedExecutionDomains & TimelineExecutionDomains.ToMask(domain)) != 0;

        static string Require(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Timeline Clip contract identity is required.", name)
                : value.Trim();
        }
    }
}
