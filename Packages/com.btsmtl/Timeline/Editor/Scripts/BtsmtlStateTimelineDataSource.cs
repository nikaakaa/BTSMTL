using System.Collections.Generic;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;

namespace BTSMTL.Timeline.Editor
{
    // 状态节点时间条：每条出边一行"按键 → 目标"，只画其可走时段；随时可走的行画满整条。
    // 拖动时段就是拖提供所读状态的 Clip：松手时经 TimelineData.ApplyModify 一次写回并形成一个 Undo。
    public sealed class BtsmtlStateTimelineDataSource : BtsmtlTimeBarDataSource
    {
        const double MinimumLength = .001;
        readonly List<BtsmtlTimeBarRow> m_Rows = new();
        readonly List<BtsmtlTimeBarItem> m_Items = new();
        readonly Dictionary<OrchestrationStaticTransition, BtsmtlTimeBarItem> m_First = new();
        readonly List<BtsmtlTimeBarItem> m_Linked = new();
        readonly TimelineOrchestrationQuery m_Context;
        TimelineAsset m_EditAsset;
        Clip m_EditClip;
        BtsmtlTimeBarEditKind m_EditKind;
        double m_EditStart;
        double m_EditEnd;

        public BtsmtlStateTimelineDataSource(TimelineOrchestrationQuery context, OrchestrationStaticState state,
            long revision)
        {
            m_Context = context;
            Windows = OrchestrationStateTransitionWindows.Build(state);
            Revision = revision;
            float duration = state.Timeline.Duration.AsSingle;
            foreach (OrchestrationTransitionWindow window in Windows.Windows)
            {
                int row = m_Rows.Count;
                string id = window.Transition.Location.EdgeId;
                m_Rows.Add(new BtsmtlTimeBarRow(id, -1, BtsmtlTimeBarItemKind.Window, window.RowLabel));
                string tooltip = window.EdgeLabel + (window.SoleProvider != null
                    ? $"\n提供状态的 Clip：{window.SoleProvider.ClipId}\n拖两端改起止、拖中间整体平移；双击定位该 Clip"
                    : window.Providers.Count > 1 ? $"\n由 {window.Providers.Count} 个 Clip 共同形成" : string.Empty);
                if (window.Anytime)
                {
                    Add(window, new BtsmtlTimeBarItem(id, row, BtsmtlTimeBarItemKind.Window, 0, duration,
                        window.RowLabel + " · 随时", tooltip, window));
                    continue;
                }
                for (int index = 0; index < window.Intervals.Count; index++)
                {
                    TimelineStaticInterval interval = window.Intervals[index];
                    Add(window, new BtsmtlTimeBarItem(id + ":" + index, row, BtsmtlTimeBarItemKind.Window,
                        interval.Start.AsSingle, interval.End.AsSingle, window.RowLabel, tooltip, window));
                }
            }
        }

        public OrchestrationStateTransitionWindows Windows { get; }
        public override IReadOnlyList<BtsmtlTimeBarRow> Rows => m_Rows;
        public override IReadOnlyList<BtsmtlTimeBarItem> Items => m_Items;
        public override double StartTime => 0;
        public override double EndTime => Windows.Timeline.Duration.AsSingle;
        // 资产预览正停在该状态时为其 Timeline 本地时刻，否则为 -1 不画游标。
        public override double CurrentTime { get; set; } = -1;
        public override long Revision { get; }

        // 连线从该转移的第一段可走时段出发；无共同时段时为 null。
        public BtsmtlTimeBarItem Item(OrchestrationStaticTransition transition) =>
            m_First.TryGetValue(transition, out BtsmtlTimeBarItem item) ? item : null;

        public TimelineAsset ResolveTimeline() => m_Context.ResolveTimeline(Windows.Timeline.Identity);

        public override void Navigate(BtsmtlTimeBarItem item)
        {
            if (item.Context is not OrchestrationTransitionWindow window)
                return;
            if (window.SoleProvider != null)
                m_Context.OpenClip(window.SoleProvider.TimelineIdentity, window.SoleProvider.ClipId);
            else
                m_Context.OpenTransition(window.Transition.Location);
        }

        public override string GetReadOnlyReason(BtsmtlTimeBarItem item)
        {
            if (item.Context is not OrchestrationTransitionWindow window)
                return "此时间条只读。";
            if (window.Anytime)
                return "该转移不读 Clip 提供的状态，随时可走，没有可拖的时段。";
            if (window.StateKeys.Count > 1)
                return "该时段是多个状态的交集，请在 Timeline 中分别调整提供各状态的 Clip。";
            if (window.SoleProvider == null)
                return $"该时段由 {window.Providers.Count} 个 Clip 拼成，请在 Timeline 中分别调整。";
            return null;
        }

