using ThirdPersonSimulation.QuantumNumerics;
using System;
using Photon.Deterministic;
using System.Collections.Generic;
using System.Reflection;
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
            internal PropertyInfo Property;
            internal string Label;
            internal object Value;
            internal AnimationCurve Curve;
        }

        void OnEnable()
        {
            m_Contracts = TimelineTreeContractComposition.Create();
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
            m_Root.Add(new Button(() =>
            {
                if (asset.Data.IsTimeRangeWorkspace)
                    TimelineEditorWindow.FindOpen(asset).Focus();
                else
                    TimelineEditorWindow.Open(asset);
            })
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
                if (selected is Clip clip && !asset.Data.IsTimeRangeWorkspace)
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
                TimelineEditorWindow.FindOpen(asset)?.RuntimeObservationReadOnly == true ||
                asset.Data.IsTimeRangeWorkspace && selected is not Clip);
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
            BtsmtlSlateTimelineProjection.DrawCameraResourceInspector(asset.Data, clip);
            if (asset.Data.IsTimeRangeWorkspace)
            {
                EditorGUILayout.LabelField("片段", clip.Name);
                EditorGUI.BeginChangeCheck();
                double start = EditorGUILayout.DelayedDoubleField("起点裁剪（秒）", clip.StartTime.AsDouble);
                double end = EditorGUILayout.DelayedDoubleField("终点裁剪（秒）", clip.EndTime.AsDouble);
                if (EditorGUI.EndChangeCheck())
                    TimelineEditorWindow.FindOpen(asset).ApplyClipTimeRangeNumbers(clip.AuthoringId,
                        QuantumScalarBoundary.ConvertExternal(start, nameof(TimelineInspector)), QuantumScalarBoundary.ConvertExternal(end, nameof(TimelineInspector)));
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.DoubleField("ClipIn", clip.ClipInTime.AsDouble);
                    EditorGUILayout.DoubleField("混合进入", clip.SelfEaseInTime.AsDouble);
                    EditorGUILayout.DoubleField("混合退出", clip.SelfEaseOutTime.AsDouble);
                }
                return;
            }
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
            FP startTime = clip.StartTime;
            FP endTime = clip.EndTime;
            FP easeIn = clip.SelfEaseInTime;
            FP easeOut = clip.SelfEaseOutTime;
            bool isDynamicTreeClip = clip is TreeClip treeClip &&
                treeClip.ClipExitSource == TimelineClipExitSource.TreeDecision;
            EditorGUI.BeginChangeCheck();
            EditorGUI.BeginChangeCheck();
            double startSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Start (Seconds)", startTime.AsDouble));
            if (EditorGUI.EndChangeCheck())
                startTime = QuantumScalarBoundary.ConvertExternal(startSeconds, nameof(TimelineInspector));
            bool dynamicLogicTreeClip = isDynamicTreeClip && clip.ExecutionDomain == TimelineExecutionDomain.Logic;
            if (dynamicLogicTreeClip)
                EditorGUILayout.LabelField("End (Seconds)", $"Timeline End ({endTime})");
            else
            {
                EditorGUI.BeginChangeCheck();
                string endLabel = isDynamicTreeClip ? "Layout End (Seconds)" : "End (Seconds)";
                double endSeconds = EditorGUILayout.DoubleField(endLabel, endTime.AsDouble);
                if (EditorGUI.EndChangeCheck())
                    endTime = QuantumScalarBoundary.ConvertExternal(endSeconds, nameof(TimelineInspector));
            }
            EditorGUI.BeginChangeCheck();
            double easeInSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Self Ease In (Seconds)", easeIn.AsDouble));
            if (EditorGUI.EndChangeCheck())
                easeIn = QuantumScalarBoundary.ConvertExternal(easeInSeconds, nameof(TimelineInspector));
            EditorGUI.BeginChangeCheck();
            double easeOutSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Self Ease Out (Seconds)", easeOut.AsDouble));
            if (EditorGUI.EndChangeCheck())
                easeOut = QuantumScalarBoundary.ConvertExternal(easeOutSeconds, nameof(TimelineInspector));
            EditorGUI.BeginChangeCheck();
            double clipInSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Clip In (Seconds)", clip.ClipInTime.AsDouble));
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
                object after = DrawProperty(binding);
                if (ReferenceEquals(before, after))
                    continue;
                if (binding.Attribute.Trimmed && after is string text)
                    after = text.Trim();
                if (ValuesDiffer(before, after))
                {
                    binding.Property.SetValue(m_ClipConfiguration, after, null);
                    ApplyConfiguration(asset, clip, m_ClipConfiguration);
                    SynchronizeClipProperties(clip);
                    break;
                }
            }
        }

        void PrepareClipProperties(Clip clip)
        {
            PropertyInfo[] properties = typeof(TimelineAuthoringClipConfiguration).GetProperties(BindingFlags.Instance | BindingFlags.Public);
            object[] attributes = clip.GetType().GetCustomAttributes(typeof(TimelineAuthoringPropertyAttribute), true);
            var bindings = new List<PropertyBinding>(attributes.Length);
            for (int i = 0; i < attributes.Length; i++)
            {
                var attribute = (TimelineAuthoringPropertyAttribute)attributes[i];
                if (!attribute.Supports(clip))
                    continue;
                PropertyInfo property = FindProperty(properties, attribute.PropertyId);
                if (property == null || !property.CanRead || !property.CanWrite)
                    continue;
                bindings.Add(new PropertyBinding
                {
                    Attribute = attribute,
                    Property = property,
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
                binding.Value = binding.Property.GetValue(m_ClipConfiguration, null);
                if (binding.Property.PropertyType == typeof(AnimationCurve))
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
            double seconds = Math.Max(0d, EditorGUILayout.DoubleField("Time (Seconds)", marker.Time.AsDouble));
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
            ApplyMarkerConfiguration(asset, marker, timeChanged ? QuantumScalarBoundary.ConvertExternal(seconds, nameof(TimelineInspector)) : marker.Time,
                graph, forceTriggerOnInitialSeek);
        }

        void ApplyMarkerConfiguration(TimelineAsset asset, TimelineMarker marker, FP time,
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
            double seconds = Math.Max(0d, EditorGUILayout.DoubleField("Time (Seconds)", section.Time.AsDouble));
            bool timeChanged = EditorGUI.EndChangeCheck();
            if (name == section.Name && !timeChanged)
                return;
            ApplySectionConfiguration(asset, section, name, timeChanged ? QuantumScalarBoundary.ConvertExternal(seconds, nameof(TimelineInspector)) : section.Time);
        }

        void ApplySectionConfiguration(TimelineAsset asset, TimelineSection section, string name, FP time)
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

        void ApplyTimeRange(TimelineAsset asset, Clip clip, FP startTime, FP endTime, FP easeIn, FP easeOut, double clipInSeconds, bool clipInChanged)
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
                        clip.ConfigureClipIn(QuantumScalarBoundary.ConvertExternal(clipInSeconds, nameof(TimelineInspector)));
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

        static PropertyInfo FindProperty(PropertyInfo[] properties, string propertyId)
        {
            if (string.IsNullOrEmpty(propertyId))
                return null;
            string name = char.ToUpperInvariant(propertyId[0]) + propertyId.Substring(1);
            for (int index = 0; index < properties.Length; index++)
                if (string.Equals(properties[index].Name, name, StringComparison.Ordinal))
                    return properties[index];
            return null;
        }

        static object DrawProperty(PropertyBinding binding)
        {
            TimelineAuthoringPropertyAttribute attribute = binding.Attribute;
            string label = binding.Label;
            object value = binding.Value;
            EditorGUI.BeginChangeCheck();
            switch (attribute.Kind)
            {
                case TimelineAuthoringPropertyKind.Text:
                    string text = EditorGUILayout.TextField(label, value as string ?? string.Empty);
                    return EditorGUI.EndChangeCheck() ? text : value;
                case TimelineAuthoringPropertyKind.Boolean:
                    bool boolean = EditorGUILayout.Toggle(label, (bool)value);
                    return EditorGUI.EndChangeCheck() ? boolean : value;
                case TimelineAuthoringPropertyKind.Integer:
                    int integer = EditorGUILayout.IntField(label, (int)value);
                    return EditorGUI.EndChangeCheck() ? integer : value;
                case TimelineAuthoringPropertyKind.Float:
                    float floatValue = EditorGUILayout.FloatField(label, (float)value);
                    if (attribute.HasMinimum)
                        floatValue = Mathf.Max((float)attribute.Minimum, floatValue);
                    if (attribute.HasMaximum)
                        floatValue = Mathf.Min((float)attribute.Maximum, floatValue);
                    return EditorGUI.EndChangeCheck() ? floatValue : value;
                case TimelineAuthoringPropertyKind.Enum:
                    Enum enumValue = EditorGUILayout.EnumPopup(label, (Enum)value);
                    return EditorGUI.EndChangeCheck() ? enumValue : value;
                case TimelineAuthoringPropertyKind.Vector2:
                    Vector2 vector = EditorGUILayout.Vector2Field(label, (Vector2)value);
                    return EditorGUI.EndChangeCheck() ? vector : value;
                case TimelineAuthoringPropertyKind.Object:
                    if (binding.Property.PropertyType == typeof(AnimationCurve))
                    {
                        AnimationCurve curve = EditorGUILayout.CurveField(label, binding.Curve);
                        return EditorGUI.EndChangeCheck() ? curve : value;
                    }
                    UnityEngine.Object asset = EditorGUILayout.ObjectField(label, value as UnityEngine.Object, binding.Property.PropertyType, false);
                    return EditorGUI.EndChangeCheck() ? asset : value;
                default:
                    EditorGUI.EndChangeCheck();
                    return value;
            }
        }

        static bool ValuesDiffer(object left, object right)
        {
            if (left is AnimationCurve leftCurve && right is AnimationCurve rightCurve)
                return !TimelineCurveAuthoring.AreEquivalent(leftCurve, rightCurve);
            return !Equals(left, right);
        }

    }
}
