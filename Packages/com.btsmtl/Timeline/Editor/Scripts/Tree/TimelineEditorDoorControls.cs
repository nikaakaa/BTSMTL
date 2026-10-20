using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
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
                    if (!list.Exists(value => value.Label == text))
                        list.Add(new TimelineClipDestination(text, ExitActivation(context.Query, destination)));
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
