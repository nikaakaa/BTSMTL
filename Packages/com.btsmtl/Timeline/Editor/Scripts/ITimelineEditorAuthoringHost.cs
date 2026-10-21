using System;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    public interface ITimelineEditorAuthoringHost
    {
        TimelineContractCatalog Contracts { get; }
        Type TreeGraphType { get; }
        // 表现域 Tree Track 能否直接添加 Clip；不能时表现域点事件改用 Marker。
        bool AllowsPresentationTreeClips { get; }
        Type TimeGridSourceType { get; }
        int GetTimeGridTickRate(ScriptableObject source);
        ScriptableObject CreateGraph(UnityEngine.Object container, string graphName, bool isTrigger);
        void OpenGraph(ScriptableObject source);
        void MutateOwnedContent(TimelineData timeline, Action mutation);
        void ShowSourceCreation(ScriptableObject pipeline, Type sourceType, UnityEngine.Object source,
            Func<UnityEngine.Object, string> attach);
        void DrawSourceInspector(ScriptableObject pipeline, UnityEngine.Object source, float sourceTime);
        void ReleaseSourceEditing();
    }
}
