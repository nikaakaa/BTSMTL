using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using BTSMTL.Timeline;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    [CustomEditor(typeof(TimelineAsset))]
    public sealed class TimelineInspector : UnityEditor.Editor
    {
        VisualElement m_Root;
        string m_ConfigurationError;
        string m_SourceRevision = string.Empty;
        TimelineContractCatalog m_Contracts;
        TimelineAuthoringClipConfiguration m_ClipConfiguration;
        PropertyBinding[] m_ClipProperties = Array.Empty<PropertyBinding>();
        ulong m_ClipRevision;

        sealed class PropertyBinding
        {
            internal TimelineAuthoringPropertyAttribute Attribute;
            internal Type ValueType;
            internal string Label;
            internal object Value;
            internal AnimationCurve Curve;
        }

        void OnEnable()
        {
            m_Contracts = TimelineWorkspaceModeBridge.Authoring.Contracts;
        }

        void OnDisable()
        {
            TimelineInspectorSelection.Changed -= Rebuild;
        }

        public override VisualElement CreateInspectorGUI()
        {
            m_Root = new VisualElement();
            m_Root.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            m_Root.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            return m_Root;
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            TimelineInspectorSelection.Changed += Rebuild;
            Rebuild();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            TimelineInspectorSelection.Changed -= Rebuild;
        }

        void Rebuild()
        {
            m_Root.Clear();
            m_ClipConfiguration = null;
            m_ClipProperties = Array.Empty<PropertyBinding>();
            TimelineAsset asset = (TimelineAsset)target;
            m_SourceRevision = TimelineAuthoringFingerprint.Compute(asset.Data);
            m_Root.Add(new Button(() => TimelineEditorWindow.Open(asset))
            {
                text = "Open Timeline Editor"
            });

            SerializedProperty data = serializedObject.FindProperty("m_Data");
            PropertyField field = new PropertyField(data, "Timeline Data");
            field.BindProperty(data);
            field.SetEnabled(false);
            m_Root.Add(field);

            if (TimelineInspectorSelection.TryGetElement(asset, out object selected))
            {
                if (selected is Clip clip)
                    PrepareClipProperties(clip);
                m_Root.Add(new Label("Selected Timeline Element"));
                m_Root.Add(new IMGUIContainer(() => DrawSelectedInspector(asset, selected)));
            }

            var errors = new List<string>();
            if (!asset.ValidateContent(m_Contracts, errors))
                m_Root.Add(new HelpBox(string.Join("\n", errors), HelpBoxMessageType.Error));
            if (!string.IsNullOrEmpty(m_ConfigurationError))
                m_Root.Add(new HelpBox(m_ConfigurationError, HelpBoxMessageType.Error));
        }

        void DrawSelectedInspector(TimelineAsset asset, object selected)
        {
            if (selected is TimelineMarker marker && GUILayout.Button("Open Trigger Graph"))
                TimelineWorkspaceModeBridge.Authoring.OpenGraph(marker.Graph);
            using var disabled = new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode ||
                TimelineEditorWindow.FindOpen(asset)?.RuntimeObservationReadOnly == true);
            switch (selected)
            {
                case Clip clip:
                    DrawClipInspector(asset, clip);
                    break;
                case Track track:
                    DrawTrackInspector(asset, track);
                    break;
                case TimelineMarker selectedMarker:
                    DrawMarkerInspector(asset, selectedMarker);
                    break;
                case TimelineSection section:
                    DrawSectionInspector(asset, section);
                    break;
            }
        }

        void DrawClipInspector(TimelineAsset asset, Clip clip)
        {
            BtsmtlSlateTimelineProjection.DrawSourceResourceInspector(asset.Data, clip);
            if (!string.IsNullOrEmpty(m_ConfigurationError))
                EditorGUILayout.HelpBox(m_ConfigurationError, MessageType.Error);
            EditorGUILayout.LabelField("Name", clip.Name);
            EditorGUILayout.LabelField("Kind", clip.ContractKind);
            EditorGUILayout.LabelField("Domain (Inherited from Track)", clip.ExecutionDomain.ToString());
            if (clip is TreeClip selectedTree)
            {
                TimelineClipExitSource exitSource = selectedTree.ClipExitSource;
                TimelineClipExitSource nextExitSource = exitSource;
                if (selectedTree.ExecutionDomain == TimelineExecutionDomain.Presentation)
                    nextExitSource = (TimelineClipExitSource)EditorGUILayout.EnumPopup("Exit Source", exitSource);
                else
                    EditorGUILayout.LabelField("Exit Source", TimelineClipExitSource.TreeDecision.ToString());
                if (nextExitSource != exitSource)
                    ApplyTreeExitSource(asset, selectedTree, nextExitSource);
            }
            OrchestrationTime startTime = clip.StartTime;
            OrchestrationTime endTime = clip.EndTime;
            OrchestrationTime easeIn = clip.SelfEaseInTime;
            OrchestrationTime easeOut = clip.SelfEaseOutTime;
            bool isDynamicTreeClip = clip is TreeClip treeClip &&
                treeClip.ClipExitSource == TimelineClipExitSource.TreeDecision;
            EditorGUI.BeginChangeCheck();
            EditorGUI.BeginChangeCheck();
            double startSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Start (Seconds)", startTime.AsSingle));
            if (EditorGUI.EndChangeCheck())
                startTime = OrchestrationTime.FromSeconds(startSeconds);
            bool dynamicLogicTreeClip = isDynamicTreeClip && clip.ExecutionDomain == TimelineExecutionDomain.Logic;
            if (dynamicLogicTreeClip)
                EditorGUILayout.LabelField("End (Seconds)", $"Timeline End ({endTime})");
            else
            {
                EditorGUI.BeginChangeCheck();
                string endLabel = isDynamicTreeClip ? "Layout End (Seconds)" : "End (Seconds)";
                double endSeconds = EditorGUILayout.DoubleField(endLabel, endTime.AsSingle);
                if (EditorGUI.EndChangeCheck())
                    endTime = OrchestrationTime.FromSeconds(endSeconds);
            }
            EditorGUI.BeginChangeCheck();
            double easeInSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Self Ease In (Seconds)", easeIn.AsSingle));
            if (EditorGUI.EndChangeCheck())
                easeIn = OrchestrationTime.FromSeconds(easeInSeconds);
            EditorGUI.BeginChangeCheck();
            double easeOutSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Self Ease Out (Seconds)", easeOut.AsSingle));
            if (EditorGUI.EndChangeCheck())
                easeOut = OrchestrationTime.FromSeconds(easeOutSeconds);
            EditorGUI.BeginChangeCheck();
            double clipInSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Clip In (Seconds)", clip.ClipInTime.AsSingle));
            bool clipInChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.LabelField("Other Ease In", clip.OtherEaseInTime.ToString());
            EditorGUILayout.LabelField("Other Ease Out", clip.OtherEaseOutTime.ToString());
            if (EditorGUI.EndChangeCheck())
                ApplyTimeRange(asset, clip, startTime, endTime, easeIn, easeOut, clipInSeconds, clipInChanged);

            if (m_ClipRevision != asset.Data.AuthoringRevision)
                SynchronizeClipProperties(clip);
            for (int index = 0; index < m_ClipProperties.Length; index++)
            {
                PropertyBinding binding = m_ClipProperties[index];
                object before = binding.Value;
                object after = DrawProperty(binding, clip.Track.Timeline.ExternalBindings);
                if (ReferenceEquals(before, after))
                    continue;
                if (binding.Attribute.Trimmed && after is string text)
                    after = text.Trim();
                if (ValuesDiffer(before, after))
                {
                    m_ClipConfiguration.Set(binding.Attribute.PropertyId, after);
                    ApplyConfiguration(asset, clip, m_ClipConfiguration);
                    SynchronizeClipProperties(clip);
                    break;
                }
            }
        }

        void PrepareClipProperties(Clip clip)
        {
            var bindings = new List<PropertyBinding>();
            foreach (TimelineAuthoringPropertyAttribute attribute in TimelineAuthoringClipBinding.Properties(clip))
            {
                if (!attribute.Supports(clip))
                    continue;
                bindings.Add(new PropertyBinding
                {
                    Attribute = attribute,
                    ValueType = TimelineAuthoringClipBinding.ValueType(clip, attribute),
                    Label = ObjectNames.NicifyVariableName(attribute.PropertyId)
                });
            }
            m_ClipProperties = bindings.ToArray();
            SynchronizeClipProperties(clip);
        }

        void SynchronizeClipProperties(Clip clip)
        {
            m_ClipConfiguration = TimelineAuthoringClipBinding.Read(clip);
            for (int i = 0; i < m_ClipProperties.Length; i++)
            {
                PropertyBinding binding = m_ClipProperties[i];
                binding.Value = m_ClipConfiguration[binding.Attribute.PropertyId];
                if (binding.ValueType == typeof(AnimationCurve))
                    binding.Curve = binding.Value is AnimationCurve curve ? new AnimationCurve(curve.keys)
                    {
                        preWrapMode = curve.preWrapMode,
                        postWrapMode = curve.postWrapMode
                    } : new AnimationCurve();
            }
            m_ClipRevision = clip.Track.Timeline.AuthoringRevision;
        }

        void ApplyTreeExitSource(TimelineAsset asset, TreeClip clip, TimelineClipExitSource exitSource)
        {
            if (!TryBeginMutation(asset))
                return;
            try
            {
                asset.Data.ApplyModify(() => clip.SetExitSource(exitSource), "Edit TreeClip Exit Source");
                asset.Data.Init();
                m_SourceRevision = TimelineAuthoringFingerprint.Compute(asset.Data);
                m_ConfigurationError = null;
            }
            catch (Exception exception)
            {
                m_ConfigurationError = exception.Message;
            }
        }

        void DrawMarkerInspector(TimelineAsset asset, TimelineMarker marker)
        {
            if (!string.IsNullOrEmpty(m_ConfigurationError))
                EditorGUILayout.HelpBox(m_ConfigurationError, MessageType.Error);
            EditorGUILayout.LabelField("Domain (Inherited from Track)", marker.ExecutionDomain.ToString());
            EditorGUI.BeginChangeCheck();
            double seconds = Math.Max(0d, EditorGUILayout.DoubleField("Time (Seconds)", marker.Time.AsSingle));
            bool timeChanged = EditorGUI.EndChangeCheck();
            bool forceTriggerOnInitialSeek = marker.ForceTriggerOnInitialSeek;
            if (marker.ExecutionDomain == TimelineExecutionDomain.Presentation)
                forceTriggerOnInitialSeek = EditorGUILayout.Toggle("Force Trigger On Initial Seek", forceTriggerOnInitialSeek);
            ScriptableObject graph = (ScriptableObject)EditorGUILayout.ObjectField(
                "Trigger Graph",
                marker.Graph,
                typeof(ScriptableObject),
                false);
            if (!timeChanged && graph == marker.Graph && forceTriggerOnInitialSeek == marker.ForceTriggerOnInitialSeek)
                return;
            ApplyMarkerConfiguration(asset, marker, timeChanged ? OrchestrationTime.FromSeconds(seconds) : marker.Time,
                graph, forceTriggerOnInitialSeek);
        }

        void ApplyMarkerConfiguration(TimelineAsset asset, TimelineMarker marker, OrchestrationTime time,
            ScriptableObject graph, bool forceTriggerOnInitialSeek)
        {
            if (!TryBeginMutation(asset))
                return;
            try
            {
                asset.Data.ApplyModify(() => TimelineWorkspaceModeBridge.Authoring.MutateOwnedContent(
                    asset.Data, () => marker.Configure(time, graph, forceTriggerOnInitialSeek)), "Edit Timeline Marker");
                m_SourceRevision = TimelineAuthoringFingerprint.Compute(asset.Data);
                m_ConfigurationError = null;
            }
            catch (Exception exception)
            {
                m_ConfigurationError = exception.Message;
            }
        }

        void DrawTrackInspector(TimelineAsset asset, Track track)
        {
            if (!string.IsNullOrEmpty(m_ConfigurationError))
                EditorGUILayout.HelpBox(m_ConfigurationError, MessageType.Error);
            EditorGUILayout.LabelField("Contract", track.ContractKind);
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("Name", track.Name ?? string.Empty);
            bool muted = EditorGUILayout.Toggle("Muted", track.PersistentMuted);
            TimelineExecutionDomain executionDomain = (TimelineExecutionDomain)EditorGUILayout.EnumPopup(
                "Execution Domain", track.ExecutionDomain);
            if (!EditorGUI.EndChangeCheck())
                return;
            if (!TryBeginMutation(asset))
                return;
            try
            {
                asset.Data.ConfigureTrackAuthoring(track, name, muted, executionDomain,
                    m_Contracts);
                m_SourceRevision = TimelineAuthoringFingerprint.Compute(asset.Data);
                m_ConfigurationError = null;
            }
            catch (Exception exception)
            {
                m_ConfigurationError = exception.Message;
            }
        }

        void DrawSectionInspector(TimelineAsset asset, TimelineSection section)
        {
            string name = EditorGUILayout.TextField("Name", section.Name);
            EditorGUI.BeginChangeCheck();
            double seconds = Math.Max(0d, EditorGUILayout.DoubleField("Time (Seconds)", section.Time.AsSingle));
            bool timeChanged = EditorGUI.EndChangeCheck();
            if (name == section.Name && !timeChanged)
                return;
            ApplySectionConfiguration(asset, section, name, timeChanged ? OrchestrationTime.FromSeconds(seconds) : section.Time);
        }

        void ApplySectionConfiguration(TimelineAsset asset, TimelineSection section, string name, OrchestrationTime time)
        {
            if (!TryBeginMutation(asset))
                return;
            try
            {
                asset.Data.ApplyModify(() =>
                {
                    asset.Data.ConfigureSection(section, name, time);
                    asset.Data.Init();
                }, "Edit Timeline Section");
                m_SourceRevision = TimelineAuthoringFingerprint.Compute(asset.Data);
                m_ConfigurationError = null;
            }
            catch (Exception exception)
            {
                m_ConfigurationError = exception.Message;
            }
        }

        void ApplyTimeRange(TimelineAsset asset, Clip clip, OrchestrationTime startTime, OrchestrationTime endTime, OrchestrationTime easeIn, OrchestrationTime easeOut, double clipInSeconds, bool clipInChanged)
        {
            if (!TryBeginMutation(asset))
                return;
            try
            {
                asset.Data.ApplyModify(() =>
                {
                    clip.ConfigureTimeRange(startTime, endTime);
                    clip.ConfigureEase(easeIn, easeOut);
                    if (clipInChanged)
                        clip.ConfigureClipIn(OrchestrationTime.FromSeconds(clipInSeconds));
                    clip.Track.UpdateMix();
                    asset.Data.Init();
                }, "Edit Timeline Clip Time");
                m_SourceRevision = TimelineAuthoringFingerprint.Compute(asset.Data);
                m_ConfigurationError = null;
            }
            catch (Exception exception)
            {
                m_ConfigurationError = exception.Message;
            }
        }

        void ApplyConfiguration(TimelineAsset asset, Clip clip, TimelineAuthoringClipConfiguration configuration)
        {
            if (!TryBeginMutation(asset))
                return;
            try
            {
                asset.Data.ApplyModify(() =>
                {
                    TimelineAuthoringClipBinding.Configure(asset.Data, clip, configuration);
                    asset.Data.Init();
                }, "Edit Timeline Clip");
                m_SourceRevision = TimelineAuthoringFingerprint.Compute(asset.Data);
                m_ConfigurationError = null;
            }
            catch (Exception exception)
            {
                m_ConfigurationError = exception.Message;
            }
        }

        bool TryBeginMutation(TimelineAsset asset)
        {
            string currentRevision = TimelineAuthoringFingerprint.Compute(asset.Data);
            if (string.IsNullOrEmpty(m_SourceRevision) ||
                string.Equals(m_SourceRevision, currentRevision, StringComparison.Ordinal))
                return true;
            m_ConfigurationError = "Timeline 内容已被外部修改，当前 Inspector 修改未提交。";
            Rebuild();
            return false;
        }

        static object DrawProperty(PropertyBinding binding,
            IReadOnlyList<TimelineExternalBindingDeclaration> externalBindings) =>
            TimelineAuthoringPropertyGUI.Draw(binding.Attribute, binding.Label, binding.Value,
                binding.ValueType, externalBindings, binding.Curve);

        static bool ValuesDiffer(object left, object right)
        {
            if (left is AnimationCurve leftCurve && right is AnimationCurve rightCurve)
                return !TimelineCurveAuthoring.AreEquivalent(leftCurve, rightCurve);
            return !Equals(left, right);
        }

    }
}
