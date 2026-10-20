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
        IMGUIContainer m_DoorPanel;
        int m_DoorContextIndex;
        string[] m_DoorContextLabels = Array.Empty<string>();
        TimelineStateDoors m_CurrentDoors;
        ITimelineOrchestrationAuthoringHost m_OrchestrationHost;
        bool m_DoorRefreshQueued;
        string m_DoorQueryReason = string.Empty;
        Vector2 m_DoorScroll;

        public bool FocusSection(string sectionAuthoringId)
        {
            if (!m_SlateProjection.FocusSource(null, null, sectionAuthoringId))
                return false;
            foreach (var section in m_Timeline.Sections)
                if (section.AuthoringId == sectionAuthoringId)
                {
                    m_SlateProjection.ApplyAuthoringPreviewTime(section.Time.AsSingle);
                    break;
                }
            return true;
        }

        VisualElement CreateDoorExitsPanel()
        {
            m_DoorPanel = new IMGUIContainer(DrawDoorExits);
            m_DoorPanel.style.width = 260f;
            m_DoorPanel.style.minWidth = 220f;
            m_OrchestrationHost = TimelineWorkspaceModeBridge.Authoring as ITimelineOrchestrationAuthoringHost;
            if (m_OrchestrationHost != null)
                m_OrchestrationHost.OrchestrationQueriesChanged += QueueDoorQueryRefresh;
            QueueDoorQueryRefresh();
            return m_DoorPanel;
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
            m_CurrentDoors = null;
            m_DoorPanel = null;
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
            m_DoorLabels.Clear();
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
            m_DoorPanel?.MarkDirtyRepaint();
        }

        void ApplyDoorContext()
        {
            m_DoorLabels.Clear();
            if (m_DoorContexts.Count != 0)
            {
                var context = m_DoorContexts[m_DoorContextIndex];
                TimelineAssetPreviewHost.Shared.SetOrchestrationContext(SourceAsset, context.Query, context.State);
                m_CurrentDoors = TimelineStateDoors.Build(context.State);
                foreach (TimelineDoorDestination destination in m_CurrentDoors.Destinations)
                    if (m_DoorLabels.TryGetValue(destination.Door.ClipId, out string previous))
                        m_DoorLabels[destination.Door.ClipId] = previous + " · " + DestinationText(destination);
                    else
                        m_DoorLabels.Add(destination.Door.ClipId, DestinationText(destination));
            }
            else
            {
                m_CurrentDoors = null;
                TimelineAssetPreviewHost.Shared.SetOrchestrationContext(SourceAsset, null, null);
            }
            m_SlateProjection?.SetAuthoringClipLabels(m_DoorLabels);
        }

        void DrawDoorExits()
        {
            EditorGUILayout.LabelField("出口 · 由正式图结构推导", EditorStyles.boldLabel);
            if (m_DoorContexts.Count == 0)
            {
                EditorGUILayout.HelpBox(m_DoorQueryReason.Length == 0
                    ? "当前 Timeline 没有唯一状态绑定；仍可独立预览资产。" : m_DoorQueryReason, MessageType.Info);
                if (GUILayout.Button("重新读取作者关系"))
                    QueueDoorQueryRefresh();
                return;
            }
            int selected = EditorGUILayout.Popup("作者上下文", m_DoorContextIndex, m_DoorContextLabels);
            if (selected != m_DoorContextIndex)
            {
                m_DoorContextIndex = selected;
                ApplyDoorContext();
            }
            var context = m_DoorContexts[m_DoorContextIndex];
            foreach (string error in context.Query.Query.Errors)
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            TimelineStateDoors doors = m_CurrentDoors;
            m_DoorScroll = EditorGUILayout.BeginScrollView(m_DoorScroll);
            foreach (TimelineDoorDestination destination in doors.Destinations)
            {
                EditorGUILayout.LabelField(DoorText(destination.Door), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(DestinationText(destination), EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(destination.Kind == TimelineDoorDestinationKind.NextSection
                        ? "定位门与目标分段" : "打开对应状态转移"))
                {
                    if (destination.Kind == TimelineDoorDestinationKind.Transition)
                        context.Query.OpenTransition(destination.Transition.Location);
                    else
                    {
                        m_SlateProjection.FocusSource(destination.Door.TrackId, destination.Door.ClipId);
                        m_SlateProjection.ApplyAuthoringPreviewTime(destination.NextSection.Start.AsSingle);
                    }
                }
            }
            foreach (OrchestrationStaticTransition transition in doors.Anytime)
                if (GUILayout.Button("随时 → " + StateText(transition.Target)))
                    context.Query.OpenTransition(transition.Location);
            foreach (var unresolved in doors.UnresolvedStateReads)
                EditorGUILayout.HelpBox($"{unresolved.Read.Label}：无法静态判断，按运行数据列为假设。", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        string DoorText(OrchestrationStaticClip door)
        {
            string track = m_Timeline.Tracks.FirstOrDefault(item => item.AuthoringId == door.TrackId)?.Name;
            return $"{(string.IsNullOrEmpty(track) ? "未命名轨道" : track)}  {door.Start.AsSingle:F2}–{door.End.AsSingle:F2}s";
        }

        string DestinationText(TimelineDoorDestination destination)
        {
            if (destination.Kind == TimelineDoorDestinationKind.Transition)
                return "→ 转移到 " + StateText(destination.Transition.Target);
            string section = m_Timeline.Sections.FirstOrDefault(item => item.AuthoringId == destination.NextSection?.AuthoringId)?.Name;
            return "→ 进入分段 " + (string.IsNullOrEmpty(section) ? "未命名分段" : section);
        }

        string StateText(OrchestrationStaticState state) =>
            m_DoorContexts[m_DoorContextIndex].Query.DescribeState(state);
    }
}
