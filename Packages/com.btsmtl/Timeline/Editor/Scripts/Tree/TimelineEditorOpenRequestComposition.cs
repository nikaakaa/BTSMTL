using System;

namespace BTSMTL.Timeline.Editor
{
    public static class TimelineEditorOpenRequestComposition
    {
        public static TimelineEditorOpenRequest Create(
            TimelineData timeline,
            UnityEngine.Object serializedOwner,
            string serializedPropertyPath,
            string ownershipLabel,
            TimelineContractCatalog contracts,
            ITimelineEditorRuntimeDebugBinding runtimeDebugBinding = null,
            ITimelineTimeRangePreview timeRangePreview = null)
        {
            return new TimelineEditorOpenRequest(
                timeline,
                serializedOwner,
                serializedPropertyPath,
                ownershipLabel,
                runtimeDebugBinding,
                contracts, TimelineWorkspaceModeBridge.Authoring,
                timeRangePreview: timeRangePreview);
        }
    }
}
