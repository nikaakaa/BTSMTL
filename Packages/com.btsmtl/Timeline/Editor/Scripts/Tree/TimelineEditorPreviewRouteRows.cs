using System.Collections.Generic;
using BTSMTL.Compilation.Analysis;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    // 编辑时间轴下方的并排路线：每个经过的 Timeline 一行、按预览全局时刻排布，被打断的未播部分淡色标出，
    // 并行分支各占一行；共用预览播放头，点击定位预览时刻，双击一行把编辑时间轴切到该 Timeline。
    public sealed partial class TimelineEditorWindow
    {
        const float RouteLabelWidth = 170f;
        const float RouteRowHeight = 18f;

        sealed class RouteBar
        {
            internal float Start;
            internal float End;
            internal float UnplayedEnd;
            internal string Tooltip;
        }

        sealed class RouteRow
        {
            internal string Label;
            internal TimelineAsset Source;
            internal int Depth;
            internal readonly List<RouteBar> Bars = new();
        }

        IMGUIContainer m_PreviewRoute;
        readonly List<RouteRow> m_RouteRows = new();
        long m_RouteRevision = -1;
        float m_RouteEnd = 1f;

        VisualElement CreatePreviewRouteRows()
        {
            m_PreviewRoute = new IMGUIContainer(DrawPreviewRoute) { name = "preview-route-rows" };
            m_PreviewRoute.style.flexShrink = 0f;
            m_RouteRevision = -1;
            return m_PreviewRoute;
        }

        void DisposePreviewRouteRows()
        {
            m_PreviewRoute = null;
            DisposeRouteRowsContent();
        }

        void RebuildRouteRows()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            m_RouteRevision = preview.Revision;
            m_RouteRows.Clear();
            m_RouteEnd = Mathf.Max(.1f, preview.Duration);
            if (preview.Chain == null)
            {
                var row = new RouteRow { Label = SourceAsset ? SourceAsset.name : "Timeline", Source = SourceAsset };
                foreach (TimelineAssetPreviewTraversal traversal in preview.Traversals)
                    row.Bars.Add(new RouteBar
                    {
                        Start = traversal.Start.AsSingle, End = traversal.End.AsSingle, UnplayedEnd = traversal.End.AsSingle,
                        Tooltip = $"{traversal.Start.AsSingle:0.00}–{traversal.End.AsSingle:0.00} s"
                    });
                if (row.Bars.Count != 0)
                    m_RouteRows.Add(row);
                return;
            }
            AddRouteChain(preview.Query, preview.Chain, 0, string.Empty);
        }

        void AddRouteChain(TimelineOrchestrationQuery query, TimelineStaticChain chain, int depth, string prefix)
        {
            var rows = new Dictionary<string, RouteRow>();
            foreach (TimelineStaticChainSpan span in chain.Spans)
            {
                string identity = span.State.TimelineIdentity;
                if (!rows.TryGetValue(identity, out RouteRow row))
                {
                    TimelineAsset source = query.ResolveTimeline(identity);
                    row = new RouteRow { Label = prefix + (source ? source.name : identity), Source = source, Depth = depth };
                    rows.Add(identity, row);
                    m_RouteRows.Add(row);
                }
                float start = span.Start.AsSingle;
                float end = span.End.AsSingle;
                float unplayed = span.Interrupted ? end + (span.State.Timeline.Duration - span.LocalEnd).AsSingle : end;
                row.Bars.Add(new RouteBar
                {
                    Start = start, End = end, UnplayedEnd = unplayed,
                    Tooltip = $"{query.DescribeState(span.State)} · 预览 {start:0.00}–{end:0.00} s · 本地 " +
                        $"{span.LocalStart.AsSingle:0.00}–{span.LocalEnd.AsSingle:0.00} s" +
                        (span.Interrupted ? " · 被转移打断，淡色为未播部分" : span.PlaybackCompleted ? " · 播到终点" : string.Empty)
                });
                m_RouteEnd = Mathf.Max(m_RouteEnd, unplayed);
            }
            foreach (TimelineStaticChainBranch branch in chain.Branches)
                AddRouteChain(query, branch.Chain, depth + 1, $"并行 · {query.DescribeState(branch.Owner)} / ");
        }

        void DrawPreviewRoute()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            bool active = preview.IsOpen && preview.Covers(SourceAsset);
            if (!active)
            {
                if (m_RouteRows.Count != 0)
                    DisposeRouteRowsContent();
                m_PreviewRoute.style.height = 0f;
                return;
            }
            if (m_RouteRevision != preview.Revision)
                RebuildRouteRows();
            float height = (m_RouteRows.Count + 1) * RouteRowHeight;
            m_PreviewRoute.style.height = height;
            Rect rect = new(0, 0, m_PreviewRoute.contentRect.width, height);
            EditorGUI.DrawRect(rect, new Color(.15f, .15f, .15f));
            Rect axis = new(RouteLabelWidth, 0, Mathf.Max(1f, rect.width - RouteLabelWidth - 4f), RouteRowHeight);
            GUI.Label(new Rect(4, 0, RouteLabelWidth - 8, RouteRowHeight), "预览路线", EditorStyles.miniBoldLabel);
            for (int tick = 0; tick <= 4; tick++)
            {
                float time = m_RouteEnd * tick / 4f;
                float x = RouteX(axis, time);
                EditorGUI.DrawRect(new Rect(x, axis.yMax - 4, 1, 4), Color.gray);
                GUI.Label(new Rect(x + 2, 0, 60, RouteRowHeight), time.ToString("0.##") + "s", EditorStyles.miniLabel);
            }
            Event current = Event.current;
            for (int i = 0; i < m_RouteRows.Count; i++)
            {
                RouteRow row = m_RouteRows[i];
                Rect line = new(0, (i + 1) * RouteRowHeight, rect.width, RouteRowHeight);
                if (row.Source == SourceAsset)
                    EditorGUI.DrawRect(line, new Color(.22f, .26f, .32f));
                GUI.Label(new Rect(4 + row.Depth * 10, line.y, RouteLabelWidth - 8 - row.Depth * 10, RouteRowHeight),
                    new GUIContent(row.Label, "双击把编辑时间轴切到该 Timeline"), EditorStyles.miniLabel);
                foreach (RouteBar bar in row.Bars)
                {
                    float left = RouteX(axis, bar.Start);
                    float right = RouteX(axis, bar.End);
                    if (bar.UnplayedEnd > bar.End)
                        EditorGUI.DrawRect(new Rect(right, line.y + 4, Mathf.Max(1, RouteX(axis, bar.UnplayedEnd) - right),
                            RouteRowHeight - 8), new Color(.35f, .45f, .55f, .35f));
                    Rect body = new(left, line.y + 3, Mathf.Max(2, right - left), RouteRowHeight - 6);
                    EditorGUI.DrawRect(body, row.Depth == 0 ? new Color(.3f, .55f, .78f) : new Color(.45f, .62f, .4f));
                    GUI.Label(body, new GUIContent(string.Empty, bar.Tooltip));
                }
                if (current.type == EventType.MouseDown && current.button == 0 && current.clickCount == 2 &&
                    line.Contains(current.mousePosition) && row.Source)
                {
                    TimelineAsset target = row.Source;
                    EditorApplication.delayCall += () => OpenRouteTimeline(target);
                    current.Use();
                }
            }
            float cursor = RouteX(axis, preview.Time);
            EditorGUI.DrawRect(new Rect(cursor, 0, 1, height), new Color(1f, .7f, .2f));
            Rect timeArea = Rect.MinMaxRect(axis.xMin, 0, axis.xMax, height);
            if ((current.type == EventType.MouseDown && current.clickCount == 1 || current.type == EventType.MouseDrag) &&
                current.button == 0 && timeArea.Contains(current.mousePosition))
            {
                preview.SetTime((current.mousePosition.x - axis.x) / axis.width * m_RouteEnd);
                current.Use();
            }
        }

        void DisposeRouteRowsContent()
        {
            m_RouteRows.Clear();
            m_RouteRevision = -1;
        }

        float RouteX(Rect axis, float time) => axis.x + Mathf.Clamp01(time / m_RouteEnd) * axis.width;

        // 切到路线上的另一条 Timeline，来处记为可返回的导航。
        void OpenRouteTimeline(TimelineAsset target)
        {
            if (!target || target == SourceAsset)
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
                m_Toolbar.BackButton.tooltip = "预览路线进入了 " + target.name + "；点此回到原 Timeline。";
            }
        }
    }
}
