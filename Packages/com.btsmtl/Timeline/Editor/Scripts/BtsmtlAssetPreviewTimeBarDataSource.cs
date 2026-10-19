using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using UnityEditor;

namespace BTSMTL.Timeline.Editor
{
    public sealed class BtsmtlAssetPreviewTimeBarDataSource : BtsmtlTimeBarDataSource
    {
        sealed class ClipSource
        {
            internal TimelineAssetPreviewTraversal Traversal;
            internal string TrackId;
            internal string ClipId;
            internal TimelineClipTimeRange Original;
            internal double DisplayStart;
            internal double DisplayEnd;
        }

        readonly TimelineAssetPreviewHost m_Host;
        readonly List<BtsmtlTimeBarRow> m_Rows = new();
        readonly List<BtsmtlTimeBarItem> m_Items = new();
        TimelineAsset m_EditSource;
        TimelineClipTimeEditBuffer m_EditBuffer;
        TimelineClipTimeRange[] m_Ranges;
        ulong m_EditRevision;
        int m_UndoGroup = -1;
        BtsmtlTimeBarEditKind m_EditKind;
        TimelinePreviewInputPoint m_InputEdit;
        float m_InputOriginalTime;
        long m_Revision;

        public BtsmtlAssetPreviewTimeBarDataSource(TimelineAssetPreviewHost host) => m_Host = host;
        public override IReadOnlyList<BtsmtlTimeBarRow> Rows => m_Rows;
        public override IReadOnlyList<BtsmtlTimeBarItem> Items => m_Items;
        public override double StartTime => 0d;
        public override double EndTime => m_Host.Preferences.RangeEnd;
        public override double CurrentTime { get => m_Host.Time; set => m_Host.SetTime((float)value); }
        public override long Revision => m_Revision;

