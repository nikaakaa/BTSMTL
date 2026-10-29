using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    // 本档角色相机组的输出相机：快速档由预览实例提供，真实档由宿主提供者提供。
    public interface ITimelinePreviewCharacterCamera
    {
        bool TryGetCharacterCamera(out Camera camera, out string reason);
    }

    public sealed partial class TimelineAssetPreviewHost
    {
        sealed class SceneViewPose
        {
            public Vector3 Pivot;
            public Quaternion Rotation;
            public float Size;
            public bool Orthographic;
            public float FieldOfView;
            public bool DynamicClip;
            public float NearClip;
            public float FarClip;
        }

        readonly Dictionary<SceneView, SceneViewPose> m_ObserverPoses = new();

        // 角色相机：Scene 窗口逐帧对齐本档角色相机；关掉回到切换前的观察视角。只改看哪台相机，不碰相机计算。
        public bool ViewsCharacterCamera { get; private set; }
        // 角色相机不可用的原因；可用时为空。
        public string CharacterCameraReason { get; private set; } = string.Empty;

        public void SetViewsCharacterCamera(bool value)
        {
            if (ViewsCharacterCamera == value)
                return;
            ViewsCharacterCamera = value;
            if (!value)
                RestoreObserverViews();
            Changed?.Invoke();
        }

        bool TryGetCharacterCamera(out Camera camera, out string reason)
        {
            camera = null;
            if (!IsOpen)
            {
                reason = "预览未打开。";
                return false;
            }
            if (Mode == TimelinePreviewMode.Fast && (!ShowsScene || m_Instance == null || !m_Instance.IsReady))
            {
                reason = ShowsScene ? "预览角色准备中。" : "未显示角色。";
                return false;
            }
            object owner = Mode == TimelinePreviewMode.Real ? m_Provider : m_Instance;
            if (owner is not ITimelinePreviewCharacterCamera source)
            {
                reason = "预览宿主没有提供角色相机。";
                return false;
            }
            return source.TryGetCharacterCamera(out camera, out reason);
        }

        // 每次 Editor 更新在预览推进之后执行，Scene 窗口看到的是本帧已发布的相机。
        void UpdateCharacterCamera()
        {
            bool available = TryGetCharacterCamera(out Camera camera, out string reason);
            reason = available ? string.Empty : reason ?? string.Empty;
            if (reason != CharacterCameraReason)
            {
                CharacterCameraReason = reason;
                Changed?.Invoke();
            }
            if (!ViewsCharacterCamera || !available)
            {
                RestoreObserverViews();
                return;
            }
            foreach (SceneView view in SceneView.sceneViews)
                if (view)
                    AlignToCamera(view, camera);
        }

        void AlignToCamera(SceneView view, Camera camera)
        {
            SceneView.CameraSettings settings = view.cameraSettings;
            if (!m_ObserverPoses.ContainsKey(view))
                m_ObserverPoses.Add(view, new SceneViewPose
                {
                    Pivot = view.pivot,
                    Rotation = view.rotation,
                    Size = view.size,
                    Orthographic = view.orthographic,
                    FieldOfView = settings.fieldOfView,
                    DynamicClip = settings.dynamicClip,
                    NearClip = settings.nearClip,
                    FarClip = settings.farClip
                });
            view.orthographic = camera.orthographic;
            settings.fieldOfView = camera.fieldOfView;
            settings.dynamicClip = false;
            settings.nearClip = camera.nearClipPlane;
            settings.farClip = camera.farClipPlane;
            // Scene 相机位于轴心沿视线后退 cameraDistance 处；距离只由尺寸与视野决定，先定尺寸再按距离放轴心。
            Transform pose = camera.transform;
            float size = camera.orthographic ? camera.orthographicSize : 1f;
            view.LookAtDirect(pose.position, pose.rotation, size);
            view.LookAtDirect(pose.position + pose.forward * view.cameraDistance, pose.rotation, size);
            view.Repaint();
        }

        void RestoreObserverViews()
        {
            if (m_ObserverPoses.Count == 0)
                return;
            foreach (KeyValuePair<SceneView, SceneViewPose> entry in m_ObserverPoses)
            {
                SceneView view = entry.Key;
                if (!view)
                    continue;
                SceneViewPose pose = entry.Value;
                SceneView.CameraSettings settings = view.cameraSettings;
                view.orthographic = pose.Orthographic;
                settings.fieldOfView = pose.FieldOfView;
                settings.dynamicClip = pose.DynamicClip;
                settings.nearClip = pose.NearClip;
                settings.farClip = pose.FarClip;
                view.LookAtDirect(pose.Pivot, pose.Rotation, pose.Size);
                view.Repaint();
            }
            m_ObserverPoses.Clear();
        }
    }
}
