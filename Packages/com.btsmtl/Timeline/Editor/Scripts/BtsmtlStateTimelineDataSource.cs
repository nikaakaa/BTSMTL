using System.Collections.Generic;
using BTSMTL.Compilation.Analysis;

namespace BTSMTL.Timeline.Editor
{
    // 状态节点时间条：每条出边一行"按键 → 目标"，只画其可走时段；随时可走的行画满整条。
    public sealed class BtsmtlStateTimelineDataSource : BtsmtlTimeBarDataSource
    {
        readonly List<BtsmtlTimeBarRow> m_Rows = new();
        readonly List<BtsmtlTimeBarItem> m_Items = new();
        readonly Dictionary<OrchestrationStaticTransition, BtsmtlTimeBarItem> m_First = new();
        readonly TimelineOrchestrationQuery m_Context;

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
                    ? $"\n提供状态的 Clip：{window.SoleProvider.ClipId}\n点击定位该 Clip"
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
        public override double CurrentTime { get; set; }
        public override long Revision { get; }

        // 连线从该转移的第一段可走时段出发；无共同时段时为 null。
        public BtsmtlTimeBarItem Item(OrchestrationStaticTransition transition) =>
            m_First.TryGetValue(transition, out BtsmtlTimeBarItem item) ? item : null;

        public override void Navigate(BtsmtlTimeBarItem item)
        {
            if (item.Context is not OrchestrationTransitionWindow window)
                return;
            if (window.SoleProvider != null)
                m_Context.OpenClip(window.SoleProvider.TimelineIdentity, window.SoleProvider.ClipId);
            else
                m_Context.OpenTransition(window.Transition.Location);
        }

        void Add(OrchestrationTransitionWindow window, BtsmtlTimeBarItem item)
        {
            m_Items.Add(item);
            m_First.TryAdd(window.Transition, item);
        }
    }
}
