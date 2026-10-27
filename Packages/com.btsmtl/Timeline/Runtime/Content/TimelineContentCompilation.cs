using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTSMTL.Diagnostics;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline
{
    public static class TimelineContentCompilation
    {
        public static TimelineContentUnit Compile(string authoringId, string name, bool loop,
            string rootFingerprint, OrchestrationTime duration, IReadOnlyList<TimelineContentTrack> tracks,
            IReadOnlyList<TimelineContentClip> sourceClips, IReadOnlyList<TimelineContentMarker> sourceMarkers,
            IReadOnlyList<TimelineBindingDeclaration> sourceBindings, IReadOnlyList<TimelineContentDependency> sourceDependencies)
        {
            TimelineContentClip[] clips = sourceClips.OrderBy(clip => clip.AuthoringId, System.StringComparer.Ordinal).ToArray();
            TimelineContentMarker[] markers = sourceMarkers.OrderBy(marker => marker.Time)
                .ThenBy(marker => marker.MarkerId, System.StringComparer.Ordinal).ToArray();
            TimelineBindingDeclaration[] bindings = sourceBindings.OrderBy(binding => binding.BindingId, System.StringComparer.Ordinal).ToArray();
            TimelineContentDependency[] dependencies = sourceDependencies.OrderBy(dependency => dependency.Identity, System.StringComparer.Ordinal).ToArray();
            var hashParts = new List<string> { "timeline-content-closure/1", authoringId, rootFingerprint };
            foreach (TimelineContentDependency dependency in dependencies)
            {
                hashParts.Add(dependency.Identity);
                hashParts.Add(dependency.Kind);
                hashParts.Add(dependency.ContentHash);
            }
            foreach (TimelineContentTrack track in tracks)
            {
                hashParts.Add(track.ContractKind);
                hashParts.Add(track.ExecutionPolicy.Domain.ToString("G"));
                hashParts.Add(track.ExecutionPolicy.OutputKind.ToString("G"));
                if (track.LogicSampleExtension.HasValue)
                {
                    TimelineLogicSampleExtension extension = track.LogicSampleExtension.Value;
                    hashParts.Add(extension.Identity.DomainId);
                    hashParts.Add(extension.Identity.OperationId);
                    hashParts.Add(extension.SemanticVersion.ToString(CultureInfo.InvariantCulture));
                }
            }
            foreach (TimelineContentClip clip in clips)
            {
                hashParts.Add(clip.AuthoringId);
                hashParts.Add(clip.ExecutionPolicy.Domain.ToString("G"));
                hashParts.Add(clip.ExecutionPolicy.OutputKind.ToString("G"));
            }
            foreach (TimelineContentMarker marker in markers)
            {
                hashParts.Add(marker.MarkerId);
                hashParts.Add(marker.ExecutionPolicy.Domain.ToString("G"));
                hashParts.Add(marker.ExecutionPolicy.OutputKind.ToString("G"));
                hashParts.Add(marker.Time.RawValue.ToString(CultureInfo.InvariantCulture));
                hashParts.Add(marker.ForceTriggerOnInitialSeek ? "1" : "0");
            }
            return new TimelineContentUnit($"timeline:{authoringId}", authoringId,
                SourceContentHasher.Hash(hashParts.ToArray()), rootFingerprint, name, duration, loop,
                tracks, clips, markers, bindings, dependencies);
        }

        public static OrchestrationIdentityTable PublishIdentities(TimelineContentUnit content)
        {
            var identities = new List<string> { content.Identity, content.ContentHash };
            for (int ordinal = 0; ordinal < content.Clips.Count; ordinal++)
                identities.Add(content.Clips[ordinal].AuthoringId);
            return OrchestrationIdentityPublication.Create(identities);
        }
    }
}
