#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Timeline
{
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
        UnityEngine.Object ResolveResource(TimelineData timeline, GraphAuthoringResourceReference reference);
        void RegisterResource(Clip clip, GraphAuthoringResourceRegistrationInput input,
            Action<GraphAuthoringResourceReference> apply);
    }

    public interface ITimelineAuthoringResourceClip
    {
        GraphAuthoringResourceReference ResourceReference { get; }
        void BindSourceAsset(UnityEngine.Object source);
    }
}
#endif
