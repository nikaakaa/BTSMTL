using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using Slate;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    public sealed partial class TimelineEditorWindow
    {
        [Serializable]
        sealed class TimelineNavigationFrame
        {
            public TimelineAsset Source;
            public string Graph;
            public string Node;
            public string Ownership;
            public BtsmtlSlateTimelineViewState View;
            public List<string> Expanded;
            public int ContextIndex;
        }

        [SerializeField] List<TimelineNavigationFrame> m_TimelineNavigation = new();
        [SerializeField] List<string> m_NestedExpanded = new();
        readonly Dictionary<string, List<EmbeddedTimelineSubTimeline>> m_NestedViews = new(StringComparer.Ordinal);
        (string Owner, int Operation)? m_NestedContextRequest;

        void RefreshNestedViews()
        {
            m_NestedViews.Clear();
            if (m_SlateProjection == null)
                return;
            TimelineAssetPreviewHost preview = TimelineAssetPreviewHost.Shared;
            preview.TryGetPlayback(SourceAsset, out TimelinePreviewRouteSegment[] parents);
            foreach (TimelineOrchestrationQuery query in m_TimelineQueries)
                foreach (OrchestrationStaticTimelineCall call in query.Query.TimelineCalls)
                {
                    if (query.ResolveTimeline(call.Caller.Identity) != SourceAsset)
                        continue;
                    if (parents.Length == 0 && !MatchesSelectedCaller(query, call))
                        continue;
                    if (parents.Length != 0 && (parents[0].State != null || parents[0].Call != null) &&
                        call.CallerOperationIndex != (parents[0].Call?.OperationIndex ?? parents[0].State.TimelineOperationIndex))
                        continue;
                    if (!m_NestedViews.TryGetValue(call.CallerTrackId, out List<EmbeddedTimelineSubTimeline> views))
                        m_NestedViews.Add(call.CallerTrackId, views = new());
                    views.Add(BuildNestedView(query, call, 0f, parents, parents,
                        new HashSet<string>(StringComparer.Ordinal) { call.Caller.Identity },
                        query.Owner + "/" + call.OperationIndex));
                }
            m_SlateProjection.SetNestedViews(m_NestedViews);
            m_SlateSurface?.MarkDirtyRepaint();
        }

        EmbeddedTimelineSubTimeline BuildNestedView(TimelineOrchestrationQuery query,
            OrchestrationStaticTimelineCall call, float parentOffset, TimelinePreviewRouteSegment[] callers,
            TimelinePreviewRouteSegment[] roots, HashSet<string> chain, string path)
        {
            TimelineAsset source = query.ResolveTimeline(call.CalleeIdentity);
            bool cycle = chain.Contains(call.CalleeIdentity);
            string name = source ? source.name : call.CalleeIdentity;
            string note = cycle ? "循环调用，已在当前路径出现，截止展开。" : call.Reason;
            var view = new EmbeddedTimelineSubTimeline
            {
                Label = $"↳ {name} · {call.Hook}" + (cycle ? " · 循环调用" : call.HasStaticOffset ? $" · {call.Offset.AsSingle:0.###} s" : " · 时刻待会话确认"),
                Tooltip = $"调用元素 {call.CallerElementId}\n父视图只读；双击进入原 Timeline 编辑。\n{note}",
                Expanded = m_NestedExpanded.Contains(path),
                SetExpanded = expanded =>
                {
                    if (expanded) m_NestedExpanded.Add(path);
                    else m_NestedExpanded.Remove(path);
                },
                ExplainReadOnly = () => ShowNotification(new GUIContent("父视图中的被调内容只读，请双击进入 " + name + " 编辑。")),
                Open = () =>
                {
                    if (source) QueueOpenNestedTimeline(query, call, source);
                    else OnAuthoringIssue("被调 Timeline 无法解析：" + call.CalleeIdentity);
                }
            };
            TimelinePreviewRouteSegment[] played = TimelineAssetPreviewHost.Shared.Route.Where(segment =>
                segment.Source == source && segment.CallerClipId == call.CallerElementId &&
                callers.Any(caller => caller.Playback == segment.CallerPlayback) &&
                (segment.Recorded is not { } record || record.OperationIndex == call.OperationIndex)).ToArray();
            if (!source || call.Callee == null || cycle || !call.HasStaticOffset && played.Length == 0)
                return view;
            float offset = parentOffset + call.Offset.AsSingle - call.CalleeStartTime.AsSingle;
            foreach (Track track in source.Data.Tracks)
            {
                var row = new EmbeddedTimelineNestedRow { Label = track.Name, Color = track.Color() };
                foreach (Clip clip in track.Clips)
                {
                    var item = new EmbeddedTimelineNestedClip
                    {
                        Label = clip.Name,
                        Tooltip = $"{source.name} / {track.Name} / {clip.Name}\n本地 {clip.StartTime.AsSingle:0.###}–{clip.EndTime.AsSingle:0.###} s · 只读",
                        Start = offset + clip.StartTime.AsSingle,
                        End = offset + clip.EndTime.AsSingle
                    };
                    foreach (TimelinePreviewRouteSegment segment in played)
                    {
                        OrchestrationTime from = OrchestrationTime.Max(clip.StartTime, segment.LocalStart);
                        OrchestrationTime until = OrchestrationTime.Min(clip.EndTime, segment.LocalEnd);
                        if (until < from || !segment.TryGlobal(from, out OrchestrationTime start, out _) ||
                            !segment.TryGlobal(until, out OrchestrationTime end, out _))
                            continue;
                        foreach (TimelinePreviewRouteSegment root in roots)
                        {
                            OrchestrationTime begin = OrchestrationTime.Max(start, root.Start);
                            OrchestrationTime finish = OrchestrationTime.Min(end, root.End);
                            if (finish >= begin && root.TryLocal(begin, out OrchestrationTime localStart, out _) &&
                                root.TryLocal(finish, out OrchestrationTime localEnd, out _))
                                item.Visited.Add(new Vector2(localStart.AsSingle, localEnd.AsSingle));
                        }
                    }
                    if (call.HasStaticOffset)
                        row.Clips.Add(item);
                    else if (item.Visited.Count != 0)
                    {
                        item.Start = item.Visited.Min(value => value.x);
                        item.End = item.Visited.Max(value => value.y);
                        row.Clips.Add(item);
                    }
                }
                view.Rows.Add(row);
            }
            chain.Add(call.CalleeIdentity);
            foreach (OrchestrationStaticTimelineCall child in call.Callee.Calls)
                if (child.CallerOperationIndex == call.OperationIndex)
                    view.Children.Add(BuildNestedView(query, child, offset, played, roots, chain,
                        path + "/" + child.OperationIndex));
            chain.Remove(call.CalleeIdentity);
            return view;
        }

        bool TryOpenNestedClip(Clip clip)
        {
            var calls = new List<(TimelineOrchestrationQuery Query, OrchestrationStaticTimelineCall Call)>();
            foreach (TimelineOrchestrationQuery query in m_TimelineQueries)
                foreach (OrchestrationStaticTimelineCall call in query.Query.TimelineCalls)
                    if (call.CallerElementId == clip.AuthoringId && query.ResolveTimeline(call.Caller.Identity) == SourceAsset &&
                        MatchesSelectedCaller(query, call))
                        calls.Add((query, call));
            if (calls.Count == 0)
                return false;
            if (calls.Count == 1)
            {
                var item = calls[0];
                QueueOpenNestedTimeline(item.Query, item.Call, item.Query.ResolveTimeline(item.Call.CalleeIdentity));
            }
            else
            {
                var menu = new GenericMenu();
                foreach (var item in calls)
                {
                    TimelineAsset target = item.Query.ResolveTimeline(item.Call.CalleeIdentity);
                    menu.AddItem(new GUIContent($"{item.Query.Label} / {target.name} · {item.Call.Hook} · {item.Call.OperationIndex}"),
                        false, () => QueueOpenNestedTimeline(item.Query, item.Call, target));
                }
                menu.ShowAsContext();
            }
            return true;
        }

        bool MatchesSelectedCaller(TimelineOrchestrationQuery query, OrchestrationStaticTimelineCall call)
        {
            if (m_StateContexts.Count == 0)
                return true;
            var context = m_StateContexts[m_StateContextIndex];
            return query.Owner == context.Query.Owner &&
                call.CallerOperationIndex == (context.Call?.OperationIndex ?? context.State.TimelineOperationIndex);
        }

        void QueueOpenNestedTimeline(TimelineOrchestrationQuery query, OrchestrationStaticTimelineCall call, TimelineAsset source) =>
            EditorApplication.delayCall += () => OpenNestedTimeline(query, call, source);

        void OpenNestedTimeline(TimelineOrchestrationQuery query, OrchestrationStaticTimelineCall call, TimelineAsset source)
        {
            string note;
            if (m_OrchestrationHost.TryGetOrchestrationQueries(source.Data, out IReadOnlyList<TimelineOrchestrationQuery> queries,
                out string reason))
            {
                int references = queries.Sum(value => value.Query.TimelineCalls.Count(candidate =>
                    value.ResolveTimeline(candidate.CalleeIdentity) == source) + value.Query.States.Count(state =>
                    value.ResolveTimeline(state.TimelineIdentity) == source));
                note = $"所选角色配置另有 {Math.Max(0, references - 1)} 处引用；修改此 Timeline 会影响这些引用。";
            }
            else
                note = "其它引用查询未完成：" + reason;
            NavigateTimeline(source, note);
            m_NestedContextRequest = (query.Owner, call.OperationIndex);
        }

        void NavigateTimeline(TimelineAsset target, string note)
        {
            int existing = m_TimelineNavigation.FindIndex(frame => frame.Source == target);
            if (existing >= 0)
            {
                ReturnToTimeline(existing);
                return;
            }
            m_TimelineNavigation.Add(new TimelineNavigationFrame
            {
                Source = SourceAsset, Graph = m_SourceGraphAuthoringId, Node = m_SourceNodeGuid,
                Ownership = m_OwnershipLabel, View = m_SlateProjection.CaptureViewState(),
                Expanded = new List<string>(m_NestedExpanded), ContextIndex = m_StateContextIndex
            });
            m_NestedExpanded.Clear();
            BindAsset(target, string.Empty, string.Empty);
            if (note.Length != 0)
                ShowNotification(new GUIContent(note));
        }

        void ReturnToTimeline(int index)
        {
            TimelineNavigationFrame frame = m_TimelineNavigation[index];
            m_TimelineNavigation.RemoveRange(index, m_TimelineNavigation.Count - index);
            m_NestedExpanded = new List<string>(frame.Expanded);
            m_StateContextIndex = frame.ContextIndex;
            Bind(frame.Source.Data, frame.Source, "m_Data", frame.Ownership, frame.Node, frame.Graph);
            EditorApplication.delayCall -= RefreshStateQueries;
            RefreshStateQueries();
            m_SlateProjection.RestoreViewState(frame.View);
        }

        void AddTimelineBreadcrumbs(VisualElement header)
        {
            if (!HasTimelineNavigation)
                return;
            var row = new VisualElement { name = "timeline-call-breadcrumbs" };
            row.style.flexDirection = FlexDirection.Row;
            for (int i = 0; i < m_TimelineNavigation.Count; i++)
            {
                int index = i;
                row.Add(new Button(() => ReturnToTimeline(index)) { text = m_TimelineNavigation[i].Source.name + " ›" });
            }
            row.Add(new Label(SourceAsset.name));
            header.Add(row);
        }
    }
}
