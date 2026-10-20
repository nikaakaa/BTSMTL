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
        ulong m_AppliedAssetSample;
        long m_PreviewPathRevision = -1;
        TimelineAsset m_ObservedPreviewSource;
        readonly List<int> m_PreviewInputs = new();

        VisualElement CreateAssetPreviewControls()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            var panel = new VisualElement();
            var toolbar = new Toolbar();
            toolbar.style.flexWrap = Wrap.Wrap;
            toolbar.style.height = StyleKeyword.Auto;
            toolbar.style.minHeight = 22f;
            toolbar.style.alignItems = Align.Center;
            var mode = new PopupField<string>(new List<string> { "资产预览", "会话预览" }, (int)m_PreviewMode);
            mode.RegisterValueChangedCallback(change =>
            {
                m_PreviewMode = (TimelinePreviewMode)mode.index;
                if (SourceAsset)
                    OpenTimelinePreview();
            });
            mode.tooltip = "资产预览按目标时刻显示；会话预览确认真实运行结果。";
            mode.style.width = 90f;
            mode.style.height = 20f;
            mode.style.alignSelf = Align.Center;
            toolbar.Add(mode);
            toolbar.Add(CreateDoorContextControl());
            m_AssetPreviewConfiguration = new ObjectField("资产预览配置")
            {
                objectType = preview.ConfigurationType,
                allowSceneObjects = false,
                value = preview.Configuration
            };
            m_AssetPreviewConfiguration.RegisterValueChangedCallback(change => preview.Configuration = change.newValue);
            m_AssetPreviewConfiguration.labelElement.style.minWidth = 0f;
            m_AssetPreviewConfiguration.labelElement.style.width = StyleKeyword.Auto;
            m_AssetPreviewConfiguration.style.width = 300f;
            m_AssetPreviewConfiguration.style.height = 20f;
            m_AssetPreviewConfiguration.style.flexGrow = 0f;
            m_AssetPreviewConfiguration.style.flexShrink = 1f;
            m_AssetPreviewConfiguration.style.alignSelf = Align.Center;
            toolbar.Add(m_AssetPreviewConfiguration);
            toolbar.Add(new ToolbarButton(OpenTimelinePreview) { text = "打开预览" });
            toolbar.Add(new ToolbarButton(() => preview.SetPlaying(!preview.IsPlaying)) { text = "播放／暂停" });
            toolbar.Add(new ToolbarButton(() => PreviewThrough(null))
            {
                text = "每段都接",
                tooltip = "在每段最早的出口窗口排入按键输入，从头预览一路接到最后一段；右键出口窗口的“去向”可预览单个接法。"
            });
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
            m_AppliedAssetSample = 0;
            m_PreviewPathRevision = -1;
            m_PreviewInputs.Clear();
        }

        // 预览链走进别的 Timeline 时编辑窗口跟过去，并把来处记为可返回的导航。
        void FollowPreview(TimelineAsset target)
        {
            if (!target || target == SourceAsset || !TimelineAssetPreviewHost.Shared.Covers(SourceAsset))
                return;
            if (target == m_NavigationOwner)
            {
                ReturnToTimeline();
                return;
            }
            if (!HasTimelineNavigation)
            {
                m_NavigationOwner = m_SerializedOwner;
                m_NavigationPropertyPath = m_SerializedPropertyPath;
                m_NavigationOwnershipLabel = m_OwnershipLabel;
                m_NavigationTrackAuthoringId = string.Empty;
                m_NavigationClipAuthoringId = string.Empty;
                m_NavigationSourceNodeGuid = m_SourceNodeGuid;
            }
            BindAsset(target, m_SourceGraphAuthoringId, m_SourceNodeGuid);
            if (m_Toolbar != null)
            {
                m_Toolbar.BackButton.text = "‹ 返回 " + m_NavigationOwner.name;
                m_Toolbar.BackButton.tooltip = "预览链从这里进入了 " + target.name + "；点此回到原 Timeline。";
            }
        }

        void RefreshAssetPreviewControls()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            bool active = preview.IsOpen && preview.Covers(SourceAsset);
            if (active && preview.SampledSource != m_ObservedPreviewSource)
            {
                m_ObservedPreviewSource = preview.SampledSource;
                if (m_ObservedPreviewSource && m_ObservedPreviewSource != SourceAsset)
                {
                    // 重新绑定会重建本面板，旧面板的刷新到此为止。
                    FollowPreview(m_ObservedPreviewSource);
                    return;
                }
            }
            if (m_AssetPreviewConfiguration != null)
            {
                m_AssetPreviewConfiguration.objectType = preview.ConfigurationType;
                m_AssetPreviewConfiguration.SetValueWithoutNotify(preview.Configuration);
                m_AssetPreviewConfiguration.SetEnabled(preview.IsRegistered);
            }
            if (m_AssetPreviewStatus != null)
                m_AssetPreviewStatus.text = !preview.IsRegistered ? TimelineAssetPreviewHost.MissingProviderReason :
                    preview.Error.Length != 0 ? preview.Error : active
                    ? $"资产预览 · 链 {preview.Time:F3} 秒 / {preview.SampledSource?.name} {preview.LocalTime:F3} 秒。{preview.Status}\n{preview.Chain?.Reason}"
                    : "选择资产预览调整节奏；会话预览确认碰撞、条件与完整运行结果。";
            if (m_AssetPreviewInputs != null)
            {
                m_AssetPreviewInputs.style.display = active && m_ShowPreviewInputs ? DisplayStyle.Flex : DisplayStyle.None;
                m_AssetPreviewInputs.MarkDirtyRepaint();
            }
            ApplyPreviewPath(active);
            if (active && m_AppliedAssetSample != preview.SampledRevision)
            {
                m_AppliedAssetSample = preview.SampledRevision;
                if (preview.SampledSource == SourceAsset && m_SlateProjection != null &&
                    m_SlateProjection.CurrentTime != preview.LocalTime)
                    m_SlateProjection.ApplyAuthoringPreviewTime(preview.LocalTime);
            }
            m_SlateSurface?.MarkDirtyRepaint();
        }

        // 资产预览路线叠加到编辑时间轴：全部换算为本 Timeline 的本地时间。
        void ApplyPreviewPath(bool active)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (m_SlateProjection == null)
                return;
            if (!active)
            {
                if (m_PreviewPathRevision != -1)
                    m_SlateProjection.SetPreviewPath(null, null, null, null, null);
                m_PreviewPathRevision = -1;
                return;
            }
            if (m_PreviewPathRevision == preview.Revision)
                return;
            m_PreviewPathRevision = preview.Revision;
            var spans = preview.Traversals.Where(span => span.Source == SourceAsset).ToArray();
            var visited = spans.Select(span => new Vector2(span.LocalStart.AsSingle, span.LocalEnd.AsSingle)).ToArray();
            var marks = new List<TimelinePreviewMark>();
            TimelineStaticChain chain = preview.Chain;
            if (chain != null)
            {
                foreach (TimelineStaticChainMarker marker in chain.Markers)
                {
                    if (preview.Query.ResolveTimeline(marker.Source.TimelineIdentity) != SourceAsset)
                        continue;
                    string label = marker.IsSectionJump
                        ? "接 " + SectionName(marker.NextSection?.AuthoringId)
                        : marker.Transition.Target == null ? "收招"
                        : "转移到 " + preview.Query.DescribeState(marker.Transition.Target);
                    marks.Add(new TimelinePreviewMark(marker.FromLocal.AsSingle, label,
                        $"预览第 {marker.Time.AsSingle:0.00} 秒"));
                }
                TimelineStaticChainSpan last = chain.Spans.Count == 0 ? null : chain.Spans[^1];
                if (last != null && chain.StopReason != TimelineStaticChainStopReason.StateMachineExited &&
                    preview.Query.ResolveTimeline(last.State.TimelineIdentity) == SourceAsset)
                    marks.Add(new TimelinePreviewMark(last.LocalEnd.AsSingle,
                        chain.StopReason == TimelineStaticChainStopReason.TimelineCompleted ? "播完收招" : "预览结束",
                        chain.Reason));
            }
            m_PreviewInputs.Clear();
            var inputs = new List<TimelinePreviewMark>();
            List<TimelinePreviewInputPoint> points = preview.Preferences.Inputs;
            for (int i = 0; i < points.Count; i++)
            {
                TimelinePreviewInputPoint point = points[i];
                if (!point.Active || spans.Length == 0)
                    continue;
                m_PreviewInputs.Add(i);
                inputs.Add(new TimelinePreviewMark(ToLocal(spans, point.Time), point.InputId,
                    $"输入 {point.InputId} · 预览第 {point.Time:0.00} 秒；拖动修改时刻"));
            }
            m_SlateProjection.SetPreviewPath(SourceAsset.name, visited, marks, inputs, MovePreviewInput);
        }

        void MovePreviewInput(int index, float localTime)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            var spans = preview.Traversals.Where(span => span.Source == SourceAsset).ToArray();
            if (index < 0 || index >= m_PreviewInputs.Count || spans.Length == 0)
                return;
            TimelinePreviewInputPoint point = preview.Preferences.Inputs[m_PreviewInputs[index]];
            point.Time = Mathf.Clamp(ToGlobal(spans, localTime), 0f, preview.Preferences.RangeEnd);
            preview.PreferencesChanged();
        }

        // 落在经过区间内按该区间换算；区间外按最近区间线性延伸，等同 Timeline 继续播放。
        static float ToLocal(TimelineAssetPreviewTraversal[] spans, float time)
        {
            TimelineAssetPreviewTraversal nearest = spans[0];
            foreach (TimelineAssetPreviewTraversal span in spans)
            {
                if (time >= span.Start.AsSingle && time <= span.End.AsSingle)
                    return span.LocalStart.AsSingle + time - span.Start.AsSingle;
                if (span.Start.AsSingle <= time)
                    nearest = span;
            }
            return nearest.LocalStart.AsSingle + time - nearest.Start.AsSingle;
        }

        static float ToGlobal(TimelineAssetPreviewTraversal[] spans, float local)
        {
            TimelineAssetPreviewTraversal nearest = spans[0];
            foreach (TimelineAssetPreviewTraversal span in spans)
            {
                if (local >= span.LocalStart.AsSingle && local <= span.LocalEnd.AsSingle)
                    return span.Start.AsSingle + local - span.LocalStart.AsSingle;
                if (span.LocalStart.AsSingle <= local)
                    nearest = span;
            }
            return nearest.Start.AsSingle + local - nearest.LocalStart.AsSingle;
        }

        string SectionName(string sectionId) =>
            SourceAsset.Data.Sections.FirstOrDefault(section => section.AuthoringId == sectionId)?.Name ?? "下一分段";

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
                bool satisfied = assumption == null ? TimelineAssetPreviewReadIntervals.DefaultSatisfied(in read) : assumption.Satisfied;
                string note = read.Source == OrchestrationStaticReadSource.ClipState ? " · 本链没有 Clip 提供，默认未成立"
                    : read.Undeterminable ? " · 无法静态判断" : string.Empty;
                bool selected = EditorGUILayout.ToggleLeft(read.Label + note, satisfied);
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
