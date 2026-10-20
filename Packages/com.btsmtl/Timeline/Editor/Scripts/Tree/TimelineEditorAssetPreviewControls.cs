using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
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
        int m_PreviewPathPlayback = -1;
        TimelinePreviewInputPoint m_DragPoint;
        TimelinePreviewRouteSegment m_DragSegment;
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
            m_PreviewPathPlayback = -1;
            m_DragPoint = null;
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
            {
                string playback = active && preview.TryGetPlayback(SourceAsset, out TimelinePreviewRouteSegment[] spans)
                    ? $"本轴显示 {SourceAsset.name} 第 {spans[0].Occurrence + 1} 次播放（{spans.Length} 圈，点路线行选择其它播放）"
                    : string.Empty;
                string note = preview.LocalNote.Length == 0 ? string.Empty : " · " + preview.LocalNote;
                m_AssetPreviewStatus.text = !preview.IsRegistered ? TimelineAssetPreviewHost.MissingProviderReason :
                    preview.Error.Length != 0 ? preview.Error : active && preview.IsReal
                    ? $"真实 · 会话 {preview.Time:F3} 秒 / {preview.SampledSource?.name} {preview.LocalTime:F3} 秒{note} · " +
                      (preview.RealStale ? "结果过期，待重演：" + preview.RealStaleReason
                          : preview.Differences.Count == 0 ? "与快速推导一致"
                          : $"{preview.Differences.Count} 处与快速推导不同（悬停路线行红标查看）") + "\n" + playback
                    : active
                    ? $"快速 · 链 {preview.Time:F3} 秒 / {preview.SampledSource?.name} {preview.LocalTime:F3} 秒。{preview.Status}\n{preview.Chain?.Reason}\n{playback}"
                    : "快速档即时调整节奏；真实档以同一输入方案经正式会话重演，确认碰撞、条件与完整运行结果。";
            }
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
            // 编辑时间轴只显示本 Timeline 的一次播放（路线上选定的或游标所在的）；换播放时重建叠加。
            preview.TryGetPlayback(SourceAsset, out TimelinePreviewRouteSegment[] spans);
            int playback = spans.Length == 0 ? -1 : spans[0].Playback;
            if (m_PreviewPathRevision == preview.Revision && m_PreviewPathPlayback == playback)
                return;
            m_PreviewPathRevision = preview.Revision;
            m_PreviewPathPlayback = playback;
            var visited = spans.Select(span => new Vector2(span.LocalStart.AsSingle, span.LocalEnd.AsSingle)).ToArray();
            var marks = new List<TimelinePreviewMark>();
            TimelineStaticChain chain = preview.Chain;
            if (preview.IsReal)
            {
                // 真实档：标出源状态在这次播放中正式选中的转移；与快速推导的差异一并标出。
                foreach (TimelinePreviewRealTransition transition in preview.RealTransitions)
                    if (transition.Playback != null && spans.Any(span => SamePlayback(span, transition.Playback.Record)))
                        marks.Add(new TimelinePreviewMark(transition.LocalTime,
                            transition.Transition == null ? "实际转移（连线已不在当前内容）"
                                : transition.Target == null ? "实际收招" : "实际转移到 " + preview.DescribeState(transition.Target),
                            $"会话第 {transition.Time:0.000} 秒" + (transition.LocalNote == null ? string.Empty : " · " + transition.LocalNote)));
                foreach (TimelinePreviewDifference difference in preview.Differences)
                    if (difference.State != null && preview.Query.ResolveTimeline(difference.State.TimelineIdentity) == SourceAsset &&
                        TryLocal(spans, difference.Time, out float local, out _, out _))
                        marks.Add(new TimelinePreviewMark(local, "与快速不同", difference.Message));
            }
            else if (chain != null)
            {
                foreach (TimelineStaticChainMarker marker in chain.Markers)
                {
                    if (preview.Query.ResolveTimeline(marker.Source.TimelineIdentity) != SourceAsset ||
                        !spans.Any(span => span.Contains(marker.Time)))
                        continue;
                    string label = marker.Transition.Target == null ? "收招"
                        : "转移到 " + preview.Query.DescribeState(marker.Transition.Target);
                    marks.Add(new TimelinePreviewMark(marker.FromLocal.AsSingle, label,
                        $"预览第 {marker.Time.AsSingle:0.00} 秒"));
                }
                TimelineStaticChainSpan last = chain.Spans.Count == 0 ? null : chain.Spans[^1];
                if (last != null && chain.StopReason != TimelineStaticChainStopReason.StateMachineExited &&
                    spans.Any(span => span.Start == last.Start))
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
                if (!point.Active || !TryLocal(spans, point.Time, out float local, out TimelinePreviewRouteSegment at,
                    out string note))
                    continue;
                m_PreviewInputs.Add(i);
                bool accepted = DescribeInput(preview, i, out string reason);
                bool? pressed = PreviewInputPressed(preview, point);
                string value = pressed == null ? string.Empty : pressed.Value ? "按下 · " : "松开 · ";
                inputs.Add(new TimelinePreviewMark(local, point.InputId,
                    $"预览第 {point.Time:0.00} 秒 · {at.Label} · {value}{reason}" +
                    (note == null ? string.Empty : "\n" + note) + "\n拖动改时刻，右键改键、改按下／松开或删除", accepted));
            }
            string[] choices = preview.Inputs.Select(descriptor => descriptor.InputId).ToArray();
            m_SlateProjection.SetPreviewPath(SourceAsset.name, visited, marks, inputs, new TimelinePreviewInputTrack(
                choices, BeginMovePreviewInput, MovePreviewInput, AddPreviewInput, ChangePreviewInput, RemovePreviewInput,
                index => index >= 0 && index < m_PreviewInputs.Count
                    ? PreviewInputPressed(preview, preview.Preferences.Inputs[m_PreviewInputs[index]]) : null,
                SetPreviewInputPressed));
        }

        static bool SamePlayback(TimelinePreviewRouteSegment span, BtsmtlExecutionRecord.RecordedPlayback played) =>
            span.Recorded is { } recorded && recorded.PlaybackId == played.PlaybackId && recorded.Owner.Equals(played.Owner);

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

        // 输入点与转移的关系：读取列表与有效期重叠只用于找出相关转移；只有转移条件沿“全部成立”必须读到该开关键按下、
        // 且这一点是有效期内该键唯一的值时，才确认它参与成立并标为接上。OR、NOT、非开关输入只说明相关，不声称因果。
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
            string window = $"有效期 {point.Time:0.000}–{until:0.000} 秒";
            bool pressed = PreviewInputPressed(preview, point) == true;
            if (preview.IsReal)
            {
                if (preview.RealStale)
                {
                    reason = "结果过期，待重演：" + preview.RealStaleReason;
                    return false;
                }
                foreach (TimelinePreviewRealTransition real in preview.RealTransitions)
                {
                    if (real.Transition == null || real.Time < point.Time || real.Time >= until ||
                        !ReadsInput(real.Transition, point.InputId))
                        continue;
                    string edge = $"{preview.DescribeState(real.Source)}（第 {real.Record.Source.ActivationGeneration} 次激活）→ " +
                        $"{preview.DescribeState(real.Target)}，第 {real.Time:0.000} 秒";
                    if (pressed && RequiresInput(real.Transition.Condition, point.InputId) &&
                        SoleValue(points, point, real.Time))
                    {
                        reason = $"实际接上：{edge}；该转移须读到此键按下，{window}内只有这一点";
                        return true;
                    }
                    reason = $"实际相关：{window}内发生读取该键的转移 {edge}；条件含或／非、非开关输入或有多个输入点，无法确认这一点是否促成";
                    return false;
                }
                reason = $"真实重演{window}内没有读取该键的转移发生";
                return false;
            }
            if (preview.Chain != null &&
                FindRelated(preview.Chain, point.InputId, point.Time, until, out TimelineStaticChainMarker marker))
            {
                string edge = $"{preview.Query.DescribeState(marker.Source)} → " +
                    $"{(marker.Transition.Target == null ? "出口" : preview.Query.DescribeState(marker.Transition.Target))}，" +
                    $"第 {marker.Time.AsSingle:0.000} 秒";
                string basis = preview.Assumptions.Count == 0 ? "按当前方案推导" : "按当前方案与假设菜单中的运行数据假设推导";
                if (pressed && RequiresInput(marker.Transition.Condition, point.InputId) &&
                    SoleValue(points, point, marker.Time.AsSingle))
                {
                    reason = $"接上：{edge}；该转移须读到此键按下，{window}内只有这一点（{basis}）";
                    return true;
                }
                reason = $"相关：{window}内推导发生读取该键的转移 {edge}；条件含或／非、非开关输入或有多个输入点，无法确认这一点是否促成（{basis}）";
                return false;
            }
            TimelinePreviewRouteSegment? at = null;
            OrchestrationTime time = OrchestrationTime.FromSeconds(point.Time);
            foreach (TimelinePreviewRouteSegment segment in preview.Route)
                if (segment.Contains(time) && segment.State != null)
                    at = segment;
            if (at is not { } current)
            {
                reason = $"{window}内没有读取该键的转移发生；按下时不在预览路线上的任何状态内";
                return false;
            }
            var labels = new List<string>();
            foreach (OrchestrationTransitionWindow transition in OrchestrationStateTransitionWindows.Build(current.State).Windows)
                foreach (OrchestrationStaticRead read in transition.HostInputs)
                    if (read.Key == point.InputId)
                        labels.Add(transition.EdgeLabel);
            string state = preview.Query.DescribeState(current.State);
            string local = current.TryLocal(time, out OrchestrationTime mapped, out _) ? $"本地 {mapped.AsSingle:0.000} 秒" : current.Label;
            reason = labels.Count == 0
                ? $"{window}内没有读取该键的转移发生；按下时在 {state}（{local}），它的出边都不读 {point.InputId}"
                : $"{window}内没有读取该键的转移发生；按下时在 {state}（{local}），读该键的转移可走时段：{string.Join("；", labels)}";
            return false;
        }

        static bool ReadsInput(OrchestrationStaticTransition transition, string inputId) =>
            transition.Reads.Any(read => read.Source == OrchestrationStaticReadSource.HostInput && read.Key == inputId);

        // 条件只有读到该键为真才能成立：键本身，或“全部成立”中的一项；“任一成立”需每项都要求它；NOT 下无法确认。
        static bool RequiresInput(OrchestrationStaticCondition condition, string inputId) => condition.Kind switch
        {
            OrchestrationStaticConditionKind.Read => condition.Read.Source == OrchestrationStaticReadSource.HostInput &&
                condition.Read.Key == inputId,
            OrchestrationStaticConditionKind.All => condition.Children.Any(child => RequiresInput(child, inputId)),
            OrchestrationStaticConditionKind.Any => condition.Children.Count != 0 &&
                condition.Children.All(child => RequiresInput(child, inputId)),
            _ => false
        };

        // 从这一点到转移时刻之间没有同键的其它输入点。
        static bool SoleValue(List<TimelinePreviewInputPoint> points, TimelinePreviewInputPoint point, double time) =>
            !points.Any(other => other != point && other.Active && other.InputId == point.InputId &&
                other.Time >= point.Time && other.Time <= time);

        static bool FindRelated(TimelineStaticChain chain, string inputId, double from, double until,
            out TimelineStaticChainMarker found)
        {
            foreach (TimelineStaticChainMarker marker in chain.Markers)
            {
                double time = marker.Time.AsSingle;
                if (time >= from && time < until && ReadsInput(marker.Transition, inputId))
                {
                    found = marker;
                    return true;
                }
            }
            foreach (TimelineStaticChainBranch branch in chain.Branches)
                if (FindRelated(branch.Chain, inputId, from, until, out found))
                    return true;
            found = null;
            return false;
        }

        // 拖动开始时固定输入点及其所在的那一圈播放，整个手势都换回这次播放的预览时刻。
        void BeginMovePreviewInput(int index)
        {
            m_DragPoint = null;
            var preview = TimelineAssetPreviewHost.Shared;
            if (index < 0 || index >= m_PreviewInputs.Count || !preview.TryGetPlayback(SourceAsset, out var spans))
                return;
            TimelinePreviewInputPoint point = preview.Preferences.Inputs[m_PreviewInputs[index]];
            if (TryLocal(spans, point.Time, out _, out TimelinePreviewRouteSegment at, out _))
            {
                m_DragPoint = point;
                m_DragSegment = at;
            }
        }

        void MovePreviewInput(int index, float localTime)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (m_DragPoint == null)
                return;
            if (!TryGlobal(preview, m_DragSegment, localTime, out float time))
                return;
            m_DragPoint.Time = Mathf.Clamp(time, 0f, preview.Preferences.RangeEnd);
            preview.PreferencesChanged();
        }

        void AddPreviewInput(string inputId, float localTime)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (!preview.TryGetPlayback(SourceAsset, out TimelinePreviewRouteSegment[] spans))
                return;
            OrchestrationTime local = OrchestrationTime.FromSeconds(localTime);
            OrchestrationTime cursor = OrchestrationTime.FromSeconds(preview.Time);
            // 优先游标所在的那一圈，其次第一个经过该本地时刻的圈；快速档在播放终点之后按该次播放继续延伸。
            TimelinePreviewRouteSegment target = spans[^1];
            foreach (TimelinePreviewRouteSegment span in spans.OrderBy(span => span.Contains(cursor) ? 0 : 1))
                if (local >= span.LocalStart && local <= span.LocalEnd)
                {
                    target = span;
                    break;
                }
            if (!TryGlobal(preview, target, localTime, out float time))
                return;
            preview.Preferences.Inputs.Add(new TimelinePreviewInputPoint
            {
                InputId = inputId,
                Time = Mathf.Clamp(time, 0f, preview.Preferences.RangeEnd),
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

        // 预览时刻落在这次播放的某一圈内时经该段的正式映射得到本地时刻；不在这次播放内不显示，不外推。
        static bool TryLocal(TimelinePreviewRouteSegment[] spans, float time, out float local,
            out TimelinePreviewRouteSegment at, out string note)
        {
            OrchestrationTime value = OrchestrationTime.FromSeconds(time);
            foreach (TimelinePreviewRouteSegment span in spans)
                if (span.Contains(value) && span.TryLocal(value, out OrchestrationTime mapped, out note))
                {
                    local = mapped.AsSingle;
                    at = span;
                    return true;
                }
            local = 0f;
            at = default;
            note = null;
            return false;
        }

        // 换回所选那一圈的预览时刻；真实档没有记录覆盖时拒绝并说明。
        static bool TryGlobal(TimelineAssetPreviewHost preview, TimelinePreviewRouteSegment span, float local, out float time)
        {
            if (span.TryGlobal(OrchestrationTime.FromSeconds(local), out OrchestrationTime mapped, out string message))
            {
                time = mapped.AsSingle;
                return true;
            }
            time = 0f;
            preview.ReportRejectedEdit("无法在这次播放定位输入：" + message);
            return false;
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
