using System;

namespace BTSMTL.Timeline.Editor
{
    public static class TimelineEditorOpenRequestComposition
    {
        public static TimelineEditorOpenRequest Create(
            TimelineData timeline,
            TimelineAsset owner,
            string ownershipLabel,
            TimelineContractCatalog contracts,
            ITimelineEditorRuntimeDebugBinding runtimeDebugBinding = null,
            ITimelineTimeRangePreview timeRangePreview = null)
        {
            return new TimelineEditorOpenRequest(
                timeline,
                owner,
                ownershipLabel,
                runtimeDebugBinding,
                contracts, TimelineWorkspaceModeBridge.Authoring,
                timeRangePreview: timeRangePreview);
        }
    }
}
