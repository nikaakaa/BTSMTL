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
        IReadOnlyList<GraphAuthoringResourceReference> ResourceScopes();
        GraphAuthoringResourceReference ResourceScope(TimelineData timeline);
        void SelectResourceScope(TimelineData timeline, GraphAuthoringResourceReference scope);
        GraphAuthoringResourceDeclaration ResourceDeclaration(TimelineData timeline, string keyKind);
        IReadOnlyList<GraphAuthoringResourceReference> ResourceReferences(TimelineData timeline, string keyKind,
            UnityEngine.Object source = null);
        GraphAuthoringResourceObjectResult ResourceObjects(TimelineData timeline, string keyKind, UnityEngine.Object source);
        UnityEngine.Object ResolveResourceObject(string token);
        TimelineAuthoringReferenceValue ResolveResource(TimelineData timeline, GraphAuthoringResourceReference reference);
        void RegisterResource(Clip clip, GraphAuthoringResourceRegistrationInput input,
            Action<GraphAuthoringResourceReference> apply);
    }

    public interface ITimelineAuthoringResourceClip
    {
        GraphAuthoringResourceReference ResourceReference { get; }
        TimelineAuthoringReferenceValue ResourceResolution { get; }
        void BindResourceResolution(TimelineAuthoringReferenceValue resolution);
        void BindSourceAsset(UnityEngine.Object source);
    }
}
#endif
