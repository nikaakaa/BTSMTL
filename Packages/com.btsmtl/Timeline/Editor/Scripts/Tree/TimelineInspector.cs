using System;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using System.Linq;
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

        void OnEnable()
        {
            TimelineInspectorSelection.Changed += Rebuild;
        }

        void OnDisable()
        {
            TimelineInspectorSelection.Changed -= Rebuild;
        }

        public override VisualElement CreateInspectorGUI()
        {
            m_Root = new VisualElement();
            Rebuild();
            return m_Root;
        }

        void Rebuild()
        {
            if (m_Root == null)
                return;
            m_Root.Clear();
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
                m_Root.Add(new Label("Selected Timeline Element"));
                m_Root.Add(new IMGUIContainer(() => DrawSelectedInspector(asset, selected)));
            }

            var errors = new List<string>();
            if (!asset.ValidateContent(TimelineTreeContractComposition.Create(), errors))
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
            if (asset.Data.IsTimeRangeWorkspace)
            {
                EditorGUILayout.LabelField("片段", clip.Name);
                EditorGUI.BeginChangeCheck();
                double start = EditorGUILayout.DelayedDoubleField("起点裁剪（秒）", clip.StartTime.ToDouble());
                double end = EditorGUILayout.DelayedDoubleField("终点裁剪（秒）", clip.EndTime.ToDouble());
                if (EditorGUI.EndChangeCheck())
                    TimelineEditorWindow.FindOpen(asset).ApplyClipTimeRangeNumbers(clip.AuthoringId,
                        FixedScalar.FromDouble(start), FixedScalar.FromDouble(end));
                EditorGUILayout.LabelField("ClipIn", clip.ClipInTime.ToString());
                EditorGUILayout.LabelField("混合进入", clip.SelfEaseInTime.ToString());
                EditorGUILayout.LabelField("混合退出", clip.SelfEaseOutTime.ToString());
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
                if (nextExitSource != exitSource && TryBeginMutation(asset))
                {
                    try
                    {
                        asset.Data.ApplyModify(() => selectedTree.SetExitSource(nextExitSource), "Edit TreeClip Exit Source");
                        asset.Data.Init();
                        m_SourceRevision = TimelineAuthoringFingerprint.Compute(asset.Data);
                        m_ConfigurationError = null;
                    }
                    catch (Exception exception)
                    {
                        m_ConfigurationError = exception.Message;
                    }
                }
            }
            FixedScalar startTime = clip.StartTime;
            FixedScalar endTime = clip.EndTime;
            FixedScalar easeIn = clip.SelfEaseInTime;
            FixedScalar easeOut = clip.SelfEaseOutTime;
            bool isDynamicTreeClip = clip is TreeClip treeClip &&
                treeClip.ClipExitSource == TimelineClipExitSource.TreeDecision;
            EditorGUI.BeginChangeCheck();
            EditorGUI.BeginChangeCheck();
            double startSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Start (Seconds)", startTime.ToDouble()));
            if (EditorGUI.EndChangeCheck())
                startTime = FixedScalar.FromDouble(startSeconds);
            bool dynamicLogicTreeClip = isDynamicTreeClip && clip.ExecutionDomain == TimelineExecutionDomain.Logic;
            if (dynamicLogicTreeClip)
                EditorGUILayout.LabelField("End (Seconds)", $"Timeline End ({endTime})");
            else
            {
                EditorGUI.BeginChangeCheck();
                string endLabel = isDynamicTreeClip ? "Layout End (Seconds)" : "End (Seconds)";
                double endSeconds = EditorGUILayout.DoubleField(endLabel, endTime.ToDouble());
                if (EditorGUI.EndChangeCheck())
                    endTime = FixedScalar.FromDouble(endSeconds);
            }
            EditorGUI.BeginChangeCheck();
            double easeInSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Self Ease In (Seconds)", easeIn.ToDouble()));
            if (EditorGUI.EndChangeCheck())
                easeIn = FixedScalar.FromDouble(easeInSeconds);
            EditorGUI.BeginChangeCheck();
            double easeOutSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Self Ease Out (Seconds)", easeOut.ToDouble()));
            if (EditorGUI.EndChangeCheck())
                easeOut = FixedScalar.FromDouble(easeOutSeconds);
            EditorGUI.BeginChangeCheck();
            double clipInSeconds = Math.Max(0d, EditorGUILayout.DoubleField("Clip In (Seconds)", clip.ClipInTime.ToDouble()));
            bool clipInChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.LabelField("Other Ease In", clip.OtherEaseInTime.ToString());
            EditorGUILayout.LabelField("Other Ease Out", clip.OtherEaseOutTime.ToString());
            if (EditorGUI.EndChangeCheck())
                ApplyTimeRange(asset, clip, startTime, endTime, easeIn, easeOut, clipInSeconds, clipInChanged);

            TimelineAuthoringClipConfiguration configuration;
            try
            {
                configuration = TimelineAuthoringClipBinding.Read(clip);
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
                return;
            }

            PropertyInfo[] properties = typeof(TimelineAuthoringClipConfiguration).GetProperties(BindingFlags.Instance | BindingFlags.Public);
            object[] attributes = clip.GetType().GetCustomAttributes(typeof(TimelineAuthoringPropertyAttribute), true);
            for (int index = 0; index < attributes.Length; index++)
            {
                TimelineAuthoringPropertyAttribute attribute = (TimelineAuthoringPropertyAttribute)attributes[index];
                PropertyInfo property = FindProperty(properties, attribute.PropertyId);
                if (property == null || !property.CanRead || !property.CanWrite)
                    continue;
                object before = property.GetValue(configuration, null);
                EditorGUI.BeginChangeCheck();
                object after = DrawProperty(attribute, property, before);
                if (!EditorGUI.EndChangeCheck())
                    continue;
                if (attribute.Trimmed && after is string text)
                    after = text.Trim();
                if (ValuesDiffer(before, after))
                {
                    property.SetValue(configuration, after, null);
                    ApplyConfiguration(asset, clip, configuration);
                    break;
                }
            }
        }

        void DrawMarkerInspector(TimelineAsset asset, TimelineMarker marker)
        {
            if (!string.IsNullOrEmpty(m_ConfigurationError))
                EditorGUILayout.HelpBox(m_ConfigurationError, MessageType.Error);
            EditorGUILayout.LabelField("Domain (Inherited from Track)", marker.ExecutionDomain.ToString());
            EditorGUI.BeginChangeCheck();
            double seconds = Math.Max(0d, EditorGUILayout.DoubleField("Time (Seconds)", marker.Time.ToDouble()));
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
            if (!TryBeginMutation(asset))
                return;
            try
            {
                asset.Data.ApplyModify(() => TimelineWorkspaceModeBridge.Authoring.MutateOwnedContent(
                    asset.Data, () => marker.Configure(timeChanged ? FixedScalar.FromDouble(seconds) : marker.Time, graph, forceTriggerOnInitialSeek)), "Edit Timeline Marker");
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
                    TimelineTreeContractComposition.Create());
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
            double seconds = Math.Max(0d, EditorGUILayout.DoubleField("Time (Seconds)", section.Time.ToDouble()));
            bool timeChanged = EditorGUI.EndChangeCheck();
            if (name == section.Name && !timeChanged)
                return;
            if (!TryBeginMutation(asset))
                return;
            try
            {
                asset.Data.ApplyModify(() =>
                {
                    asset.Data.ConfigureSection(section, name, timeChanged ? FixedScalar.FromDouble(seconds) : section.Time);
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

        void ApplyTimeRange(TimelineAsset asset, Clip clip, FixedScalar startTime, FixedScalar endTime, FixedScalar easeIn, FixedScalar easeOut, double clipInSeconds, bool clipInChanged)
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
                        clip.ConfigureClipIn(FixedScalar.FromDouble(clipInSeconds));
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

        static object DrawProperty(TimelineAuthoringPropertyAttribute attribute, PropertyInfo property, object value)
        {
            string label = ObjectNames.NicifyVariableName(attribute.PropertyId);
            switch (attribute.Kind)
            {
                case TimelineAuthoringPropertyKind.Text:
                    return EditorGUILayout.TextField(label, value as string ?? string.Empty);
                case TimelineAuthoringPropertyKind.Boolean:
                    return EditorGUILayout.Toggle(label, value is bool boolean && boolean);
                case TimelineAuthoringPropertyKind.Integer:
                    return EditorGUILayout.IntField(label, value is int integer ? integer : 0);
                case TimelineAuthoringPropertyKind.Float:
                    float floatValue = value is float number ? number : 0f;
                    floatValue = EditorGUILayout.FloatField(label, floatValue);
                    if (attribute.HasMinimum)
                        floatValue = Mathf.Max((float)attribute.Minimum, floatValue);
                    if (attribute.HasMaximum)
                        floatValue = Mathf.Min((float)attribute.Maximum, floatValue);
                    return floatValue;
                case TimelineAuthoringPropertyKind.Enum:
                    Enum enumValue = value as Enum;
                    if (enumValue == null && attribute.EnumType != null)
                        enumValue = (Enum)Enum.ToObject(attribute.EnumType, 0);
                    return enumValue == null ? value : EditorGUILayout.EnumPopup(label, enumValue);
                case TimelineAuthoringPropertyKind.Vector2:
                    return EditorGUILayout.Vector2Field(label, value is Vector2 vector ? vector : Vector2.zero);
                case TimelineAuthoringPropertyKind.Object:
                    if (property.PropertyType == typeof(AnimationCurve))
                    {
                        AnimationCurve curve = value as AnimationCurve;
                        AnimationCurve copy = curve == null ? new AnimationCurve() : new AnimationCurve(curve.keys)
                        {
                            preWrapMode = curve.preWrapMode,
                            postWrapMode = curve.postWrapMode
                        };
                        return EditorGUILayout.CurveField(label, copy);
                    }
                    return EditorGUILayout.ObjectField(label, value as UnityEngine.Object, property.PropertyType, false);
                default:
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
