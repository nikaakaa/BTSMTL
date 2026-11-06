using System;
using System.Threading.Tasks;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    public interface ITimelineEditorAuthoringHost
    {
        TimelineContractCatalog Contracts { get; }
        Type TreeGraphType { get; }
        Type TimeGridSourceType { get; }
        int GetTimeGridTickRate(ScriptableObject source);
        ITimelineAuthoringDocument GetAuthoringDocument(UnityEngine.Object source);
        Task<TimelineAsset> OpenTimeline(string identity);
        ScriptableObject CreateGraph(UnityEngine.Object container, string graphName, bool isTrigger);
        void OpenGraph(ScriptableObject source);
        void MutateOwnedContent(TimelineData timeline, Action mutation);
        void ShowSourceCreation(TimelineData timeline, ScriptableObject pipeline, Type sourceType, UnityEngine.Object source,
            Func<UnityEngine.Object, Task<string>> attach);
        void DrawSourceInspector(ScriptableObject pipeline, UnityEngine.Object source, float sourceTime);
        void ReleaseSourceEditing();
    }
}
