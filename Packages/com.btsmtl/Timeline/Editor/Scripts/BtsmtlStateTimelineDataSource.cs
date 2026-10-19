using System;
using System.Collections.Generic;
using BTSMTL.Compilation.Analysis;

namespace BTSMTL.Timeline.Editor
{
    public sealed class BtsmtlStateTimelineDataSource : BtsmtlTimeBarDataSource
    {
        readonly List<BtsmtlTimeBarRow> m_Rows = new();
        readonly List<BtsmtlTimeBarItem> m_Items = new();
        readonly Dictionary<string, BtsmtlTimeBarItem> m_Doors = new(StringComparer.Ordinal);
        readonly TimelineOrchestrationQuery m_Context;

        public BtsmtlStateTimelineDataSource(TimelineOrchestrationQuery context, OrchestrationStaticState state,
            long revision)
        {
            m_Context = context;
            Doors = TimelineStateDoors.Build(state);
            Revision = revision;
            m_Rows.Add(new BtsmtlTimeBarRow("sections", -1, BtsmtlTimeBarItemKind.Section, "分段"));
            m_Rows.Add(new BtsmtlTimeBarRow("doors", -1, BtsmtlTimeBarItemKind.Door, "门"));
            m_Rows.Add(new BtsmtlTimeBarRow("jumps", -1, BtsmtlTimeBarItemKind.SectionJump, "分段跳转"));
            OrchestrationStaticTimeline timeline = state.Timeline;
            foreach (OrchestrationStaticSection section in timeline.Sections)
                m_Items.Add(new BtsmtlTimeBarItem(section.AuthoringId, 0, BtsmtlTimeBarItemKind.Section,
                    section.Start.AsSingle, section.End.AsSingle, SectionName(section.AuthoringId),
                    $"分段：{SectionName(section.AuthoringId)}", section));
            foreach (OrchestrationStaticClip clip in Doors.Doors)
            {
                var labels = new List<string>();
                foreach (TimelineDoorDestination destination in Doors.DestinationsOf(clip))
                    labels.Add(destination.Kind == TimelineDoorDestinationKind.NextSection
                        ? "→" + SectionName(destination.NextSection.AuthoringId)
                        : destination.Label);
                string label = string.Join("、", labels);
                var item = new BtsmtlTimeBarItem(clip.ClipId, 1, BtsmtlTimeBarItemKind.Door,
                    clip.Start.AsSingle, clip.End.AsSingle, label,
                    $"门：{clip.ClipId}\n{label}\n点击定位 Timeline Clip", clip);
                m_Doors.Add(clip.ClipId, item);
                m_Items.Add(item);
            }
            foreach (TimelineDoorDestination destination in Doors.Destinations)
                if (destination.Kind == TimelineDoorDestinationKind.NextSection)
                    m_Items.Add(new BtsmtlTimeBarItem(destination.Door.ClipId + ":continue", 2,
                        BtsmtlTimeBarItemKind.SectionJump, destination.Door.Start.AsSingle,
                        destination.NextSection.Start.AsSingle,
                        "→" + SectionName(destination.NextSection.AuthoringId),
                        "分段跳转：点击定位门 Clip", destination));
        }

        public TimelineStateDoors Doors { get; }
        public override IReadOnlyList<BtsmtlTimeBarRow> Rows => m_Rows;
        public override IReadOnlyList<BtsmtlTimeBarItem> Items => m_Items;
        public override double StartTime => 0;
        public override double EndTime => Doors.Timeline.Duration.AsSingle;
        public override double CurrentTime { get; set; }
        public override long Revision { get; }

        public BtsmtlTimeBarItem Door(string clipId) => m_Doors[clipId];

        public override void Navigate(BtsmtlTimeBarItem item)
        {
            if (item.Context is OrchestrationStaticClip clip)
                m_Context.OpenClip(clip.TimelineIdentity, clip.ClipId);
            else if (item.Context is TimelineDoorDestination destination)
                m_Context.OpenClip(destination.Door.TimelineIdentity, destination.Door.ClipId);
        }

        string SectionName(string identity)
        {
            foreach (TimelineSection section in m_Context.ResolveTimeline(Doors.Timeline.Identity).Data.Sections)
                if (section.AuthoringId == identity)
                    return section.Name;
            return identity;
        }
    }
}
