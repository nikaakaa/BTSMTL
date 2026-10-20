using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    public sealed partial class TimelineEditorWindow
    {
        readonly List<(TimelineOrchestrationQuery Query, OrchestrationStaticState State)> m_DoorContexts = new();
        readonly Dictionary<string, List<TimelineClipDestination>> m_ClipDestinations = new(StringComparer.Ordinal);
        readonly List<TimelineSectionContinueWindow> m_ContinueWindows = new();
        IMGUIContainer m_DoorContextControl;
        int m_DoorContextIndex;
        string[] m_DoorContextLabels = Array.Empty<string>();
        GUIContent m_DoorContextIssues = GUIContent.none;
        ITimelineOrchestrationAuthoringHost m_OrchestrationHost;
        bool m_DoorRefreshQueued;
        string m_DoorQueryReason = string.Empty;

        public bool FocusSection(string sectionAuthoringId)
        {
            if (!m_SlateProjection.FocusSource(null, null, sectionAuthoringId))
                return false;
            m_SlateProjection.FocusSectionGroup(sectionAuthoringId);
            return true;
        }

        VisualElement CreateDoorContextControl()
        {
            m_DoorContextControl = new IMGUIContainer(DrawDoorContext);
            m_DoorContextControl.style.width = 340f;
            m_DoorContextControl.style.height = 20f;
            m_DoorContextControl.style.alignSelf = Align.Center;
            return m_DoorContextControl;
        }

        void BindDoorQueries()
        {
            m_OrchestrationHost = TimelineWorkspaceModeBridge.Authoring as ITimelineOrchestrationAuthoringHost;
            if (m_OrchestrationHost != null)
                m_OrchestrationHost.OrchestrationQueriesChanged += QueueDoorQueryRefresh;
            QueueDoorQueryRefresh();
        }

        void DisposeDoorQueryBindings()
        {
            EditorApplication.delayCall -= RefreshDoorQueries;
            if (m_OrchestrationHost != null)
                m_OrchestrationHost.OrchestrationQueriesChanged -= QueueDoorQueryRefresh;
            m_OrchestrationHost = null;
            m_DoorRefreshQueued = false;
            m_DoorContexts.Clear();
            m_ClipDestinations.Clear();
            m_ContinueWindows.Clear();
            m_DoorContextControl = null;
        }

        void QueueDoorQueryRefresh()
        {
            if (m_DoorRefreshQueued)
                return;
            m_DoorRefreshQueued = true;
            EditorApplication.delayCall += RefreshDoorQueries;
        }

        void RefreshDoorQueries()
        {
            m_DoorRefreshQueued = false;
            m_DoorContexts.Clear();
            m_DoorQueryReason = string.Empty;
            if (m_Timeline == null)
                return;
            if (TimelineWorkspaceModeBridge.Authoring is ITimelineOrchestrationAuthoringHost host &&
                host.TryGetOrchestrationQueries(m_Timeline, out IReadOnlyList<TimelineOrchestrationQuery> queries,
                    out m_DoorQueryReason))
                foreach (TimelineOrchestrationQuery query in queries)
                    foreach (OrchestrationStaticState state in query.Query.States)
                        if (state.Timeline != null && query.ResolveTimeline(state.TimelineIdentity) == SourceAsset)
                            m_DoorContexts.Add((query, state));
            var ordered = m_DoorContexts
                .OrderBy(context => context.State.IsInitial ? 0 : 1)
                .ThenBy(context => context.State.HasStaticTimelineStartTime ? 0 : 1)
                .ThenBy(context => StartTime(context.State)).ToList();
            m_DoorContexts.Clear();
            m_DoorContexts.AddRange(ordered);
            if (m_DoorContextIndex >= m_DoorContexts.Count)
                m_DoorContextIndex = 0;
            m_DoorContextLabels = m_DoorContexts.Select(context => context.Query.Label + " / " + context.Query.DescribeState(context.State)).ToArray();
            ApplyDoorContext();
            m_DoorContextControl?.MarkDirtyRepaint();
        }

        static long StartTime(OrchestrationStaticState state) =>
            state.HasStaticTimelineStartTime ? state.TimelineStartTime.RawValue : 0L;

        void ApplyDoorContext()
        {
            m_ClipDestinations.Clear();
            m_ContinueWindows.Clear();
            m_DoorContextIssues = GUIContent.none;
            if (m_DoorContexts.Count != 0)
            {
                var context = m_DoorContexts[m_DoorContextIndex];
                TimelineAssetPreviewHost.Shared.SetOrchestrationContext(SourceAsset, context.Query, context.State);
                TimelineStateDoors doors = TimelineStateDoors.Build(context.State);
                foreach (TimelineDoorDestination destination in doors.Destinations)
                {
                    if (!m_ClipDestinations.TryGetValue(destination.Door.ClipId, out List<TimelineClipDestination> list))
                        m_ClipDestinations.Add(destination.Door.ClipId, list = new List<TimelineClipDestination>());
                    string text = DestinationText(destination);
                    TimelineDoorDestination target = destination;
                    if (!list.Exists(value => value.Label == text))
                        list.Add(new TimelineClipDestination(text, ExitActivation(context.Query, destination),
                            "预览：在此" + (destination.Kind == TimelineDoorDestinationKind.NextSection ? "接 " : string.Empty) + text,
                            () => PreviewThrough(target)));
                    if (destination.Kind == TimelineDoorDestinationKind.NextSection)
                        m_ContinueWindows.Add(new TimelineSectionContinueWindow(destination.Door.TrackId,
                            destination.Door.Start.AsSingle, destination.Door.End.AsSingle));
                }
                if (context.Query.Query.Errors.Count != 0)
                    m_DoorContextIssues = new GUIContent($"⚠ {context.Query.Query.Errors.Count} 个问题",
                        string.Join("\n", context.Query.Query.Errors));
            }
            else
                TimelineAssetPreviewHost.Shared.SetOrchestrationContext(SourceAsset, null, null);
            m_SlateProjection?.SetAuthoringDestinations(
                m_ClipDestinations.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<TimelineClipDestination>)pair.Value,
                    StringComparer.Ordinal),
                m_ContinueWindows);
        }

        // 从入口逐段补最早的衔接输入直到链走到目标出口窗口，再在窗口起点写入目标去向读取的按键输入；target 为空时一路接到最后一段。
        void PreviewThrough(TimelineDoorDestination target)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (m_DoorContexts.Count == 0)
            {
                ShowNotification(new GUIContent("当前 Timeline 没有状态上下文，无法推导接法。"));
                return;
            }
            if (!preview.IsOpen || preview.Source != SourceAsset || preview.Mode != TimelinePreviewMode.Asset)
            {
                m_PreviewMode = TimelinePreviewMode.Asset;
                OpenTimelinePreview();
            }
            if (!preview.IsOpen || preview.Instance == null)
            {
                ShowNotification(new GUIContent(preview.Error.Length != 0 ? preview.Error : "资产预览尚未打开。"));
                return;
            }
            OrchestrationStaticState state = m_DoorContexts[m_DoorContextIndex].State;
            TimelineStateDoors doors = TimelineStateDoors.Build(state);
            TimelineAssetPreviewPreferences preferences = preview.Preferences;
            preferences.Inputs.Clear();
            string failure = string.Empty;
            OrchestrationStaticClip previousDoor = null;
            for (int step = 0; step < 32; step++)
            {
                TimelineStaticChain chain = TimelineStaticChain.Build(state, preferences.StartingSectionId,
                    OrchestrationTime.FromSeconds(preferences.RangeEnd),
                    new TimelineAssetPreviewReadIntervals(preferences, preview.Instance.Inputs, preview.InputRequests));
                if (target != null && Reached(chain, target))
                    break;
                TimelineDoorDestination next = target;
                TimelineStaticChainSpan span = target == null ? null : SpanCovering(chain, state, target.Door);
                if (span == null)
                {
                    // 收招等后续状态不属于本 Timeline，按本状态最后一段找下一分段门。
                    TimelineStaticChainSpan last = chain.Spans.LastOrDefault(value => value.State == state);
                    next = last == null ? null : doors.Destinations
                        .Where(value => value.Kind == TimelineDoorDestinationKind.NextSection && value.Section == last.Section)
                        .OrderBy(value => value.Door.Start).FirstOrDefault();
                    span = last;
                }
                if (next == null || span == null || next != target && next.Door == previousDoor)
                {
                    if (target != null || next != null)
                        failure = "链在这里接不下去：" + chain.Reason;
                    break;
                }
                previousDoor = next.Door;
                OrchestrationTime local = OrchestrationTime.Max(next.Door.Start, span.LocalStart);
                OrchestrationTime start = span.Start + local - span.LocalStart;
                OrchestrationTime end = span.Start + next.Door.End - span.LocalStart;
                int before = preferences.Inputs.Count;
                foreach (string inputId in HostInputs(next, state))
                    ScheduleInput(preferences, preview, inputId, start.AsSingle, end.AsSingle);
                if (preferences.Inputs.Count == before)
                {
                    failure = "这个去向的条件不读取按键输入，需要在“输入日程与假设”里调整运行数据假设。";
                    break;
                }
                if (next == target)
                {
                    chain = TimelineStaticChain.Build(state, preferences.StartingSectionId,
                        OrchestrationTime.FromSeconds(preferences.RangeEnd),
                        new TimelineAssetPreviewReadIntervals(preferences, preview.Instance.Inputs, preview.InputRequests));
                    if (!Reached(chain, target))
                        failure = "已在窗口起点排入输入，但条件仍未成立：" + chain.Reason;
                    break;
                }
            }
            preview.PreferencesChanged();
            preview.SetTime(0f);
            preview.SetPlaying(failure.Length == 0);
            if (failure.Length != 0)
                ShowNotification(new GUIContent(failure));
        }

        static bool Reached(TimelineStaticChain chain, TimelineDoorDestination target)
        {
            foreach (TimelineStaticChainMarker marker in chain.Markers)
                if (target.Kind == TimelineDoorDestinationKind.Transition
                        ? marker.Transition == target.Transition
                        : marker.IsSectionJump && marker.NextSection == target.NextSection)
                    return true;
            return false;
        }

        static TimelineStaticChainSpan SpanCovering(TimelineStaticChain chain, OrchestrationStaticState state,
            OrchestrationStaticClip door)
        {
            foreach (TimelineStaticChainSpan span in chain.Spans)
                if (span.State == state && span.LocalStart < door.End && span.LocalEnd > door.Start)
                    return span;
            return null;
        }

        static IEnumerable<string> HostInputs(TimelineDoorDestination destination, OrchestrationStaticState state)
        {
            var result = new List<string>();
            if (destination.Kind == TimelineDoorDestinationKind.Transition)
                CollectHostInputs(destination.Transition.Condition, result);
            else
                foreach (OrchestrationStaticContinueRequest request in destination.Door.ContinueRequests)
                    if (request.TimelineOperationIndex == state.TimelineOperationIndex)
                        CollectHostInputs(request.Condition, result);
            return result;
        }

        // 只取肯定分支里的输入；取反下的输入表示"不能按"，不排入。
        static void CollectHostInputs(OrchestrationStaticCondition condition, List<string> result)
        {
            switch (condition.Kind)
            {
                case OrchestrationStaticConditionKind.All:
                case OrchestrationStaticConditionKind.Any:
                    foreach (OrchestrationStaticCondition child in condition.Children)
                        CollectHostInputs(child, result);
                    break;
                case OrchestrationStaticConditionKind.Read:
                    if (condition.Read.Source == OrchestrationStaticReadSource.HostInput && !result.Contains(condition.Read.Key))
                        result.Add(condition.Read.Key);
                    break;
            }
        }

        static void ScheduleInput(TimelineAssetPreviewPreferences preferences, TimelineAssetPreviewHost preview,
            string inputId, float start, float end)
        {
            bool request = preview.InputRequests.Any(value => value.InputId == inputId);
            var descriptor = preview.Instance.Inputs.FirstOrDefault(value => value.InputId == inputId);
            if (descriptor == null || !request && descriptor.ValueKind != BTSMTL.Graphs.GraphHostValueKind.Bool)
                return;
            preferences.Inputs.Add(new TimelinePreviewInputPoint { InputId = inputId, Time = start, Boolean = true, Active = true });
            if (!request)
                preferences.Inputs.Add(new TimelinePreviewInputPoint { InputId = inputId, Time = end, Boolean = false, Active = true });
        }

        Action ExitActivation(TimelineOrchestrationQuery query, TimelineDoorDestination destination)
        {
            if (destination.Kind == TimelineDoorDestinationKind.Transition)
            {
                OrchestrationStaticSourceLocation location = destination.Transition.Location;
                return () => query.OpenTransition(location);
            }
            string sectionId = destination.NextSection?.AuthoringId;
            return () => FocusSection(sectionId);
        }

        void DrawDoorContext()
        {
            Rect rect = m_DoorContextControl.contentRect;
            if (m_DoorContexts.Count == 0)
            {
                EditorGUI.LabelField(rect, new GUIContent(m_DoorQueryReason.Length == 0 ? "无状态上下文" : "无状态上下文 ⓘ",
                    m_DoorQueryReason.Length == 0 ? "当前 Timeline 没有唯一状态绑定；仍可独立预览资产。" : m_DoorQueryReason),
                    EditorStyles.miniLabel);
                return;
            }
            float issuesWidth = m_DoorContextIssues == GUIContent.none ? 0f : 72f;
            Rect popupRect = new Rect(rect.x, rect.y + 1f, rect.width - issuesWidth, 18f);
            int selected = EditorGUI.Popup(popupRect, m_DoorContextIndex, m_DoorContextLabels, EditorStyles.toolbarPopup);
            if (selected != m_DoorContextIndex)
            {
                m_DoorContextIndex = selected;
                ApplyDoorContext();
            }
            if (issuesWidth > 0f)
                EditorGUI.LabelField(new Rect(popupRect.xMax + 4f, rect.y, issuesWidth - 4f, rect.height),
                    m_DoorContextIssues, EditorStyles.miniLabel);
        }

        string DestinationText(TimelineDoorDestination destination)
        {
            if (destination.Kind == TimelineDoorDestinationKind.Transition)
                return TransitionText(destination.Transition);
            string section = m_Timeline.Sections.FirstOrDefault(item => item.AuthoringId == destination.NextSection?.AuthoringId)?.Name;
            return string.IsNullOrEmpty(section) ? "未命名分段" : section;
        }

        string TransitionText(OrchestrationStaticTransition transition)
        {
            if (transition.Target == null)
                return "收招";
            if (transition.Target.OperationIndex == m_DoorContexts[m_DoorContextIndex].State.OperationIndex)
                return "重新开始本状态";
            return "转移到 " + StateText(transition.Target);
        }

        string StateText(OrchestrationStaticState state) =>
            m_DoorContexts[m_DoorContextIndex].Query.DescribeState(state);
    }
}