        public void Rebuild()
        {
            m_Rows.Clear();
            m_Items.Clear();
            int stateRow = -1;
            BtsmtlTimeBarItem stateItem = null;
            int previousState = -1;
            for (int i = 0; i < m_Host.Traversals.Count; i++)
            {
                TimelineAssetPreviewTraversal span = m_Host.Traversals[i];
                TimelineStaticChainSpan inferred = m_Host.Chain?.Spans[i];
                int operation = inferred == null ? 0 : inferred.State.OperationIndex;
                if (stateRow < 0 || operation != previousState || span.Transition != null)
                {
                    previousState = operation;
                    stateRow = AddRow($"state:{i}", -1, BtsmtlTimeBarItemKind.State,
                        inferred == null ? span.Source.name : inferred.State.Name);
                    stateItem = AddItem($"state:{i}", stateRow, BtsmtlTimeBarItemKind.State,
                        span.Start.AsSingle, span.End.AsSingle, m_Rows[stateRow].Label, "静态推导状态", inferred?.State);
                }
                else
                    stateItem.End = span.End.AsSingle;
                string sectionName = inferred?.Section == null ? span.Source.Data.Sections
                    .Where(section => section.Time <= span.LocalStart).OrderBy(section => section.Time)
                    .LastOrDefault()?.Name ?? "Timeline" : span.Source.Data.Sections
                    .First(section => section.AuthoringId == inferred.Section.AuthoringId).Name;
                int sectionRow = AddRow($"section:{i}", stateRow, BtsmtlTimeBarItemKind.Section, sectionName);
                AddItem($"section:{i}", sectionRow, BtsmtlTimeBarItemKind.Section,
                    span.Start.AsSingle, span.End.AsSingle, sectionName,
                    inferred?.Recovery == true ? "继续门未成立，播放到分段终点收招。" : "静态推导分段", inferred?.Section);
                var doors = new HashSet<string>(StringComparer.Ordinal);
                if (inferred != null)
                    foreach (TimelineDoorDestination door in TimelineStateDoors.Build(inferred.State).Destinations)
                        doors.Add(door.Door.ClipId);
                foreach (var track in span.Content.Content.Tracks)
                {
                    int trackRow = AddRow($"track:{i}:{track.AuthoringId}", sectionRow,
                        BtsmtlTimeBarItemKind.Playback, track.Name);
                    foreach (var clip in span.Content.Content.Clips)
                    {
                        if (clip.TrackAuthoringId != track.AuthoringId || clip.TrackMuted ||
                            clip.TimeRange.End <= span.LocalStart || clip.TimeRange.Start >= span.LocalEnd)
                            continue;
                        Clip authoringClip = span.Source.Data.Tracks.First(value => value.AuthoringId == track.AuthoringId)
                            .Clips.First(value => value.AuthoringId == clip.AuthoringId);
                        bool tree = authoringClip is ITimelineOwnedAuthoringIdentity;
                        string graphMode = !tree ? string.Empty : clip.ExecutionPolicy.IsPresentation
                            ? "表现图" : "逻辑条件按输入与假设推导";
                        bool door = doors.Contains(clip.AuthoringId);
                        double displayStart = OrchestrationTime.Max(span.Start,
                            span.Start + clip.TimeRange.Start - span.LocalStart).AsSingle;
                        double displayEnd = OrchestrationTime.Min(span.End,
                            span.Start + clip.TimeRange.End - span.LocalStart).AsSingle;
                        var source = new ClipSource
                        {
                            Traversal = span, TrackId = track.AuthoringId, ClipId = clip.AuthoringId,
                            Original = clip.TimeRange, DisplayStart = displayStart, DisplayEnd = displayEnd
                        };
                        AddItem($"clip:{i}:{clip.AuthoringId}", trackRow,
                            tree ? BtsmtlTimeBarItemKind.TreeClip : BtsmtlTimeBarItemKind.Clip,
                            displayStart, displayEnd,
                            authoringClip.Name + (tree ? " · " + graphMode : string.Empty),
                            $"{span.Source.name} / {track.Name} / {authoringClip.Name}" +
                            $"\n作者范围 {clip.TimeRange.Start.AsSingle:0.###}–{clip.TimeRange.End.AsSingle:0.###} 秒；此处仅显示实际经过区间。" +
                            (tree ? "\n" + graphMode + "。" : string.Empty), source).GateHighlighted = door;
                    }
                }
            }
            if (m_Host.Chain != null)
            {
                int markerRow = AddRow("markers", -1, BtsmtlTimeBarItemKind.Transition, "转移与分段跳转");
                for (int i = 0; i < m_Host.Chain.Markers.Count; i++)
                {
                    TimelineStaticChainMarker marker = m_Host.Chain.Markers[i];
                    AddItem($"marker:{i}", markerRow,
                        marker.IsSectionJump ? BtsmtlTimeBarItemKind.SectionJump : BtsmtlTimeBarItemKind.Transition,
                        marker.Time.AsSingle, marker.Time.AsSingle,
                        marker.IsSectionJump ? "进入下一分段" : "状态转移",
                        "由输入日程、门与假设推导；双击定位作者来源。", marker);
                }
            }
            foreach (var input in m_Host.Instance.Inputs)
            {
                int row = AddRow("input:" + input.InputId, -1, BtsmtlTimeBarItemKind.Input, "输入 · " + input.InputId);
                foreach (TimelinePreviewInputPoint point in m_Host.Preferences.Inputs)
                    if (point.InputId == input.InputId)
                        AddItem($"input:{m_Host.Preferences.Inputs.IndexOf(point)}", row,
                            BtsmtlTimeBarItemKind.Input, point.Time, point.Time,
                            point.Active ? input.ValueKind == BTSMTL.Graphs.GraphHostValueKind.Bool
                                ? point.Boolean ? "成立" : "不成立" : "取值" : "停用",
                            "拖动修改预览输入时刻，不写作者资产。", point);
            }
            m_Revision++;
        }

        int AddRow(string id, int parent, BtsmtlTimeBarItemKind kind, string label)
        {
            m_Rows.Add(new BtsmtlTimeBarRow(id, parent, kind, label));
            return m_Rows.Count - 1;
        }

        BtsmtlTimeBarItem AddItem(string id, int row, BtsmtlTimeBarItemKind kind, double start,
            double end, string label, string tooltip, object context)
        {
            var item = new BtsmtlTimeBarItem(id, row, kind, start, end, label, tooltip, context);
            m_Items.Add(item);
            return item;
        }

