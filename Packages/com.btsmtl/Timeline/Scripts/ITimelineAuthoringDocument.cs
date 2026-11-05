#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineAuthoringReferenceValue
    {
        public TimelineAuthoringReferenceValue(UnityEngine.Object asset, string errorCode = "", string errorMessage = "")
        {
            Asset = asset;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }
        public UnityEngine.Object Asset { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public bool IsMissing => !Asset && !string.IsNullOrEmpty(ErrorCode);
    }

    public interface ITimelineAuthoringDocument
    {
        string SourcePath { get; }
        bool HasUnsavedChanges { get; }
        void Prepare(TimelineData timeline, UnityEngine.ScriptableObject graph = null);
        BTSMTL.Authoring.Graph.IGraphNodeInterfaceResolver PrepareAuthoringContext(UnityEngine.ScriptableObject graph);
        UnityEngine.ScriptableObject ResolveGraph(string authoringId);
        void Save();
    }

    public interface ITimelineAuthoringResourceDocument
    {
        IReadOnlyList<GraphAuthoringResourceChoice> ResourceScopes();
        string ResourceScope(TimelineData timeline);
        string ResourceContext(TimelineData timeline);
        void SelectResourceScope(TimelineData timeline, string scope);
        GraphAuthoringResourceDeclaration ResourceDeclaration(TimelineData timeline, string domain);
        IReadOnlyList<GraphAuthoringResourceChoice> ResourceReferences(TimelineData timeline, string domain,
            UnityEngine.Object source = null);
        GraphAuthoringResourceObjectResult ResourceObjects(TimelineData timeline, string domain, UnityEngine.Object source);
        UnityEngine.Object ResolveResourceObject(string value);
        TimelineAuthoringReferenceValue ResolveResource(TimelineData timeline, string domain, string value);
    }

    public interface ITimelineAuthoringResourceClip
    {
        string ResourceReference { get; }
        TimelineAuthoringReferenceValue ResourceResolution { get; }
        void BindResourceResolution(TimelineAuthoringReferenceValue resolution);
        void BindSourceAsset(UnityEngine.Object source);
    }
}
#endif
