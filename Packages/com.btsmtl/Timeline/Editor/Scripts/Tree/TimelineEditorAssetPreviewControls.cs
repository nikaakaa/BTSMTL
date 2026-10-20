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
        ToolbarMenu m_SchemeMenu;
        ToolbarMenu m_AssumptionMenu;
        VisualElement m_RealControls;
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
            var mode = new PopupField<string>(new List<string> { "快速", "真实" }, (int)m_PreviewMode);
            mode.RegisterValueChangedCallback(change =>
            {
                m_PreviewMode = (TimelinePreviewMode)mode.index;
                if (SourceAsset && preview.IsOpen)
                    OpenTimelinePreview();
                RefreshAssetPreviewControls();
            });
            mode.tooltip = "快速：按静态推导即时显示；真实：以同一输入方案经正式会话重演，并与快速结果对比。";
            mode.style.width = 64f;
            mode.style.height = 20f;
            mode.style.alignSelf = Align.Center;
            toolbar.Add(mode);
            toolbar.Add(CreateStateContextControl());
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
            toolbar.Add(new ToolbarButton(preview.Close) { text = "关闭预览" });
            m_SchemeMenu = new ToolbarMenu { tooltip = "输入方案：与本 Timeline 并列保存，可提交" };
            toolbar.Add(m_SchemeMenu);
            m_AssumptionMenu = new ToolbarMenu
            {
                text = "假设",
                tooltip = "其它运行数据的假设；切换后立即重新推导"
            };
            toolbar.Add(m_AssumptionMenu);
            toolbar.Add(new ToolbarButton(() =>
            {
                m_ShowPreviewInputs = !m_ShowPreviewInputs;
                RefreshAssetPreviewControls();
            }) { text = "预览设置" });
            toolbar.SetEnabled(SourceAsset);
            panel.Add(toolbar);
            m_RealControls = preview.CreateRealControls() ?? new VisualElement();
            panel.Add(m_RealControls);
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
            var context = m_StateContexts.Count == 0 ? default : m_StateContexts[m_StateContextIndex];
            TimelineAssetPreviewHost.Shared.Open(SourceAsset, m_PreviewMode, context.Query, context.State);
        }

        void DisposeAssetPreviewBindings()
        {
            TimelineAssetPreviewHost.Shared.Changed -= RefreshAssetPreviewControls;
            m_AssetPreviewStatus = null;
            m_AssetPreviewConfiguration = null;
            m_AssetPreviewInputs = null;
            m_SchemeMenu = null;
            m_AssumptionMenu = null;
            m_RealControls = null;
            m_AppliedAssetSample = 0;
            m_PreviewPathRevision = -1;
            m_PreviewInputs.Clear();
            DisposePreviewRouteRows();
        }

        // 预览链走进别的 Timeline 时编辑窗口跟过去，并把来处记为可返回的导航。
        void FollowPreview(TimelineAsset target)
        {
            if (TimelineAssetPreviewHost.Shared.Covers(SourceAsset))
                OpenRouteTimeline(target);
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
            if (m_RealControls != null)
                m_RealControls.style.display = m_PreviewMode == TimelinePreviewMode.Real ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_AssetPreviewStatus != null)
                m_AssetPreviewStatus.text = !preview.IsRegistered ? TimelineAssetPreviewHost.MissingProviderReason :
                    preview.Error.Length != 0 ? preview.Error : active && preview.IsReal
                    ? $"真实 · 会话 {preview.Time:F3} 秒 / {preview.SampledSource?.name} {preview.LocalTime:F3} 秒 · " +
                      (preview.Differences.Count == 0 ? "与快速推导一致" : $"{preview.Differences.Count} 处与快速推导不同（悬停路线行红标查看）")
                    : active
                    ? $"快速 · 链 {preview.Time:F3} 秒 / {preview.SampledSource?.name} {preview.LocalTime:F3} 秒。{preview.Status}\n{preview.Chain?.Reason}"
                    : "快速档即时调整节奏；真实档以同一输入方案经正式会话重演，确认碰撞、条件与完整运行结果。";
            if (m_AssetPreviewInputs != null)
            {
                m_AssetPreviewInputs.style.display = active && m_ShowPreviewInputs ? DisplayStyle.Flex : DisplayStyle.None;
                m_AssetPreviewInputs.MarkDirtyRepaint();
            }
            ApplyPreviewPath(active);
            RefreshSchemeMenus(active);
            m_PreviewRoute?.MarkDirtyRepaint();
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
            var spans = preview.Route.Where(span => span.Source == SourceAsset).ToArray();
            var visited = spans.Select(span => new Vector2(span.LocalStart.AsSingle, span.LocalEnd.AsSingle)).ToArray();
            var marks = new List<TimelinePreviewMark>();
            TimelineStaticChain chain = preview.Chain;
            if (preview.IsReal)
            {
                // 真实档：标出源状态在本 Timeline 播放中正式选中的转移；与快速推导的差异一并标出。
                foreach (TimelinePreviewRealTransition transition in preview.RealTransitions)
                    if (transition.SourceTimeline == SourceAsset)
                        marks.Add(new TimelinePreviewMark(transition.LocalTime,
                            transition.Transition == null ? "实际转移（连线已不在当前内容）"
                                : transition.Target == null ? "实际收招" : "实际转移到 " + preview.DescribeState(transition.Target),
                            $"会话第 {transition.Time:0.000} 秒"));
                foreach (TimelinePreviewDifference difference in preview.Differences)
                    if (spans.Length != 0 && difference.State != null &&
                        preview.Query.ResolveTimeline(difference.State.TimelineIdentity) == SourceAsset)
                        marks.Add(new TimelinePreviewMark(ToLocal(spans, difference.Time), "与快速不同", difference.Message));
            }
            else if (chain != null)
            {
                foreach (TimelineStaticChainMarker marker in chain.Markers)
                {
                    if (preview.Query.ResolveTimeline(marker.Source.TimelineIdentity) != SourceAsset)
                        continue;
                    string label = marker.Transition.Target == null ? "收招"
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
                bool accepted = DescribeInput(preview, i, out string reason);
                bool? pressed = PreviewInputPressed(preview, point);
                string value = pressed == null ? string.Empty : pressed.Value ? "按下 · " : "松开 · ";
                inputs.Add(new TimelinePreviewMark(ToLocal(spans, point.Time), point.InputId,
                    $"预览第 {point.Time:0.00} 秒 · {value}{reason}\n拖动改时刻，右键改键、改按下／松开或删除", accepted));
            }
            string[] choices = preview.Inputs.Select(descriptor => descriptor.InputId).ToArray();
            m_SlateProjection.SetPreviewPath(SourceAsset.name, visited, marks, inputs, new TimelinePreviewInputTrack(
                choices, MovePreviewInput, AddPreviewInput, ChangePreviewInput, RemovePreviewInput,
                index => index >= 0 && index < m_PreviewInputs.Count
                    ? PreviewInputPressed(preview, preview.Preferences.Inputs[m_PreviewInputs[index]]) : null,
                SetPreviewInputPressed));
        }

        // 开关类输入的按下（true）与松开（false）；其它类型返回 null。
        static bool? PreviewInputPressed(TimelineAssetPreviewHost preview, TimelinePreviewInputPoint point)
        {
            foreach (GraphHostInputDescriptor descriptor in preview.Inputs)
                if (descriptor.InputId == point.InputId)
                    return descriptor.ValueKind == GraphHostValueKind.Bool ? point.Boolean : null;
            return null;
        }

        void SetPreviewInputPressed(int index, bool pressed)
        {
            if (index < 0 || index >= m_PreviewInputs.Count)
                return;
            var preview = TimelineAssetPreviewHost.Shared;
            preview.Preferences.Inputs[m_PreviewInputs[index]].Boolean = pressed;
            preview.PreferencesChanged();
        }

        // 按键在其有效期内（请求按正式缓冲时长，其余到同键下一次改值）被读取该键的转移选中即为接上。
        static bool DescribeInput(TimelineAssetPreviewHost preview, int index, out string reason)
        {
            List<TimelinePreviewInputPoint> points = preview.Preferences.Inputs;
            TimelinePreviewInputPoint point = points[index];
            double until = preview.Preferences.RangeEnd;
            bool request = false;
            foreach (TimelinePreviewInputRequest value in preview.InputRequests)
                if (value.InputId == point.InputId)
                {
                    until = point.Time + value.DurationSeconds;
                    request = true;
                }
            if (!request)
                foreach (TimelinePreviewInputPoint other in points)
                    if (other != point && other.Active && other.InputId == point.InputId && other.Time > point.Time)
                        until = Math.Min(until, other.Time);
            if (preview.IsReal)
            {
                foreach (TimelinePreviewRealTransition real in preview.RealTransitions)
                    if (real.Transition != null && real.Time >= point.Time && real.Time < until &&
                        real.Transition.Reads.Any(read =>
                            read.Source == OrchestrationStaticReadSource.HostInput && read.Key == point.InputId))
                    {
                        reason = $"实际接上：{preview.DescribeState(real.Source)} → {preview.DescribeState(real.Target)}，第 {real.Time:0.000} 秒";
                        return true;
                    }
                reason = "真实重演中有效期内没有读取该键的转移发生";
                return false;
            }
            if (preview.Chain != null &&
                FindAcceptance(preview.Chain, point.InputId, point.Time, until, out TimelineStaticChainMarker marker))
            {
                reason = $"接上：{preview.Query.DescribeState(marker.Source)} → " +
                    $"{(marker.Transition.Target == null ? "出口" : preview.Query.DescribeState(marker.Transition.Target))}，" +
                    $"第 {marker.Time.AsSingle:0.00} 秒";
                return true;
            }
            TimelinePreviewRouteSegment? at = null;
            foreach (TimelinePreviewRouteSegment segment in preview.Route)
                if (segment.Start.AsSingle <= point.Time && point.Time < segment.End.AsSingle)
                    at = segment;
            if (!at.HasValue || at.Value.State == null)
            {
                reason = "未接上：此刻不在预览路线上的任何状态内";
                return false;
            }
            OrchestrationStaticState state = at.Value.State;
            var labels = new List<string>();
            foreach (OrchestrationTransitionWindow window in OrchestrationStateTransitionWindows.Build(state).Windows)
                foreach (OrchestrationStaticRead read in window.HostInputs)
                    if (read.Key == point.InputId)
                        labels.Add(window.EdgeLabel);
            reason = labels.Count == 0
                ? $"未接上：此刻在 {preview.Query.DescribeState(state)}，它的出边都不读 {point.InputId}"
                : $"未接上：此刻在 {preview.Query.DescribeState(state)}（本地 {(at.Value.LocalStart.AsSingle + point.Time - at.Value.Start.AsSingle):0.00} 秒），" +
                  $"读该键的转移为 {string.Join("；", labels)}，按键不在可走时段内或被更早的转移抢先";
            return false;
        }

        static bool FindAcceptance(TimelineStaticChain chain, string inputId, double from, double until,
            out TimelineStaticChainMarker found)
        {
            foreach (TimelineStaticChainMarker marker in chain.Markers)
            {
                double time = marker.Time.AsSingle;
                if (time < from || time >= until)
                    continue;
                foreach (OrchestrationStaticRead read in marker.Transition.Reads)
                    if (read.Source == OrchestrationStaticReadSource.HostInput && read.Key == inputId)
                    {
                        found = marker;
                        return true;
                    }
            }
            foreach (TimelineStaticChainBranch branch in chain.Branches)
                if (FindAcceptance(branch.Chain, inputId, from, until, out found))
                    return true;
            found = null;
            return false;
        }

        TimelinePreviewRouteSegment[] SourceSpans() =>
            TimelineAssetPreviewHost.Shared.Route.Where(span => span.Source == SourceAsset).ToArray();

        void MovePreviewInput(int index, float localTime)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            TimelinePreviewRouteSegment[] spans = SourceSpans();
            if (index < 0 || index >= m_PreviewInputs.Count || spans.Length == 0)
                return;
            TimelinePreviewInputPoint point = preview.Preferences.Inputs[m_PreviewInputs[index]];
            point.Time = Mathf.Clamp(ToGlobal(spans, localTime), 0f, preview.Preferences.RangeEnd);
            preview.PreferencesChanged();
        }

        void AddPreviewInput(string inputId, float localTime)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            TimelinePreviewRouteSegment[] spans = SourceSpans();
            if (spans.Length == 0)
                return;
            preview.Preferences.Inputs.Add(new TimelinePreviewInputPoint
            {
                InputId = inputId,
                Time = Mathf.Clamp(ToGlobal(spans, localTime), 0f, preview.Preferences.RangeEnd),
                Boolean = true
            });
            preview.PreferencesChanged();
        }

        void ChangePreviewInput(int index, string inputId)
        {
            if (index < 0 || index >= m_PreviewInputs.Count)
                return;
            var preview = TimelineAssetPreviewHost.Shared;
            preview.Preferences.Inputs[m_PreviewInputs[index]].InputId = inputId;
            preview.PreferencesChanged();
        }

        void RemovePreviewInput(int index)
        {
            if (index < 0 || index >= m_PreviewInputs.Count)
                return;
            var preview = TimelineAssetPreviewHost.Shared;
            preview.Preferences.Inputs.RemoveAt(m_PreviewInputs[index]);
            preview.PreferencesChanged();
        }

        // 落在经过区间内按该区间换算；区间外按最近区间线性延伸，等同 Timeline 继续播放。
        static float ToLocal(TimelinePreviewRouteSegment[] spans, float time)
        {
            TimelinePreviewRouteSegment nearest = spans[0];
            foreach (TimelinePreviewRouteSegment span in spans)
            {
                if (time >= span.Start.AsSingle && time <= span.End.AsSingle)
                    return span.LocalStart.AsSingle + time - span.Start.AsSingle;
                if (span.Start.AsSingle <= time)
                    nearest = span;
            }
            return nearest.LocalStart.AsSingle + time - nearest.Start.AsSingle;
        }

        static float ToGlobal(TimelinePreviewRouteSegment[] spans, float local)
        {
            TimelinePreviewRouteSegment nearest = spans[0];
            foreach (TimelinePreviewRouteSegment span in spans)
            {
                if (local >= span.LocalStart.AsSingle && local <= span.LocalEnd.AsSingle)
                    return span.Start.AsSingle + local - span.LocalStart.AsSingle;
                if (span.LocalStart.AsSingle <= local)
                    nearest = span;
            }
            return nearest.Start.AsSingle + local - nearest.LocalStart.AsSingle;
        }

        // 方案菜单：切换、另存、重命名、删除与从录制输入导入；假设菜单逐条切换并立即重新推导。
        void RefreshSchemeMenus(bool active)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (m_SchemeMenu == null || m_AssumptionMenu == null)
                return;
            m_SchemeMenu.SetEnabled(active);
            m_AssumptionMenu.SetEnabled(active);
            if (!active || preview.Preferences == null)
            {
                m_SchemeMenu.text = "输入方案";
                return;
            }
            TimelinePreviewInputSchemeStore store = preview.InputSchemes;
            int invalid = store.InvalidInputs(preview.Inputs).Count();
            m_SchemeMenu.text = "方案：" + preview.Preferences.Name + (invalid != 0 ? $" · {invalid} 个失效按键" : string.Empty);
            m_SchemeMenu.tooltip = store.Path.Length != 0 ? "存于 " + store.Path : "尚未保存：第一次修改时在入口 Timeline 旁创建";
            DropdownMenu schemes = m_SchemeMenu.menu;
            schemes.MenuItems().Clear();
            IReadOnlyList<TimelineAssetPreviewPreferences> list = store.All;
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                schemes.AppendAction(list[i].Name, _ =>
                    {
                        if (store.Select(index))
                            preview.PreferencesChanged();
                    },
                    list[i] == preview.Preferences ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            schemes.AppendSeparator();
            schemes.AppendAction("另存为…", _ => AskSchemeName(preview.Preferences.Name + " 副本", name =>
            {
                store.SaveAs(name);
                preview.PreferencesChanged();
            }));
            schemes.AppendAction("重命名…", _ => AskSchemeName(preview.Preferences.Name, name =>
            {
                store.Rename(name);
                preview.PreferencesChanged();
            }));
            schemes.AppendAction("删除当前方案", _ =>
                {
                    if (store.DeleteCurrent())
                        preview.PreferencesChanged();
                },
                list.Count > 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            schemes.AppendAction("从录制输入导入…", _ =>
            {
                string path = EditorUtility.OpenFilePanel("选择录制输入", Application.dataPath, string.Empty);
                if (path.Length != 0)
                    preview.ImportInputs(path);
            });
            DropdownMenu assumptions = m_AssumptionMenu.menu;
            assumptions.MenuItems().Clear();
            TimelineAssetPreviewPreferences preferences = preview.Preferences;
            if (preview.Assumptions.Count == 0)
                assumptions.AppendAction("本链没有需要假设的运行数据", null, DropdownMenuAction.Status.Disabled);
            foreach (OrchestrationStaticRead read in preview.Assumptions)
            {
                string identity = TimelineAssetPreviewReadIntervals.ConditionIdentity(in read);
                TimelinePreviewConditionAssumption assumption = preferences.Conditions.Find(value => value.Identity == identity);
                bool satisfied = assumption == null ? TimelineAssetPreviewReadIntervals.DefaultSatisfied(in read) : assumption.Satisfied;
                string note = read.Source == OrchestrationStaticReadSource.ClipState ? " · 本链没有 Clip 提供，默认不成立"
                    : read.Undeterminable ? " · 无法静态判断" : string.Empty;
                OrchestrationStaticRead captured = read;
                assumptions.AppendAction((read.Label + note).Replace('/', '／'), _ =>
                {
                    TimelinePreviewConditionAssumption value = preferences.Conditions.Find(item => item.Identity == identity);
                    if (value == null)
                    {
                        value = new TimelinePreviewConditionAssumption
                            { Identity = identity, Satisfied = TimelineAssetPreviewReadIntervals.DefaultSatisfied(in captured) };
                        preferences.Conditions.Add(value);
                    }
                    value.Satisfied = !value.Satisfied;
                    preview.PreferencesChanged();
                }, satisfied ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
        }

        void AskSchemeName(string initial, Action<string> apply)
        {
            Rect anchor = m_SchemeMenu.worldBound;
            UnityEditor.PopupWindow.Show(anchor, new SchemeNamePopup(initial, apply));
        }

        sealed class SchemeNamePopup : PopupWindowContent
        {
            readonly Action<string> m_Apply;
            string m_Name;

            internal SchemeNamePopup(string name, Action<string> apply)
            {
                m_Name = name;
                m_Apply = apply;
            }

            public override Vector2 GetWindowSize() => new(260f, 48f);

            public override void OnGUI(Rect rect)
            {
                GUI.SetNextControlName("scheme-name");
                m_Name = EditorGUILayout.TextField("方案名", m_Name);
                EditorGUI.FocusTextInControl("scheme-name");
                bool submit = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                if ((GUILayout.Button("确定") || submit) && !string.IsNullOrWhiteSpace(m_Name))
                {
                    m_Apply(m_Name.Trim());
                    editorWindow.Close();
                }
            }
        }

        // 预览设置：显示范围、假设目标、各输入点的取值（开关类为按下／松开），以及方案中已失效的按键。
        void DrawAssetPreviewInputs()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (!preview.IsOpen || !preview.Covers(SourceAsset))
                return;
            TimelineAssetPreviewPreferences preferences = preview.Preferences;
            m_PreviewInputsScroll = EditorGUILayout.BeginScrollView(m_PreviewInputsScroll);
            EditorGUI.BeginChangeCheck();
            preferences.RangeEnd = EditorGUILayout.FloatField("预览范围终点（秒）", preferences.RangeEnd);
            if (preferences.RangeEnd <= 0f)
                EditorGUILayout.HelpBox("预览范围终点必须大于零。", MessageType.Error);
            preferences.HasTarget = EditorGUILayout.Toggle("提供假设目标", preferences.HasTarget);
            if (preferences.HasTarget)
            {
                preferences.TargetPosition = EditorGUILayout.Vector3Field("假设目标位置", preferences.TargetPosition);
                preferences.TargetEuler = EditorGUILayout.Vector3Field("假设目标朝向", preferences.TargetEuler);
            }
            IReadOnlyList<GraphHostInputDescriptor> inputs = preview.Inputs;
            foreach (TimelinePreviewInputPoint point in preferences.Inputs)
            {
                GraphHostInputDescriptor descriptor = inputs.FirstOrDefault(value => value.InputId == point.InputId);
                if (descriptor == null)
                    continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{point.InputId} · {point.Time:0.###} 秒", GUILayout.Width(200f));
                    DrawInputValue(descriptor, point);
                }
            }
            bool changed = EditorGUI.EndChangeCheck();
            for (int i = 0; i < preferences.Inputs.Count; i++)
            {
                TimelinePreviewInputPoint point = preferences.Inputs[i];
                if (inputs.Any(descriptor => descriptor.InputId == point.InputId))
                    continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"失效按键：{point.InputId}（{point.Time:0.###} 秒），宿主已不声明该输入");
                    if (GUILayout.Button("删除", GUILayout.Width(60f)))
                    {
                        preferences.Inputs.RemoveAt(i--);
                        changed = true;
                    }
                }
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