        public override bool TryBeginEdit(BtsmtlTimeBarItem item, BtsmtlTimeBarEditKind kind, out string reason)
        {
            reason = string.Empty;
            m_Host.SetPlaying(false);
            if (item.Context is TimelinePreviewInputPoint point && kind == BtsmtlTimeBarEditKind.Move)
            {
                m_InputEdit = point;
                m_InputOriginalTime = point.Time;
                return true;
            }
            if (item.Context is not ClipSource source)
            {
                reason = GetReadOnlyReason(item);
                return false;
            }
            if (kind == BtsmtlTimeBarEditKind.TrimStart && source.Original.Start < source.Traversal.LocalStart ||
                kind == BtsmtlTimeBarEditKind.TrimEnd && source.Original.End > source.Traversal.LocalEnd)
            {
                reason = "此作者边界位于当前经过区间之外；请在原 Timeline 中裁剪，可在此移动整个 Clip。";
                return false;
            }
            m_EditKind = kind;
            m_EditSource = source.Traversal.Source;
            m_EditBuffer = new TimelineClipTimeEditBuffer(m_EditSource.Data);
            m_Ranges = m_EditBuffer.Ranges.ToArray();
            m_EditRevision = m_EditSource.Data.AuthoringRevision;
            Undo.IncrementCurrentGroup();
            m_UndoGroup = Undo.GetCurrentGroup();
            return true;
        }

        public override bool TrySetRange(BtsmtlTimeBarItem item, double start, double end, out string message)
        {
            if (m_InputEdit != null)
            {
                m_InputEdit.Time = (float)Math.Clamp(start, 0d, m_Host.Preferences.RangeEnd);
                m_Host.PreferencesChanged();
                message = string.Empty;
                return true;
            }
            if (m_EditRevision != m_EditSource.Data.AuthoringRevision)
            {
                message = "作者内容已变化，请结束手势后重新拖动。";
                return false;
            }
            var source = (ClipSource)item.Context;
            TimelineClipTimeRange original = source.Original;
            OrchestrationTime startDelta = OrchestrationTime.FromSeconds(start - source.DisplayStart);
            OrchestrationTime endDelta = OrchestrationTime.FromSeconds(end - source.DisplayEnd);
            OrchestrationTime localStart = m_EditKind == BtsmtlTimeBarEditKind.TrimEnd
                ? original.Start : original.Start + startDelta;
            OrchestrationTime localEnd = m_EditKind == BtsmtlTimeBarEditKind.TrimStart
                ? original.End : original.End + (m_EditKind == BtsmtlTimeBarEditKind.Move ? startDelta : endDelta);
            for (int i = 0; i < m_Ranges.Length; i++)
                if (m_EditBuffer.ClipAt(i).AuthoringId == source.ClipId)
                    m_Ranges[i] = new TimelineClipTimeRange(localStart, localEnd, original.EaseIn,
                        original.EaseOut, original.ClipIn, original.SourceStart, original.SourceEnd,
                        original.OtherEaseIn, original.OtherEaseOut);
            if (!m_EditBuffer.TryValidate(m_Ranges, m_Host.Contracts, out message))
                return false;
            m_EditSource.Data.ApplyModify(() =>
            {
                for (int i = 0; i < m_Ranges.Length; i++)
                    if (m_EditBuffer.ClipAt(i).AuthoringId == source.ClipId)
                        m_EditBuffer.ClipAt(i).ApplyTimeRange(in m_Ranges[i]);
            }, "调整预览链 Clip 时间");
            m_EditRevision = m_EditSource.Data.AuthoringRevision;
            m_EditBuffer.Capture();
            return true;
        }

        public override void CompleteEdit()
        {
            if (m_UndoGroup >= 0)
                Undo.CollapseUndoOperations(m_UndoGroup);
            m_UndoGroup = -1;
            m_EditSource = null;
            m_InputEdit = null;
        }

        public override void CancelEdit()
        {
            if (m_InputEdit != null)
            {
                m_InputEdit.Time = m_InputOriginalTime;
                m_Host.PreferencesChanged();
            }
            if (m_UndoGroup >= 0)
                Undo.RevertAllDownToGroup(m_UndoGroup);
            m_UndoGroup = -1;
            m_EditSource = null;
            m_InputEdit = null;
        }

        public override void Navigate(BtsmtlTimeBarItem item)
        {
            if (item.Context is ClipSource source)
                m_Host.Query?.OpenClip(source.Traversal.Content.Content.Identity, source.ClipId);
            else if (item.Context is TimelineStaticChainMarker marker)
            {
                if (marker.IsSectionJump && marker.Doors.Count != 0)
                    m_Host.Query.OpenClip(marker.Source.TimelineIdentity, marker.Doors[0].ClipId);
                else if (marker.Transition != null)
                    m_Host.Query.OpenTransition(marker.Transition.Location);
            }
            else if (item.Context is OrchestrationStaticState state)
                m_Host.Query.OpenState(state.Location);
        }
    }
}
