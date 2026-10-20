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
        readonly Dictionary<string, string> m_DoorLabels = new(StringComparer.Ordinal);
        readonly List<TimelineAuthoringExit> m_AuthoringExits = new();
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
            m_DoorLabels.Clear();
            m_AuthoringExits.Clear();
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
            if (m_DoorContextIndex >= m_DoorContexts.Count)
                m_DoorContextIndex = 0;
            m_DoorContextLabels = m_DoorContexts.Select(context => context.Query.Label + " / " + context.Query.DescribeState(context.State)).ToArray();
            ApplyDoorContext();
            m_DoorContextControl?.MarkDirtyRepaint();
        }

        void ApplyDoorContext()
        {
            m_DoorLabels.Clear();
            m_AuthoringExits.Clear();
            m_DoorContextIssues = GUIContent.none;
            if (m_DoorContexts.Count != 0)
            {
                var context = m_DoorContexts[m_DoorContextIndex];
                TimelineAssetPreviewHost.Shared.SetOrchestrationContext(SourceAsset, context.Query, context.State);
                TimelineStateDoors doors = TimelineStateDoors.Build(context.State);
                foreach (TimelineDoorDestination destination in doors.Destinations)
                {
                    string text = DestinationText(destination);
                    m_DoorLabels[destination.Door.ClipId] = m_DoorLabels.TryGetValue(destination.Door.ClipId, out string previous)
                        ? previous + " · " + text : text;
                    m_AuthoringExits.Add(new TimelineAuthoringExit(
                        $"{destination.Door.Start.AsSingle:F2}–{destination.Door.End.AsSingle:F2}s  {text}", false,
                        destination.Door.Start.AsSingle, destination.Door.End.AsSingle, ExitActivation(context.Query, destination)));
                }
                foreach (OrchestrationStaticTransition transition in doors.Anytime)
                {
                    OrchestrationStaticSourceLocation location = transition.Location;
                    m_AuthoringExits.Add(new TimelineAuthoringExit("→ 转移到 " + StateText(transition.Target), true, 0f, 0f,
                        () => context.Query.OpenTransition(location)));
                }
                foreach (var unresolved in doors.UnresolvedStateReads)
                {
                    string read = unresolved.Read.Label;
                    read = read.Substring(read.LastIndexOf('/') + 1);
                    if (read.StartsWith("tag:", StringComparison.Ordinal))
                        read = read.Substring(4);
                    m_AuthoringExits.Add(new TimelineAuthoringExit($"{read}：无法静态判断，按运行数据假设", true, 0f, 0f, null));
                }
                if (context.Query.Query.Errors.Count != 0)
                    m_DoorContextIssues = new GUIContent($"⚠ {context.Query.Query.Errors.Count} 个问题",
                        string.Join("\n", context.Query.Query.Errors));
            }
            else
                TimelineAssetPreviewHost.Shared.SetOrchestrationContext(SourceAsset, null, null);
            m_SlateProjection?.SetAuthoringClipLabels(m_DoorLabels);
            m_SlateProjection?.SetAuthoringExits(m_AuthoringExits);
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
                return "→ 转移到 " + StateText(destination.Transition.Target);
            string section = m_Timeline.Sections.FirstOrDefault(item => item.AuthoringId == destination.NextSection?.AuthoringId)?.Name;
            return "→ 连到 " + (string.IsNullOrEmpty(section) ? "未命名分段" : section);
        }

        string StateText(OrchestrationStaticState state) =>
            m_DoorContexts[m_DoorContextIndex].Query.DescribeState(state);
    }
}
