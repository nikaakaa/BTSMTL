using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Workspace;
using BTSMTL.Timeline;

namespace BTSMTL.WebWorkbench;

public sealed class AuthoringDocumentReception
{
    public AuthoringDocumentReception(AuthoringDocumentSet documents, AuthoringDefinitionSet definitions,
        Func<AuthoringContentReference, AuthoringGraphDocument> resolveGraph)
    {
        Documents = documents;
        Definitions = definitions;
        RootDefinition = definitions.Roots[documents.Root.Kind];
        RootFields = AuthoringFieldDefinition.ReceiveFields(RootDefinition.Fields, documents.Root.Fields,
            "root:" + documents.Root.Id, documents.Root.DisplayName);
        foreach (object document in documents.Documents.Values)
        {
            if (document is AuthoringGraphDocument graph) ReceiveGraph(graph, resolveGraph);
            else if (document is AuthoringTimelineDocument timeline) ReceiveTimeline(timeline);
        }
    }

    public AuthoringDocumentSet Documents { get; }
    public AuthoringDefinitionSet Definitions { get; }
    public AuthoringObjectDefinition RootDefinition { get; }
    public IReadOnlyDictionary<GraphAuthoringFieldId, AuthoringValue> RootFields { get; }
    public AuthoringValue RootMember(string member) => RootFields[RootDefinition.Members[member]];

    void ReceiveGraph(AuthoringGraphDocument graph, Func<AuthoringContentReference, AuthoringGraphDocument> resolve)
    {
        if (graph.RoleId.Equals(Definitions.MachineRole))
        {
            string path = "graph:" + graph.Id;
            AuthoringFieldDefinition.ReceiveFields(Definitions.MachineGraph.Fields, graph.Fields, path, graph.DisplayName);
            foreach (AuthoringEdgeDocument edge in graph.Edges.Span)
                AuthoringFieldDefinition.ReceiveFields(Definitions.Edge.Fields, edge.Fields,
                    path + "/edge:" + edge.Id, order: edge.Order);
            foreach (AuthoringNodeDocument node in graph.Nodes.Span)
                Definitions.Nodes[node.CapabilityId].Receive(node, graph.RoleId, Definitions.InputTypes);
            AuthoringStateMachineContract.ValidateDocument(graph, Definitions.StateKinds,
                Definitions.MachineBodyField, Definitions.MachineConditionField, resolve, Definitions.Machine, true);
        }
        else
            AuthoringFlowGraphContract.ReceiveDocument(graph, Definitions.FlowGraph.Fields, Definitions.Edge.Fields,
                Definitions.Nodes, Definitions.InputTypes, Definitions.FlowAnchors[graph.RoleId], true);
    }

    void ReceiveTimeline(AuthoringTimelineDocument timeline)
    {
        string path = "timeline:" + timeline.Id;
        AuthoringFieldDefinition.ReceiveFields(Definitions.Timeline.Fields, timeline.Fields, path,
            timeline.DisplayName, loop: timeline.Loop);
        foreach (AuthoringTimelineTrackDocument track in timeline.Tracks.Span)
        {
            string trackPath = path + "/track:" + track.Id;
            AuthoringObjectDefinition trackDefinition = Definitions.Tracks[track.ContractKind];
            IReadOnlyDictionary<GraphAuthoringFieldId, AuthoringValue> fields = AuthoringFieldDefinition.ReceiveFields(
                trackDefinition.Fields, track.Fields, trackPath, track.DisplayName);
            var domain = Enum.Parse<TimelineExecutionDomain>(
                ((AuthoringScalarValue)fields[trackDefinition.Members["ExecutionDomain"]]).Value);
            TimelineTrackContract contract = Definitions.TimelineContracts.RequireTrack(track.ContractKind);
            if (!contract.SupportsExecutionDomain(domain))
                throw new InvalidOperationException(trackPath + ": 执行域不属于原轨道合同。");
            foreach (AuthoringTimelineClipDocument clip in track.Clips.Span)
            {
                string clipPath = trackPath + "/clip:" + clip.Id;
                AuthoringFieldDefinition.ReceiveFields(Definitions.Clips[clip.ContractKind].Fields,
                    clip.Fields, clipPath, clip.DisplayName);
                Definitions.TimelineContracts.RequireClipPlacement(track.ContractKind, clip.ContractKind, domain,
                    track.Id.Value, clip.Id.Value);
                if (!Definitions.TimelineContracts.RequireClip(clip.ContractKind).AllowsTimeRange(clip.TimeRange))
                    throw new InvalidOperationException(clipPath + ": 时间范围不属于原片段合同。");
            }
            if (contract.OverlapPolicy == TimelineTrackOverlapPolicy.Reject)
                for (int first = 0; first < track.Clips.Length; first++)
                    for (int second = first + 1; second < track.Clips.Length; second++)
                    {
                        AuthoringTimelineClipDocument left = track.Clips.Span[first], right = track.Clips.Span[second];
                        if (left.TimeRange.End > right.TimeRange.Start && right.TimeRange.End > left.TimeRange.Start)
                            throw new InvalidOperationException(trackPath + $": 原轨道不允许片段 {left.Id} 与 {right.Id} 重叠。");
                    }
            foreach (AuthoringTimelineMarkerDocument marker in track.Markers.Span)
            {
                if (marker.ContractKind != Definitions.MarkerKind)
                    throw new InvalidOperationException(trackPath + "/marker:" + marker.Id + ": Marker种类不属于原定义。");
                AuthoringFieldDefinition.ReceiveFields(Definitions.Marker.Fields, marker.Fields,
                    trackPath + "/marker:" + marker.Id, marker.DisplayName, time: marker.Time);
            }
        }
        foreach (AuthoringTimelineBindingDocument binding in timeline.Bindings.Span)
            AuthoringFieldDefinition.ReceiveFields(Definitions.Binding.Fields, binding.Fields,
                path + "/binding:" + binding.Id);
    }
}
