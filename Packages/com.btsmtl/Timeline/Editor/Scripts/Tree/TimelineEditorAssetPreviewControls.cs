using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    public sealed partial class TimelineEditorWindow
    {
        [SerializeField] TimelinePreviewMode m_PreviewMode;
        Label m_AssetPreviewStatus;
        ObjectField m_AssetPreviewConfiguration;
        IMGUIContainer m_AssetPreviewInputs;
        bool m_ShowPreviewInputs;
        Vector2 m_PreviewInputsScroll;
        BtsmtlTimeBarView m_AssetChainView;
        BtsmtlAssetPreviewTimeBarDataSource m_AssetChainSource;
        IMGUIContainer m_AssetChainPanel;
        long m_AssetChainRevision = -1;
        ulong m_AppliedAssetSample;

        VisualElement CreateAssetPreviewControls()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            var panel = new VisualElement();
            var toolbar = new Toolbar();
            toolbar.style.flexWrap = Wrap.Wrap;
            toolbar.style.height = StyleKeyword.Auto;
            toolbar.style.minHeight = 22f;
            var mode = new PopupField<string>(new List<string> { "资产预览", "会话预览" }, (int)m_PreviewMode);
            mode.RegisterValueChangedCallback(change =>
            {
                m_PreviewMode = (TimelinePreviewMode)mode.index;
                if (SourceAsset)
                    OpenTimelinePreview();
            });
            mode.tooltip = "资产预览按目标时刻显示；会话预览确认真实运行结果。";
            toolbar.Add(mode);
            m_AssetPreviewConfiguration = new ObjectField("资产预览配置")
            {
                objectType = preview.ConfigurationType,
                allowSceneObjects = false,
                value = preview.Configuration
            };
            m_AssetPreviewConfiguration.RegisterValueChangedCallback(change => preview.Configuration = change.newValue);
            m_AssetPreviewConfiguration.style.minWidth = 260f;
            m_AssetPreviewConfiguration.style.flexGrow = 1f;
            toolbar.Add(m_AssetPreviewConfiguration);
            toolbar.Add(new ToolbarButton(OpenTimelinePreview) { text = "打开预览" });
            toolbar.Add(new ToolbarButton(() => preview.SetPlaying(!preview.IsPlaying)) { text = "播放／暂停" });
            toolbar.Add(new ToolbarButton(preview.Close) { text = "关闭资产预览" });
            toolbar.Add(new ToolbarButton(() =>
            {
                m_PreviewMode = TimelinePreviewMode.Session;
                mode.SetValueWithoutNotify("会话预览");
                OpenTimelinePreview();
            }) { text = "在会话中验证" });
            toolbar.Add(new ToolbarButton(() =>
            {
                m_ShowPreviewInputs = !m_ShowPreviewInputs;
                RefreshAssetPreviewControls();
            }) { text = "输入日程与假设" });
            toolbar.SetEnabled(SourceAsset);
            panel.Add(toolbar);
            m_AssetPreviewStatus = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            panel.Add(m_AssetPreviewStatus);
            m_AssetPreviewInputs = new IMGUIContainer(DrawAssetPreviewInputs);
            m_AssetPreviewInputs.style.maxHeight = 240f;
            panel.Add(m_AssetPreviewInputs);
            m_AssetChainSource = new BtsmtlAssetPreviewTimeBarDataSource(preview);
            m_AssetChainView = new BtsmtlTimeBarView();
            m_AssetChainPanel = new IMGUIContainer(() => m_AssetChainView.Draw(m_AssetChainPanel.contentRect));
            m_AssetChainPanel.style.height = 260f;
            panel.Add(m_AssetChainPanel);
            preview.Changed += RefreshAssetPreviewControls;
            RefreshAssetPreviewControls();
            return panel;
        }

        void OpenTimelinePreview()
        {
            var context = m_DoorContexts.Count == 0 ? default : m_DoorContexts[m_DoorContextIndex];
            TimelineAssetPreviewHost.Shared.Open(SourceAsset, m_PreviewMode, context.Query, context.State);
        }

        void DisposeAssetPreviewBindings()
        {
            TimelineAssetPreviewHost.Shared.Changed -= RefreshAssetPreviewControls;
            m_AssetPreviewStatus = null;
            m_AssetPreviewConfiguration = null;
            m_AssetPreviewInputs = null;
            m_AssetChainView?.Dispose();
            m_AssetChainView = null;
            m_AssetChainSource = null;
            m_AssetChainPanel = null;
            m_AssetChainRevision = -1;
            m_AppliedAssetSample = 0;
        }

        void RefreshAssetPreviewControls()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            bool active = preview.IsOpen && preview.Source == SourceAsset;
            if (m_AssetPreviewConfiguration != null)
            {
                m_AssetPreviewConfiguration.objectType = preview.ConfigurationType;
                m_AssetPreviewConfiguration.SetValueWithoutNotify(preview.Configuration);
                m_AssetPreviewConfiguration.SetEnabled(preview.IsRegistered);
            }
            if (m_AssetPreviewStatus != null)
                m_AssetPreviewStatus.text = !preview.IsRegistered ? TimelineAssetPreviewHost.MissingProviderReason :
                    preview.Error.Length != 0 ? preview.Error : active
                    ? $"资产预览 · 链 {preview.Time:F3} 秒 / {preview.SampledSource?.name} {preview.LocalTime:F3} 秒 · 未经物理。{preview.Status}\n{preview.Chain?.Reason}"
                    : "选择资产预览调整节奏；会话预览确认碰撞、条件与完整运行结果。";
            if (m_AssetPreviewInputs != null)
            {
                m_AssetPreviewInputs.style.display = active && m_ShowPreviewInputs ? DisplayStyle.Flex : DisplayStyle.None;
                m_AssetPreviewInputs.MarkDirtyRepaint();
            }
            if (m_AssetChainPanel != null)
            {
                m_AssetChainPanel.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                m_AssetChainView.SetSource(active ? m_AssetChainSource : null);
                if (active && m_AssetChainRevision != preview.Revision)
                {
                    m_AssetChainRevision = preview.Revision;
                    m_AssetChainSource.Rebuild();
                }
                m_AssetChainPanel.MarkDirtyRepaint();
            }
            if (active && m_AppliedAssetSample != preview.SampledRevision)
            {
                m_AppliedAssetSample = preview.SampledRevision;
                if (preview.SampledSource == SourceAsset && m_SlateProjection != null &&
                    m_SlateProjection.CurrentTime != preview.LocalTime)
                    m_SlateProjection.ApplyAuthoringPreviewTime(preview.LocalTime);
            }
            m_SlateSurface?.MarkDirtyRepaint();
        }

        void DrawAssetPreviewInputs()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (!preview.IsOpen || preview.Source != SourceAsset)
                return;
            TimelineAssetPreviewPreferences preferences = preview.Preferences;
            m_PreviewInputsScroll = EditorGUILayout.BeginScrollView(m_PreviewInputsScroll);
            EditorGUI.BeginChangeCheck();
            preferences.RangeEnd = EditorGUILayout.FloatField("预览范围终点（秒）", preferences.RangeEnd);
            if (preferences.RangeEnd <= 0f)
                EditorGUILayout.HelpBox("预览范围终点必须大于零。", MessageType.Error);
            var sections = new List<string> { "从 Timeline 起点" };
            foreach (var section in SourceAsset.Data.Sections)
                sections.Add(section.Name);
            int sectionIndex = 0;
            for (int i = 0; i < SourceAsset.Data.Sections.Count; i++)
                if (SourceAsset.Data.Sections[i].AuthoringId == preferences.StartingSectionId)
                    sectionIndex = i + 1;
            sectionIndex = EditorGUILayout.Popup("起始分段", sectionIndex, sections.ToArray());
            preferences.StartingSectionId = sectionIndex == 0 ? string.Empty : SourceAsset.Data.Sections[sectionIndex - 1].AuthoringId;
            preferences.HasTarget = EditorGUILayout.Toggle("提供假设目标", preferences.HasTarget);
            if (preferences.HasTarget)
            {
                preferences.TargetPosition = EditorGUILayout.Vector3Field("假设目标位置", preferences.TargetPosition);
                preferences.TargetEuler = EditorGUILayout.Vector3Field("假设目标朝向", preferences.TargetEuler);
            }
            IReadOnlyList<GraphHostInputDescriptor> inputs = preview.Instance.Inputs;
            IReadOnlyList<TimelinePreviewInputRequest> requests = preview.InputRequests;
            if (requests.Count != 0)
                EditorGUILayout.HelpBox("请求按正式缓冲时长显示脉冲；静态推导不执行请求消费，实际接纳需在会话中验证。", MessageType.Info);
            foreach (GraphHostInputDescriptor descriptor in inputs)
            {
                TimelinePreviewInputRequest? request = null;
                foreach (TimelinePreviewInputRequest value in requests)
                    if (value.InputId == descriptor.InputId)
                        request = value;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField((request.HasValue ? "请求 · " : string.Empty) + descriptor.InputId);
                    if (GUILayout.Button("在游标处添加输入", GUILayout.Width(140f)))
                    {
                        preferences.Inputs.Add(new TimelinePreviewInputPoint
                            { InputId = descriptor.InputId, Time = preview.Time, Boolean = true });
                        GUI.changed = true;
                    }
                }
                for (int i = 0; i < preferences.Inputs.Count; i++)
                {
                    TimelinePreviewInputPoint point = preferences.Inputs[i];
                    if (point.InputId != descriptor.InputId)
                        continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        point.Time = EditorGUILayout.Slider(point.Time, 0f, preferences.RangeEnd, GUILayout.Width(220f));
                        point.Active = EditorGUILayout.ToggleLeft("启用", point.Active, GUILayout.Width(56f));
                        DrawInputValue(descriptor, point);
                        if (request.HasValue && point.Boolean)
                            EditorGUILayout.LabelField($"到期 {point.Time + request.Value.DurationSeconds:0.###} 秒", GUILayout.Width(110f));
                        if (GUILayout.Button("删除", GUILayout.Width(44f)))
                        {
                            preferences.Inputs.RemoveAt(i--);
                            GUI.changed = true;
                        }
                    }
                }
            }
            for (int i = 0; i < preferences.Inputs.Count; i++)
            {
                TimelinePreviewInputPoint point = preferences.Inputs[i];
                if (inputs.Any(descriptor => descriptor.InputId == point.InputId))
                    continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"未声明的输入：{point.InputId}");
                    if (GUILayout.Button("删除日程项", GUILayout.Width(100f)))
                    {
                        preferences.Inputs.RemoveAt(i--);
                        GUI.changed = true;
                    }
                }
            }
            EditorGUILayout.LabelField("运行数据假设", EditorStyles.boldLabel);
            foreach (OrchestrationStaticRead read in preview.Assumptions)
            {
                string identity = TimelineAssetPreviewReadIntervals.ConditionIdentity(in read);
                TimelinePreviewConditionAssumption assumption = preferences.Conditions.Find(value => value.Identity == identity);
                bool satisfied = assumption == null || assumption.Satisfied;
                bool selected = EditorGUILayout.ToggleLeft(read.Label + (read.Undeterminable ? " · 无法静态判断" : string.Empty), satisfied);
                if (selected != satisfied)
                {
                    if (assumption == null)
                    {
                        assumption = new TimelinePreviewConditionAssumption { Identity = identity };
                        preferences.Conditions.Add(assumption);
                    }
                    assumption.Satisfied = selected;
                }
            }
            bool changed = EditorGUI.EndChangeCheck();
            if (GUILayout.Button("从录制输入导入"))
            {
                string path = EditorUtility.OpenFilePanel("选择录制输入", Application.dataPath, string.Empty);
                if (path.Length != 0)
                    preview.ImportInputs(path);
            }
            EditorGUILayout.EndScrollView();
            if (changed)
                preview.PreferencesChanged();
        }

        static void DrawInputValue(GraphHostInputDescriptor descriptor, TimelinePreviewInputPoint point)
        {
            switch (descriptor.ValueKind)
            {
                case GraphHostValueKind.Bool: point.Boolean = EditorGUILayout.Toggle(point.Boolean); break;
                case GraphHostValueKind.Int32: point.Integer = EditorGUILayout.IntField(point.Integer); break;
                case GraphHostValueKind.Float32: point.Scalar = EditorGUILayout.FloatField(point.Scalar); break;
                case GraphHostValueKind.Vector2: point.Vector2 = EditorGUILayout.Vector2Field(string.Empty, point.Vector2); break;
                case GraphHostValueKind.Vector3: point.Vector3 = EditorGUILayout.Vector3Field(string.Empty, point.Vector3); break;
                case GraphHostValueKind.Quaternion:
                    point.Rotation = Quaternion.Euler(EditorGUILayout.Vector3Field(string.Empty, point.Rotation.eulerAngles));
                    break;
                case GraphHostValueKind.Enum:
                    point.Integer = Convert.ToInt32(EditorGUILayout.EnumPopup((Enum)Enum.ToObject(descriptor.ValueType, point.Integer)));
                    break;
                case GraphHostValueKind.Text: point.Text = EditorGUILayout.TextField(point.Text); break;
            }
        }
    }
}
