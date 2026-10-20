using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BTSMTL.Diagnostics;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public static class TimelineContentDiscovery
    {
        public static OrchestrationIdentityTable PublishIdentities(TimelineContentUnit content)
        {
            var identities = new List<string> { content.Identity, content.ContentHash };
            for (int ordinal = 0; ordinal < content.Clips.Count; ordinal++)
                identities.Add(content.Clips[ordinal].AuthoringId);
            for (int ordinal = 0; ordinal < content.Sections.Count; ordinal++)
                identities.Add(content.Sections[ordinal].AuthoringId);
            return OrchestrationIdentityPublication.Create(identities);
        }

        public static TimelineContentCurve CaptureSourceCurve(string channelId, AnimationCurve curve)
        {
            var keys = new TimelineContentCurveKey[curve.length];
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = curve[i];
                keys[i] = new TimelineContentCurveKey(key.time, key.value, key.inTangent, key.outTangent,
                    key.inWeight, key.outWeight, (int)key.weightedMode);
            }
            return new TimelineContentCurve(channelId, channelId, TimelineCurveTimeDomain.SourceSeconds,
                TimelineCurveValueDomain.Unbounded(0f, string.Empty),
                (int)curve.preWrapMode, (int)curve.postWrapMode, keys);
        }
#if UNITY_EDITOR
        public static TimelineContentDiscoveryResult Discover(TimelineAsset asset, TimelineContractCatalog catalog)
        {
            return Discover(asset?.Data, catalog);
        }

        public static TimelineContentDiscoveryResult Discover(TimelineData timeline, TimelineContractCatalog catalog)
            => Discover(timeline, catalog, new TimelineAuthoringGraphBindings());
