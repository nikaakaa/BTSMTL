#if UNITY_EDITOR
using System;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using ThirdPersonCamera;
using ThirdPersonCharacter.Pipeline.Motion.RootMotion;
using ThirdPersonSimulation;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    sealed class TimelineClipCreationRequest
    {
        public string TrackAuthoringId;
        public string Kind;
        public TimelineExecutionDomain ExecutionDomain;
        public FixedScalar StartTime;
        public FixedScalar EndTime;
        public FixedScalar DefaultEndTime;
        public FixedScalar TimelineEndTime;
        public UnityEngine.Object Resource;
        public RootMotionCurveAsset SourceCurve;
        public float SourceStartTime;
        public float SourceEndTime;
        public ExtraPolationMode Extrapolation = ExtraPolationMode.None;
        public string BlendProfileId = string.Empty;
        public string CurveId = "MotionCurve";
        public TimelineMotionContributionSpace Space = TimelineMotionContributionSpace.Local;
        public TimelineMotionChannel Channel = TimelineMotionChannel.Action;
        public TimelineMotionBlendMode BlendMode = TimelineMotionBlendMode.Override;
        public int Priority = 100;
        public bool ConsumeLowerChannels = true;
        public TimelineCameraMode CameraMode = TimelineCameraMode.SkillCloseup;
        public int CameraPriority = 100;
        public float CameraBlendInSeconds = 0.15f;
        public float CameraBlendOutSeconds = 0.2f;
        public string CameraTargetKey = string.Empty;
        public TimelineCameraInterruptPolicy CameraInterruptPolicy = TimelineCameraInterruptPolicy.BlendOut;
        public TimelineCameraLookResponseMode CameraLookResponse = TimelineCameraLookResponseMode.Suppressed;
        public float ManualOrbitWeight;
        public float PitchResponseWeight = 1f;
        public float YawResponseWeight = 1f;
        public string TargetBindingId = string.Empty;
        public string ParameterBindingId = string.Empty;
        public ScriptableObject TreeGraph;
        public Type TreeGraphType;
        public string NewTreeGraphName = string.Empty;
        public TimelineClipExitSource TreeExitSource = TimelineClipExitSource.FrameBoundary;
    }

    sealed class TimelineClipCreationPopup : PopupWindowContent
    {
        readonly TimelineClipCreationRequest m_Request;
        readonly IReadOnlyList<TimelineExternalBindingDeclaration> m_Bindings;
        readonly Func<TimelineClipCreationRequest, string> m_Create;
        string m_Error;
        string m_SubmitError;

        public TimelineClipCreationPopup(
            TimelineClipCreationRequest request,
            IReadOnlyList<TimelineExternalBindingDeclaration> bindings,
            Func<TimelineClipCreationRequest, string> create)
        {
            m_Request = request;
            m_Bindings = bindings ?? Array.Empty<TimelineExternalBindingDeclaration>();
            m_Create = create;
        }

        public override Vector2 GetWindowSize() => new Vector2(380, 520);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Add Clip", EditorStyles.boldLabel);
            m_Request.StartTime = DrawTime("Start (Seconds)", m_Request.StartTime);
            bool dynamicTreeClip = IsTreeClip && m_Request.TreeExitSource == TimelineClipExitSource.TreeDecision;
            if (dynamicTreeClip && m_Request.ExecutionDomain == TimelineExecutionDomain.Presentation &&
                m_Request.EndTime == m_Request.DefaultEndTime)
                m_Request.EndTime = FixedScalar.Max(
                    m_Request.StartTime + FixedScalar.FromRaw(1),
                    m_Request.TimelineEndTime);
            if (dynamicTreeClip && m_Request.ExecutionDomain == TimelineExecutionDomain.Logic)
                EditorGUILayout.LabelField("End (Seconds)", $"Timeline End ({m_Request.EndTime})");
            else if (dynamicTreeClip && m_Request.ExecutionDomain == TimelineExecutionDomain.Presentation)
                m_Request.EndTime = DrawTime("Layout End (Seconds)", m_Request.EndTime);
            else
                m_Request.EndTime = DrawTime("End (Seconds)", m_Request.EndTime);

            if (m_Request.Kind == TimelineContractKinds.AnimationClip)
            {
                UnityEngine.Object previousResource = m_Request.Resource;
                m_Request.Resource = EditorGUILayout.ObjectField("Animation Clip", m_Request.Resource, typeof(UnityEngine.AnimationClip), false);
                if (!ReferenceEquals(previousResource, m_Request.Resource) && m_Request.Resource is UnityEngine.AnimationClip animation)
                {
                    FixedScalar resourceEndTime = m_Request.StartTime + FixedScalar.FromDouble(animation.length);
                    if (m_Request.EndTime == m_Request.DefaultEndTime)
                        m_Request.EndTime = resourceEndTime;
                    m_Request.DefaultEndTime = resourceEndTime;
                }
                m_Request.Extrapolation = (ExtraPolationMode)EditorGUILayout.EnumPopup("Extrapolation", m_Request.Extrapolation);
                m_Request.BlendProfileId = EditorGUILayout.TextField("Blend Profile", m_Request.BlendProfileId);
            }
            else if (m_Request.Kind == TimelineContractKinds.TreeClip)
            {
                if (IsTreeClip)
                {
                    if (m_Request.ExecutionDomain == TimelineExecutionDomain.Presentation)
                        m_Request.TreeExitSource = (TimelineClipExitSource)EditorGUILayout.EnumPopup("Exit Source", m_Request.TreeExitSource);
                    m_Request.TreeGraph = (ScriptableObject)EditorGUILayout.ObjectField(
                        "Skill Graph",
                        m_Request.TreeGraph,
                        m_Request.TreeGraphType,
                        false);
                    m_Request.NewTreeGraphName = EditorGUILayout.TextField("New Graph Name", m_Request.NewTreeGraphName);
                    EditorGUILayout.HelpBox("Skill Graph 为空时按名称在 Timeline 资产内创建子资产图。", MessageType.Info);
                }
            }

            if (m_Request.Kind == TimelineContractKinds.MotionCurveClip)
            {
                RootMotionCurveAsset previousSource = m_Request.SourceCurve;
                m_Request.SourceCurve = (RootMotionCurveAsset)EditorGUILayout.ObjectField(
                    "Root Motion Curve",
                    m_Request.SourceCurve,
                    typeof(RootMotionCurveAsset),
                    false);
                if (!ReferenceEquals(previousSource, m_Request.SourceCurve) && m_Request.SourceCurve)
                {
                    m_Request.SourceStartTime = 0f;
                    m_Request.SourceEndTime = m_Request.SourceCurve.Duration;
                    FixedScalar sourceEndTime = m_Request.StartTime + FixedScalar.FromDouble(m_Request.SourceCurve.Duration);
                    if (m_Request.EndTime == m_Request.DefaultEndTime)
                        m_Request.EndTime = sourceEndTime;
                    m_Request.DefaultEndTime = sourceEndTime;
                }
                m_Request.CurveId = EditorGUILayout.TextField("Curve Id", m_Request.CurveId);
                m_Request.SourceStartTime = EditorGUILayout.FloatField("Source Start (s)", m_Request.SourceStartTime);
                m_Request.SourceEndTime = EditorGUILayout.FloatField("Source End (s)", m_Request.SourceEndTime);
                m_Request.Space = (TimelineMotionContributionSpace)EditorGUILayout.EnumPopup("Space", m_Request.Space);
                m_Request.Channel = (TimelineMotionChannel)EditorGUILayout.EnumPopup("Channel", m_Request.Channel);
                m_Request.BlendMode = (TimelineMotionBlendMode)EditorGUILayout.EnumPopup("Blend Mode", m_Request.BlendMode);
                m_Request.Priority = EditorGUILayout.IntField("Priority", m_Request.Priority);
                m_Request.ConsumeLowerChannels = EditorGUILayout.Toggle("Consume Lower", m_Request.ConsumeLowerChannels);
            }
            else if (m_Request.Kind == TimelineContractKinds.CameraStateClip)
            {
                m_Request.CameraMode = (TimelineCameraMode)EditorGUILayout.EnumPopup("Mode", m_Request.CameraMode);
                m_Request.CameraPriority = EditorGUILayout.IntField("Priority", m_Request.CameraPriority);
                m_Request.CameraBlendInSeconds = EditorGUILayout.FloatField("Blend In", m_Request.CameraBlendInSeconds);
                m_Request.CameraBlendOutSeconds = EditorGUILayout.FloatField("Blend Out", m_Request.CameraBlendOutSeconds);
                m_Request.CameraTargetKey = EditorGUILayout.TextField("Target Key", m_Request.CameraTargetKey);
                m_Request.CameraInterruptPolicy = (TimelineCameraInterruptPolicy)EditorGUILayout.EnumPopup("Interrupt", m_Request.CameraInterruptPolicy);
            }
            else if (m_Request.Kind == TimelineContractKinds.CameraResponseClip)
            {
                m_Request.CameraLookResponse = (TimelineCameraLookResponseMode)EditorGUILayout.EnumPopup("Look Response", m_Request.CameraLookResponse);
                m_Request.ManualOrbitWeight = EditorGUILayout.Slider("Manual Orbit", m_Request.ManualOrbitWeight, 0f, 1f);
                m_Request.PitchResponseWeight = EditorGUILayout.Slider("Pitch Response", m_Request.PitchResponseWeight, 0f, 1f);
                m_Request.YawResponseWeight = EditorGUILayout.Slider("Yaw Response", m_Request.YawResponseWeight, 0f, 1f);
                m_Request.CameraPriority = EditorGUILayout.IntField("Priority", m_Request.CameraPriority);
            }
            else if (m_Request.Kind == TimelineContractKinds.CameraEffectClip)
            {
                UnityEngine.Object previousResource = m_Request.Resource;
                m_Request.Resource = EditorGUILayout.ObjectField("Effect", m_Request.Resource, typeof(CameraEffectAsset), false);
                if (!ReferenceEquals(previousResource, m_Request.Resource) && m_Request.Resource is ITimelineCameraSourceAsset source)
                {
                    m_Request.SourceStartTime = 0f;
                    m_Request.SourceEndTime = source.SourceDuration;
                    FixedScalar sourceEnd = m_Request.StartTime + FixedScalar.FromDouble(source.SourceDuration);
                    if (m_Request.EndTime == m_Request.DefaultEndTime)
                        m_Request.EndTime = sourceEnd;
                    m_Request.DefaultEndTime = sourceEnd;
                }
                if (m_Request.Resource is ITimelineCameraSourceAsset)
                {
                    m_Request.SourceStartTime = EditorGUILayout.FloatField("源起点（秒）", m_Request.SourceStartTime);
                    m_Request.SourceEndTime = EditorGUILayout.FloatField("源终点（秒）", m_Request.SourceEndTime);
                }
            }
            else if (m_Request.Kind == TimelineContractKinds.ScenePresentationParameterCurveClip)
            {
                BuildBindingPopup("Target Binding", true, ref m_Request.TargetBindingId);
                BuildBindingPopup("Parameter Binding", false, ref m_Request.ParameterBindingId);
            }

            m_Error = Validate();
            if (!string.IsNullOrEmpty(m_Error))
                EditorGUILayout.HelpBox(m_Error, MessageType.Error);
            if (!string.IsNullOrEmpty(m_SubmitError))
                EditorGUILayout.HelpBox(m_SubmitError, MessageType.Error);
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(m_Error)))
            {
                if (GUILayout.Button("Create"))
                {
                    string error = m_Create(m_Request);
                    if (string.IsNullOrEmpty(error))
                        editorWindow.Close();
                    else
                        m_SubmitError = error;
                }
            }
        }

        string Validate()
        {
            if (m_Request.StartTime < FixedScalar.Zero || m_Request.EndTime <= m_Request.StartTime)
                return "帧范围必须满足 Start < End。";
            if (m_Request.Kind == TimelineContractKinds.AnimationClip && m_Request.Resource is not UnityEngine.AnimationClip)
                return "必须选择已有 AnimationClip。";
            if (m_Request.Kind == TimelineContractKinds.MotionCurveClip)
            {
                if (!m_Request.SourceCurve)
                    return "必须选择正式 RootMotionCurveAsset。";
                if (!m_Request.SourceCurve.TryValidate(out string sourceError))
                    return sourceError;
                if (m_Request.SourceStartTime < 0f ||
                    m_Request.SourceEndTime <= m_Request.SourceStartTime ||
                    m_Request.SourceEndTime > m_Request.SourceCurve.Duration)
                    return "Source 时间范围必须位于 RootMotionCurveAsset 内。";
                FixedScalar sourceEndTime = m_Request.StartTime + FixedScalar.FromDouble(m_Request.SourceEndTime - m_Request.SourceStartTime);
                if (sourceEndTime > m_Request.EndTime)
                    return "Clip 结束秒数必须覆盖 Source 时间范围。";
            }
            if (m_Request.Kind == TimelineContractKinds.CameraEffectClip && m_Request.Resource is not CameraEffectAsset)
                return "Camera Effect 必须选择正式相机效果资源。";
            if (m_Request.Kind == TimelineContractKinds.ScenePresentationParameterCurveClip &&
                (string.IsNullOrEmpty(m_Request.TargetBindingId) || string.IsNullOrEmpty(m_Request.ParameterBindingId)))
                return "Scene 参数 Clip 必须选择 Target 和 Parameter binding。";
            if (IsTreeClip && m_Request.TreeGraph == null && string.IsNullOrWhiteSpace(m_Request.NewTreeGraphName))
                return "TreeClip必须绑定或创建 FlowCanvas SkillGraph。";
            return string.Empty;
        }

        static FixedScalar DrawTime(string label, FixedScalar time)
        {
            EditorGUI.BeginChangeCheck();
            double seconds = EditorGUILayout.DoubleField(label, time.ToDouble());
            return EditorGUI.EndChangeCheck() ? FixedScalar.FromDouble(seconds) : time;
        }

        bool IsTreeClip => m_Request.Kind == TimelineContractKinds.TreeClip;

        void BuildBindingPopup(string label, bool target, ref string value)
        {
            var options = new List<string> { "Select binding" };
            for (int index = 0; index < m_Bindings.Count; index++)
            {
                TimelineExternalBindingDeclaration binding = m_Bindings[index];
                bool valid = target
                    ? binding.ValueKind == TimelineBindingValueKind.Target && binding.Access == TimelineBindingAccess.Input && binding.Lifetime == TimelineBindingLifetime.Call
                    : (binding.ValueKind == TimelineBindingValueKind.Scalar || binding.ValueKind == TimelineBindingValueKind.Boolean) && binding.Access == TimelineBindingAccess.Write && binding.Lifetime == TimelineBindingLifetime.Tick;
                if (valid)
                    options.Add(binding.BindingId);
            }
            int selected = Mathf.Max(0, options.IndexOf(value) >= 0 ? options.IndexOf(value) : 0);
            int next = EditorGUILayout.Popup(label, selected, options.ToArray());
            value = next > 0 ? options[next] : string.Empty;
        }
    }
}
#endif