        public override bool TryBeginEdit(BtsmtlTimeBarItem item, BtsmtlTimeBarEditKind kind, out string reason)
        {
            reason = GetReadOnlyReason(item);
            if (reason != null)
                return false;
            var window = (OrchestrationTransitionWindow)item.Context;
            OrchestrationStaticClip provider = window.SoleProvider;
            m_EditAsset = m_Context.ResolveTimeline(provider.TimelineIdentity);
            m_EditClip = FindClip(m_EditAsset, provider.TrackId, provider.ClipId);
            if (m_EditClip == null)
            {
                m_EditAsset = null;
                reason = "作者 Timeline 已不含提供该状态的 Clip，请等待重新查询。";
                return false;
            }
            m_EditKind = kind;
            m_EditStart = m_EditClip.StartTime.AsSingle;
            m_EditEnd = m_EditClip.EndTime.AsSingle;
            m_Linked.Clear();
            var shared = new List<string>();
            foreach (OrchestrationTransitionWindow other in Windows.ProvidedBy(provider))
            {
                if (other != window)
                    shared.Add(other.RowLabel);
                foreach (BtsmtlTimeBarItem candidate in m_Items)
                    if (candidate.Context == other)
                    {
                        candidate.ProviderHighlighted = true;
                        m_Linked.Add(candidate);
                    }
            }
            reason = shared.Count != 0
                ? $"改的是 Clip '{m_EditClip.Name}'；同时改变：{string.Join("、", shared)}。"
                : $"改的是 Clip '{m_EditClip.Name}'。";
            return true;
        }

        public override bool TrySetRange(BtsmtlTimeBarItem item, double start, double end, out string message)
        {
            if (m_EditKind == BtsmtlTimeBarEditKind.Move)
            {
                if (start < 0)
                {
                    end -= start;
                    start = 0;
                }
            }
            else if (m_EditKind == BtsmtlTimeBarEditKind.TrimStart)
                start = System.Math.Min(System.Math.Max(0, start), end - MinimumLength);
            else
                end = System.Math.Max(end, start + MinimumLength);
            foreach (Clip other in m_EditClip.Track.Clips)
                if (other != m_EditClip && start < other.EndTime.AsSingle && other.StartTime.AsSingle < end)
                {
                    message = $"Track '{m_EditClip.Track.Name}' 上会与 Clip '{other.Name}' 重叠。";
                    return true;
                }
            foreach (BtsmtlTimeBarItem linked in m_Linked)
            {
                linked.Start = start;
                linked.End = end;
                linked.Pending = true;
            }
            message = $"Clip '{m_EditClip.Name}' {start:0.###}–{end:0.###} s，松手写回";
            return true;
        }

        public override void CompleteEdit()
        {
            if (m_EditClip == null || m_Linked.Count == 0)
            {
                CancelEdit();
                return;
            }
            OrchestrationTime start = OrchestrationTime.FromSeconds(m_Linked[0].Start);
            OrchestrationTime end = OrchestrationTime.FromSeconds(m_Linked[0].End);
            Clip clip = m_EditClip;
            TimelineAsset asset = m_EditAsset;
            Reset();
            if (start == clip.StartTime && end == clip.EndTime)
                return;
            asset.Data.ApplyModify(() => clip.ConfigureTimeRange(start, end), "调整转移可走时段");
        }

        public override void CancelEdit()
        {
            foreach (BtsmtlTimeBarItem linked in m_Linked)
            {
                linked.Pending = false;
                linked.Start = m_EditStart;
                linked.End = m_EditEnd;
            }
            Reset();
        }

        void Reset()
        {
            foreach (BtsmtlTimeBarItem linked in m_Linked)
                linked.ProviderHighlighted = false;
            m_Linked.Clear();
            m_EditAsset = null;
            m_EditClip = null;
        }

        static Clip FindClip(TimelineAsset asset, string trackId, string clipId)
        {
            if (!asset)
                return null;
            foreach (Track track in asset.Data.Tracks)
                if (track.AuthoringId == trackId)
                    foreach (Clip clip in track.Clips)
                        if (clip.AuthoringId == clipId)
                            return clip;
            return null;
        }

        void Add(OrchestrationTransitionWindow window, BtsmtlTimeBarItem item)
        {
            m_Items.Add(item);
            m_First.TryAdd(window.Transition, item);
        }
    }
}