#endif

        public static TimelineContentDiscoveryResult Discover(TimelineData timeline, TimelineContractCatalog catalog,
            ITimelineGraphBindingSource graphBindings)
        {
            var errors = new List<string>();
            if (timeline == null)
            {
                errors.Add("Timeline content root is missing.");
                return new TimelineContentDiscoveryResult(null, errors, null);
            }

            if (!timeline.ValidateContent(catalog, errors))
                return new TimelineContentDiscoveryResult(null, errors, null);

            var clips = new List<TimelineContentClip>();
            var markers = new List<TimelineContentMarker>();
            var tracks = new List<TimelineContentTrack>();
            var sections = new List<TimelineContentSection>();
            var closure = new TimelineContentClosureBuilder();
            OrchestrationTime duration = OrchestrationTime.Zero;
            for (int trackIndex = 0; trackIndex < timeline.Tracks.Count; trackIndex++)
            {
                Track track = timeline.Tracks[trackIndex];
                if (track == null)
                    continue;
                TimelineTrackContract trackContract = catalog.RequireTrack(track.ContractKind);
                var clipAuthoringIds = new List<string>();
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    if (clip != null)
                        clipAuthoringIds.Add(clip.AuthoringId);
                    if (clip != null)
                        duration = OrchestrationTime.Max(duration, clip.EndTime);
                }
                for (int markerIndex = 0; markerIndex < track.Markers.Count; markerIndex++)
                {
                    TimelineMarker marker = track.Markers[markerIndex];
                    if (marker == null)
                        continue;
                    duration = OrchestrationTime.Max(duration, OrchestrationTime.Max(OrchestrationTime.FromRaw(1), marker.Time));
                    TimelineGraphBinding graphBinding = graphBindings.ResolveMarker(marker, closure);
                    markers.Add(new TimelineContentMarker(
                        marker.AuthoringId,
                        track.AuthoringId,
                        TimelineClipExecutionPolicy.FromDomain(marker.ExecutionDomain),
                        marker.Time,
                        marker.ForceTriggerOnInitialSeek,
                        graphBinding.GraphId,
                        graphBinding.Revision,
                        track.PersistentMuted));
                }
                tracks.Add(new TimelineContentTrack(
                    track.AuthoringId,
                    track.ContractKind,
                    TimelineClipExecutionPolicy.FromDomain(track.ExecutionDomain),
                    trackIndex,
                    track.PersistentMuted,
                    clipAuthoringIds,
                    trackContract.LogicSampleExtension,
                    track.Name));
            }
            for (int sectionIndex = 0; sectionIndex < timeline.Sections.Count; sectionIndex++)
            {
                TimelineSection section = timeline.Sections[sectionIndex];
                if (section != null)
                    duration = OrchestrationTime.Max(duration, section.Time);
                if (section != null)
                    sections.Add(new TimelineContentSection(
                        section.AuthoringId,
                        section.Time,
                        section.BranchId));
            }
            for (int trackIndex = 0; trackIndex < timeline.Tracks.Count; trackIndex++)
            {
                Track track = timeline.Tracks[trackIndex];
                if (track == null)
                    continue;
                TimelineTrackContract trackContract = catalog.RequireTrack(track.ContractKind);
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    if (clip == null)
                        continue;
                    TimelineClipContract clipContract = catalog.RequireClip(clip.ContractKind);
                    if (!trackContract.AllowsClip(clipContract.Kind))
                        continue;
                    TimelineClipExecutionPhase executionPhase = clipContract.DefaultExecutionPhase;
                    if (clip is ITimelineClipExecutionPhaseSource phaseSource)
                        executionPhase = phaseSource.TimelineExecutionPhase;
                    TimelineClipExitSource exitSource = TimelineClipExitSource.FrameBoundary;
                    if (clip is ITimelineClipExitSource exitSourceSource)
                        exitSource = exitSourceSource.ClipExitSource;
                    if (!clipContract.SupportsExecutionPhase(executionPhase))
                    {
                        errors.Add($"timeline_clip_execution_phase:{clip.AuthoringId}:phase '{executionPhase}' is not supported by '{clipContract.Kind}'.");
                        continue;
                    }
                    var bindingUses = new List<TimelineContentBindingUse>();
                    var curves = new List<TimelineContentCurve>();
                    int weightCurveIndex = -1;
                    int easeInCurveIndex = -1;
                    int easeOutCurveIndex = -1;
                    IReadOnlyList<TimelineCurveChannelDescriptor> curveDescriptors = clip.CurveChannels;
                    for (int curveIndex = 0; curveIndex < curveDescriptors.Count; curveIndex++)
                    {
                        TimelineCurveChannelDescriptor descriptor = curveDescriptors[curveIndex];
                        switch (descriptor.Role)
                        {
                            case TimelineCurveChannelRole.Weight: weightCurveIndex = curves.Count; break;
                            case TimelineCurveChannelRole.EaseIn: easeInCurveIndex = curves.Count; break;
                            case TimelineCurveChannelRole.EaseOut: easeOutCurveIndex = curves.Count; break;
                        }
                        AnimationCurve sourceCurve = descriptor.Read(clip);
                        Keyframe[] keys = sourceCurve?.keys ?? Array.Empty<Keyframe>();
                        var curveKeys = new TimelineContentCurveKey[keys.Length];
                        for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                        {
                            Keyframe key = keys[keyIndex];
                            curveKeys[keyIndex] = new TimelineContentCurveKey(
                                key.time,
                                key.value,
                                key.inTangent,
                                key.outTangent,
                                key.inWeight,
                                key.outWeight,
                                (int)key.weightedMode);
                        }
                        curves.Add(new TimelineContentCurve(
                            descriptor.ChannelId.Value,
                            descriptor.DisplayName,
                            descriptor.TimeDomain,
                            descriptor.ValueDomain,
                            (int)(sourceCurve?.preWrapMode ?? WrapMode.ClampForever),
                            (int)(sourceCurve?.postWrapMode ?? WrapMode.ClampForever),
                            curveKeys));
                    }
                    if (clip is ITimelineExternalBindingUseSource bindingSource && bindingSource.ExternalBindingUses != null)
                    {
                        for (int useIndex = 0; useIndex < bindingSource.ExternalBindingUses.Count; useIndex++)
                        {
                            TimelineExternalBindingUse use = bindingSource.ExternalBindingUses[useIndex];
                            if (use != null)
                                bindingUses.Add(new TimelineContentBindingUse(use.BindingId, use.ValueKind, use.Access, use.Lifetime, use.TargetBindingId));
                        }
                    }
                    clips.Add(new TimelineContentClip(
                        clip.AuthoringId,
                        clipContract.Kind,
                        track.AuthoringId,
                        TimelineClipExecutionPolicy.FromDomain(
                            track.ExecutionDomain),
                        clip.CaptureTimeRange(),
                        executionPhase,
                        exitSource,
                        clipContract.ContentKind,
                        clipContract.SupportsStop,
                        track.PersistentMuted,
                        clip.CaptureContentParameters(),
                        bindingUses,
                        curves,
                        clip.ContractKind == TimelineContractKinds.TreeClip ? graphBindings.ResolveTreeClip(clip, closure) : default,
                        clip.CaptureSampling(weightCurveIndex, easeInCurveIndex, easeOutCurveIndex)));
                    if (clip is ITimelineContentClosureSource closureSource)
                        closureSource.CollectContentClosure(closure);
                }
            }

            var bindings = new List<TimelineBindingDeclaration>();
            for (int i = 0; i < timeline.ExternalBindings.Count; i++)
            {
                TimelineExternalBindingDeclaration binding = timeline.ExternalBindings[i];
                if (binding == null)
                    continue;
                bindings.Add(new TimelineBindingDeclaration(
                    binding.BindingId,
                    binding.Domain,
                    binding.ParameterId,
                    binding.ValueKind,
                    binding.Access,
                    binding.Lifetime));
            }
            clips.Sort((left, right) => string.CompareOrdinal(left.AuthoringId, right.AuthoringId));
            markers.Sort((left, right) =>
            {
                int frame = left.Time.CompareTo(right.Time);
                return frame != 0 ? frame : string.CompareOrdinal(left.MarkerId, right.MarkerId);
            });
            sections.Sort((left, right) =>
            {
                int frame = left.Time.CompareTo(right.Time);
                return frame != 0 ? frame : string.CompareOrdinal(left.AuthoringId, right.AuthoringId);
            });
            bindings.Sort((left, right) => string.CompareOrdinal(left.BindingId, right.BindingId));
            errors.AddRange(closure.Errors);
            List<TimelineContentDependency> dependencies = new List<TimelineContentDependency>(closure.Dependencies);
            dependencies.Sort((left, right) => string.CompareOrdinal(left.Identity, right.Identity));
            if (errors.Count > 0)
                return new TimelineContentDiscoveryResult(null, errors, null);
            string rootFingerprint = TimelineAuthoringFingerprint.Compute(timeline);
            var hashParts = new List<string>
            {
                "timeline-content-closure/1",
                timeline.AuthoringId,
                rootFingerprint
            };
            for (int i = 0; i < dependencies.Count; i++)
            {
                hashParts.Add(dependencies[i].Identity);
                hashParts.Add(dependencies[i].Kind);
                hashParts.Add(dependencies[i].ContentHash);
            }
            for (int i = 0; i < tracks.Count; i++)
            {
                hashParts.Add(tracks[i].ContractKind);
                hashParts.Add(tracks[i].ExecutionPolicy.Domain.ToString("G"));
                hashParts.Add(tracks[i].ExecutionPolicy.OutputKind.ToString("G"));
                if (tracks[i].LogicSampleExtension.HasValue)
                {
                    TimelineLogicSampleExtension extension = tracks[i].LogicSampleExtension.Value;
                    hashParts.Add(extension.Identity.DomainId);
                    hashParts.Add(extension.Identity.OperationId);
                    hashParts.Add(extension.SemanticVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            for (int i = 0; i < clips.Count; i++)
            {
                hashParts.Add(clips[i].AuthoringId);
                hashParts.Add(clips[i].ExecutionPolicy.Domain.ToString("G"));
                hashParts.Add(clips[i].ExecutionPolicy.OutputKind.ToString("G"));
            }
            for (int i = 0; i < markers.Count; i++)
            {
                hashParts.Add(markers[i].MarkerId);
                hashParts.Add(markers[i].ExecutionPolicy.Domain.ToString("G"));
                hashParts.Add(markers[i].ExecutionPolicy.OutputKind.ToString("G"));
                hashParts.Add(markers[i].Time.RawValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                hashParts.Add(markers[i].ForceTriggerOnInitialSeek ? "1" : "0");
            }
            var content = new TimelineContentUnit(
                $"timeline:{timeline.AuthoringId}",
                timeline.AuthoringId,
                SourceContentHasher.Hash(hashParts.ToArray()),
                rootFingerprint,
                timeline.Name,
                duration,
                timeline.Loop,
                tracks,
                clips,
                markers,
                sections,
                bindings,
                dependencies);
            return new TimelineContentDiscoveryResult(content, errors, PublishIdentities(content));
        }
    }
}
