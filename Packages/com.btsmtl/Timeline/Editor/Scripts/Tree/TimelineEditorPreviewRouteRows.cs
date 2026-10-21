using System.Collections.Generic;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    // 编辑时间轴下方的并排路线：每个经过的 Timeline 一行、按预览时刻排布并与编辑时间轴共用刻度，被打断的未播部分淡色标出，
    // 并行分支各占一行；共用预览播放头，点击定位预览时刻，双击一行把编辑时间轴切到该 Timeline。
    public sealed partial class TimelineEditorWindow
    {
        const float RouteRowHeight = 18f;

        sealed class RouteBar
        {
            internal float Start;
            internal float End;
            internal float UnplayedEnd;
            internal string Tooltip;
            // 对应路线中的哪一次播放；不在路线中（如快速档的并行分支）为 -1。
            internal int Playback = -1;
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

        VisualElement CreatePreviewRouteRows()
        {
            m_PreviewRoute = new IMGUIContainer(DrawPreviewRoute) { name = "preview-route-rows" };
            m_PreviewRoute.style.flexShrink = 0f;
            m_RouteRevision = -1;
            RefreshPreviewRouteRows();
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
            if (preview.IsReal || preview.Chain == null)
            {
                // 真实档与无状态上下文的单 Timeline 预览：按经过的 Timeline 分行排布播放段。
                var rows = new Dictionary<TimelineAsset, RouteRow>();
                foreach (TimelinePreviewRouteSegment segment in preview.Route)
                {
                    if (!rows.TryGetValue(segment.Source, out RouteRow row))
                    {
                        row = new RouteRow { Label = (preview.IsReal ? "实际 · " : string.Empty) +
                            (segment.IsNested ? "↳ " : string.Empty) + segment.Source.name, Source = segment.Source };
                        rows.Add(segment.Source, row);
                        m_RouteRows.Add(row);
                    }
                    string state = segment.State == null ? string.Empty : preview.DescribeState(segment.State) + " · ";
                    row.Bars.Add(new RouteBar
                    {
                        Start = segment.Start.AsSingle, End = segment.End.AsSingle, UnplayedEnd = segment.End.AsSingle,
                        Playback = segment.Playback,
                        Tooltip = $"{state}{segment.Label} · {segment.Start.AsSingle:0.000}–{segment.End.AsSingle:0.000} s · 本地 " +
                            $"{segment.LocalStart.AsSingle:0.00}–{segment.LocalEnd.AsSingle:0.00} s · 单击选定这次播放" +
                            (segment.IsNested && segment.CallerPlayback < 0 ? "\n调用方没有可换算的记录，无法归属调用 Clip。" : string.Empty)
                    });
                }
                if (preview.IsReal && preview.FastChain != null)
                    AddRouteChain(preview.Query, preview.FastChain, 0, "快速 · ");
                return;
            }
            AddRouteChain(preview.Query, preview.Chain, 0, string.Empty, preview.Route);
            foreach (TimelinePreviewRouteSegment segment in preview.Route)
                if (segment.IsNested)
                {
                    var row = new RouteRow { Label = "调用 · " + segment.Source.name, Source = segment.Source, Depth = 1 };
                    row.Bars.Add(new RouteBar
                    {
                        Start = segment.Start.AsSingle, End = segment.End.AsSingle, UnplayedEnd = segment.End.AsSingle,
                        Playback = segment.Playback, Tooltip = $"{segment.Label} · 调用元素 {segment.CallerClipId}"
                    });
                    m_RouteRows.Add(row);
                }
        }

        void AddRouteChain(TimelineOrchestrationQuery query, TimelineStaticChain chain, int depth, string prefix,
            IReadOnlyList<TimelinePreviewRouteSegment> route = null)
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
                int playback = -1;
                if (route != null)
                    foreach (TimelinePreviewRouteSegment segment in route)
                        if (segment.Source == row.Source && segment.Start == span.Start)
                            playback = segment.Playback;
                row.Bars.Add(new RouteBar
                {
                    Start = start, End = end, UnplayedEnd = unplayed, Playback = playback,
                    Tooltip = $"{query.DescribeState(span.State)} · 预览 {start:0.00}–{end:0.00} s · 本地 " +
                        $"{span.LocalStart.AsSingle:0.00}–{span.LocalEnd.AsSingle:0.00} s" +
                        (span.Interrupted ? " · 被转移打断，淡色为未播部分" : span.PlaybackCompleted ? " · 播到终点" : string.Empty)
                });
            }
            foreach (TimelineStaticChainBranch branch in chain.Branches)
                AddRouteChain(query, branch.Chain, depth + 1, $"并行 · {query.DescribeState(branch.Owner)} / ", route);
        }

        bool RefreshPreviewRouteRows()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            bool active = preview.IsOpen && preview.Covers(SourceAsset);
            if (!active)
            {
                if (m_RouteRows.Count != 0)
                    DisposeRouteRowsContent();
                m_PreviewRoute.style.height = 0f;
                return false;
            }
            if (m_RouteRevision != preview.Revision)
                RebuildRouteRows();
            m_PreviewRoute.style.height = m_RouteRows.Count * RouteRowHeight;
            return m_RouteRows.Count != 0;
        }

        // 路线行与编辑时间轴共用同一刻度：本轴所选那次播放的时刻与上方时间尺对齐，其它行按预览时刻同步平移。
        void DrawPreviewRoute()
        {
            if (!RefreshPreviewRouteRows() || m_SlateProjection == null ||
                !m_SlateProjection.TryGetTimeAxis(out float axisLeft, out float axisWidth, out float viewMin, out float viewMax))
                return;
            var preview = TimelineAssetPreviewHost.Shared;
            m_RouteAxis = new RouteAxis(axisLeft, axisWidth, viewMin, viewMax - viewMin, RouteOffset(preview));
            float height = m_RouteRows.Count * RouteRowHeight;
            Rect rect = new(0, 0, m_PreviewRoute.contentRect.width, height);
            EditorGUI.DrawRect(rect, new Color(.15f, .15f, .15f));
            float axisRight = axisLeft + axisWidth;
            Event current = Event.current;
            for (int i = 0; i < m_RouteRows.Count; i++)
            {
                RouteRow row = m_RouteRows[i];
                Rect line = new(0, i * RouteRowHeight, rect.width, RouteRowHeight);
                if (row.Source == SourceAsset)
                    EditorGUI.DrawRect(line, new Color(.22f, .26f, .32f));
                GUI.Label(new Rect(4 + row.Depth * 10, line.y, axisLeft - 8 - row.Depth * 10, RouteRowHeight),
                    new GUIContent(row.Label, "双击把编辑时间轴切到该 Timeline"), EditorStyles.miniLabel);
                foreach (RouteBar bar in row.Bars)
                {
                    float left = Mathf.Max(axisLeft, RouteX(bar.Start));
                    float right = Mathf.Min(axisRight, RouteX(bar.End));
                    float unplayed = Mathf.Min(axisRight, RouteX(bar.UnplayedEnd));
                    if (bar.UnplayedEnd > bar.End && unplayed > right)
                        EditorGUI.DrawRect(new Rect(Mathf.Max(axisLeft, right), line.y + 4,
                            unplayed - Mathf.Max(axisLeft, right), RouteRowHeight - 8), new Color(.35f, .45f, .55f, .35f));
                    if (right <= left)
                        continue;
                    Rect body = new(left, line.y + 3, Mathf.Max(2, right - left), RouteRowHeight - 6);
                    EditorGUI.DrawRect(body, row.Depth == 0 ? new Color(.3f, .55f, .78f) : new Color(.45f, .62f, .4f));
                    if (bar.Playback >= 0 && row.Source == SourceAsset && bar.Playback == m_PreviewPathPlayback)
                        EditorGUI.DrawRect(new Rect(body.x, body.yMax - 2, body.width, 2), new Color(1f, .85f, .3f));
                    GUI.Label(body, new GUIContent(string.Empty, bar.Tooltip));
                    // 单击一段选定这次播放：编辑时间轴的输入点与标记都按它定位。
                    if (current.type == EventType.MouseDown && current.button == 0 && current.clickCount == 1 &&
                        bar.Playback >= 0 && body.Contains(current.mousePosition))
                        preview.SelectPlayback(bar.Playback);
                }
                if (current.type == EventType.MouseDown && current.button == 0 && current.clickCount == 2 &&
                    line.Contains(current.mousePosition) && row.Source)
                {
                    TimelineAsset target = row.Source;
                    EditorApplication.delayCall += () => OpenRouteTimeline(target);
                    current.Use();
                }
            }
            if (preview.IsReal)
                foreach (TimelinePreviewDifference difference in preview.Differences)
                {
                    float x = RouteX(difference.Time);
                    if (x < axisLeft || x > axisRight)
                        continue;
                    EditorGUI.DrawRect(new Rect(x - 1, 0, 3, height), new Color(.95f, .3f, .25f));
                    GUI.Label(new Rect(x - 6, 0, 12, RouteRowHeight), new GUIContent(string.Empty, difference.Message));
                }
            float cursor = RouteX(preview.Time);
            if (cursor >= axisLeft && cursor <= axisRight)
                EditorGUI.DrawRect(new Rect(cursor, 0, 1, height), new Color(1f, .7f, .2f));
            Rect timeArea = Rect.MinMaxRect(axisLeft, 0, axisRight, height);
            if ((current.type == EventType.MouseDown && current.clickCount == 1 || current.type == EventType.MouseDrag) &&
                current.button == 0 && timeArea.Contains(current.mousePosition))
            {
                preview.SetTime(Mathf.Max(0f, RouteTime(current.mousePosition.x)));
                current.Use();
            }
        }

        // 本轴所选那次播放（游标所在的一圈优先）把预览时刻换成本地时刻的平移量；本轴不在路线上时按预览时刻直接对齐。
        float RouteOffset(TimelineAssetPreviewHost preview)
        {
            if (!preview.TryGetPlayback(SourceAsset, out TimelinePreviewRouteSegment[] spans))
                return 0f;
            OrchestrationTime cursor = OrchestrationTime.FromSeconds(preview.Time);
            TimelinePreviewRouteSegment selected = spans[0];
            foreach (TimelinePreviewRouteSegment span in spans)
                if (span.Contains(cursor))
                    selected = span;
            return selected.Start.AsSingle - selected.LocalStart.AsSingle;
        }

        void DisposeRouteRowsContent()
        {
            m_RouteRows.Clear();
            m_RouteRevision = -1;
        }

        readonly struct RouteAxis
        {
            internal RouteAxis(float left, float width, float viewMin, float viewSpan, float offset)
            {
                Left = left;
                Width = width;
                ViewMin = viewMin;
                ViewSpan = viewSpan;
                Offset = offset;
            }

            internal readonly float Left;
            internal readonly float Width;
            internal readonly float ViewMin;
            internal readonly float ViewSpan;
            internal readonly float Offset;
        }

        RouteAxis m_RouteAxis;
        Vector4 m_DrawnTimeAxis;

        float RouteX(float time) => m_RouteAxis.Left + (time - m_RouteAxis.Offset - m_RouteAxis.ViewMin) / m_RouteAxis.ViewSpan * m_RouteAxis.Width;

        float RouteTime(float x) => (x - m_RouteAxis.Left) / m_RouteAxis.Width * m_RouteAxis.ViewSpan + m_RouteAxis.ViewMin + m_RouteAxis.Offset;

        // 切到路线上的另一条 Timeline，来处记为可返回的导航。
        void OpenRouteTimeline(TimelineAsset target)
        {
            if (!target || target == SourceAsset)
                return;
            NavigateTimeline(target, string.Empty);
        }
    }
}
