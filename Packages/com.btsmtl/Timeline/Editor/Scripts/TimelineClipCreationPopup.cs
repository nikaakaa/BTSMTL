#if UNITY_EDITOR
using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
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
        public OrchestrationTime StartTime;
        public OrchestrationTime EndTime;
        public OrchestrationTime DefaultEndTime;
        public OrchestrationTime TimelineEndTime;
        public UnityEngine.Object Resource;
        public Clip Prototype;
        public TimelineAuthoringClipConfiguration Configuration;
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
        readonly IReadOnlyList<TimelineAuthoringPropertyAttribute> m_Properties;
        string m_Error;
        string m_SubmitError;

        public TimelineClipCreationPopup(TimelineClipCreationRequest request,
            IReadOnlyList<TimelineExternalBindingDeclaration> bindings, Func<TimelineClipCreationRequest, string> create)
        {
            m_Request = request;
            m_Bindings = bindings;
            m_Create = create;
            m_Properties = TimelineAuthoringClipBinding.Properties(request.Prototype);
        }

        public override Vector2 GetWindowSize() => new Vector2(380, 520);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Add Clip", EditorStyles.boldLabel);
            m_Request.StartTime = DrawTime("Start (Seconds)", m_Request.StartTime);
            bool dynamicTreeClip = IsTreeClip && m_Request.TreeExitSource == TimelineClipExitSource.TreeDecision;
            if (dynamicTreeClip && m_Request.ExecutionDomain == TimelineExecutionDomain.Presentation &&
                m_Request.EndTime == m_Request.DefaultEndTime)
                m_Request.EndTime = OrchestrationTime.Max(m_Request.StartTime + OrchestrationTime.FromRaw(1), m_Request.TimelineEndTime);
            if (dynamicTreeClip && m_Request.ExecutionDomain == TimelineExecutionDomain.Logic)
                EditorGUILayout.LabelField("End (Seconds)", $"Timeline End ({m_Request.EndTime})");
            else
                m_Request.EndTime = DrawTime(dynamicTreeClip ? "Layout End (Seconds)" : "End (Seconds)", m_Request.EndTime);

            if (IsTreeClip)
            {
                if (m_Request.ExecutionDomain == TimelineExecutionDomain.Presentation)
                    m_Request.TreeExitSource = (TimelineClipExitSource)EditorGUILayout.EnumPopup("Exit Source", m_Request.TreeExitSource);
                m_Request.TreeGraph = (ScriptableObject)EditorGUILayout.ObjectField("Skill Graph", m_Request.TreeGraph,
                    m_Request.TreeGraphType, false);
                m_Request.NewTreeGraphName = EditorGUILayout.TextField("New Graph Name", m_Request.NewTreeGraphName);
                EditorGUILayout.HelpBox("Skill Graph 为空时按名称在 Timeline 资产内创建子资产图。", MessageType.Info);
            }
            if (m_Request.Prototype is ITimelineAuthoringClipSource source)
            {
                UnityEngine.Object previous = m_Request.Resource;
                m_Request.Resource = EditorGUILayout.ObjectField("Source", previous, source.SourceAssetType, false);
                if (!ReferenceEquals(previous, m_Request.Resource) && m_Request.Resource)
                {
                    float duration = source.GetSourceDuration(m_Request.Resource);
                    OrchestrationTime end = m_Request.StartTime + OrchestrationTime.FromSeconds(duration);
                    if (m_Request.EndTime == m_Request.DefaultEndTime)
                        m_Request.EndTime = end;
                    m_Request.DefaultEndTime = end;
                    foreach (TimelineAuthoringPropertyAttribute property in m_Properties)
                    {
                        if (property.IsSourceAsset)
                            m_Request.Configuration.Set(property.PropertyId, m_Request.Resource);
                        if (property.MemberName == nameof(SampledClip.SourceStartTime))
                            m_Request.Configuration.Set(property.PropertyId, 0f);
                        if (property.MemberName == nameof(SampledClip.SourceEndTime))
                            m_Request.Configuration.Set(property.PropertyId, duration);
                    }
                }
            }
            foreach (TimelineAuthoringPropertyAttribute property in m_Properties)
            {
                if (property.IsSourceAsset || !property.Supports(m_Request.Prototype, m_Request.Resource))
                    continue;
                m_Request.Configuration.Set(property.PropertyId, TimelineAuthoringPropertyGUI.Draw(property,
                    ObjectNames.NicifyVariableName(property.PropertyId), m_Request.Configuration[property.PropertyId],
                    TimelineAuthoringClipBinding.ValueType(m_Request.Prototype, property), m_Bindings));
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
            if (m_Request.StartTime < OrchestrationTime.Zero || m_Request.EndTime <= m_Request.StartTime)
                return "帧范围必须满足 Start < End。";
            if (m_Request.Prototype is ITimelineAuthoringClipSource source && !m_Request.Resource)
                return source.SourceMissingError;
            if (IsTreeClip && m_Request.TreeGraph == null && string.IsNullOrWhiteSpace(m_Request.NewTreeGraphName))
                return "TreeClip必须绑定或创建 FlowCanvas SkillGraph。";
            return string.Empty;
        }

        static OrchestrationTime DrawTime(string label, OrchestrationTime time)
        {
            EditorGUI.BeginChangeCheck();
            double seconds = EditorGUILayout.DoubleField(label, time.AsSingle);
            return EditorGUI.EndChangeCheck() ? OrchestrationTime.FromSeconds(seconds) : time;
        }

        bool IsTreeClip => m_Request.Kind == TimelineContractKinds.TreeClip;

    }
}
#endif
