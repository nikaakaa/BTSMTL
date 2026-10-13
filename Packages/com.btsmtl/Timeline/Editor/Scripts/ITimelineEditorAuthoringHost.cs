using System;
using UnityEngine;
using ThirdPersonCamera;

namespace BTSMTL.Timeline.Editor
{
    public interface ITimelineEditorAuthoringHost
    {
        Type TreeGraphType { get; }
        Type TimeGridSourceType { get; }
        int GetTimeGridTickRate(ScriptableObject source);
        ScriptableObject CreateGraph(UnityEngine.Object container, string graphName, bool isTrigger);
        void OpenGraph(ScriptableObject source);
        void MutateOwnedContent(TimelineData timeline, Action mutation);
        void ShowCameraSourceCreation(ScriptableObject pipeline, CameraEffectAsset source, Func<CameraEffectAsset, string> attach);
        void DrawCameraSourceInspector(ScriptableObject pipeline, CameraEffectAsset source, float sourceTime);
        void ReleaseCameraSourceEditing();
    }
}
