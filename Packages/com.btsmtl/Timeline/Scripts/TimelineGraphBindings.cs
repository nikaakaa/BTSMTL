using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace BTSMTL.Timeline
{
    public interface ITimelineGraphBindingSource
    {
        TimelineGraphBinding ResolveTreeClip(Clip clip, TimelineContentClosureBuilder closure);
        TimelineGraphBinding ResolveMarker(TimelineMarker marker, TimelineContentClosureBuilder closure);
    }

#if UNITY_EDITOR || BTSMTL_AUTHORING
    public sealed class TimelineGraphSourceBindings : ITimelineGraphBindingSource
    {
        readonly IReadOnlyDictionary<string, IReadOnlyList<TimelineContentDependency>> m_Graphs;

        public TimelineGraphSourceBindings(IReadOnlyDictionary<string, IReadOnlyList<TimelineContentDependency>> graphs)
        {
            m_Graphs = graphs;
        }

        public TimelineGraphBinding ResolveTreeClip(Clip clip, TimelineContentClosureBuilder closure) =>
            Resolve(((ITimelineTreeClipAuthoringSource)clip).AssetTreeId, closure);

        public TimelineGraphBinding ResolveMarker(TimelineMarker marker, TimelineContentClosureBuilder closure) =>
            Resolve(marker.GraphId, closure);

        TimelineGraphBinding Resolve(string graphId, TimelineContentClosureBuilder closure)
        {
            IReadOnlyList<TimelineContentDependency> sources = m_Graphs[graphId];
            foreach (TimelineContentDependency source in sources)
                closure.AddDependency(source.Identity, source.Kind, source.SourcePath, source.ContentHash);
            string identity = TimelineGraphBinding.TreeDependencyIdentity(graphId);
            return new TimelineGraphBinding(identity, sources.Single(source => source.Identity == identity).ContentHash);
        }
    }
#endif

#if UNITY_EDITOR
    public sealed class TimelineAuthoringGraphBindings : ITimelineGraphBindingSource
    {
        public static Func<UnityEngine.Object, ITimelineAuthoringDocument> Document { get; set; }

        Func<ScriptableObject, string> m_Fingerprint;

        public static void Prepare(TimelineData timeline, ScriptableObject graph = null) =>
            (timeline.AuthoringDocument ?? Document(timeline.SerializedOwner)).Prepare(timeline, graph);

        public TimelineGraphBinding ResolveTreeClip(Clip clip, TimelineContentClosureBuilder closure) =>
            Resolve(clip.Timeline.AuthoringDocument.ResolveGraph(((ITimelineTreeClipAuthoringSource)clip).AssetTreeId),
                false, clip.ExecutionDomain, $"clip:{clip.AuthoringId}", closure);

        public TimelineGraphBinding ResolveMarker(TimelineMarker marker, TimelineContentClosureBuilder closure) =>
            Resolve(marker.Track.Timeline.AuthoringDocument.ResolveGraph(marker.GraphId), true, marker.ExecutionDomain,
                $"track:{marker.Track.AuthoringId}/marker:{marker.AuthoringId}", closure);

        TimelineGraphBinding Resolve(ScriptableObject asset, bool marker, TimelineExecutionDomain domain,
            string sourcePath, TimelineContentClosureBuilder closure)
        {
            if (asset is not ITimelineTreeGraphAsset graph ||
                (marker ? !graph.IsTimelineTrigger : !graph.IsTimelineTree))
                throw new InvalidOperationException($"Timeline '{sourcePath}' requires a {(marker ? "TimelineTrigger" : "TimelineBody")} graph.");
            m_Fingerprint ??= graph.CreateTimelineContentFingerprint();
            graph.CollectTimelineContentClosure(closure, sourcePath, domain, m_Fingerprint);
            string identity = TimelineGraphBinding.TreeDependencyIdentity(graph.AuthoringId);
            for (int i = 0; i < closure.Dependencies.Count; i++)
                if (closure.Dependencies[i].Identity == identity)
                    return new TimelineGraphBinding(identity, closure.Dependencies[i].ContentHash);
            throw new InvalidOperationException($"Timeline graph '{identity}' has no valid content revision: {string.Join(" | ", closure.Errors)}");
        }
    }
#endif
}
