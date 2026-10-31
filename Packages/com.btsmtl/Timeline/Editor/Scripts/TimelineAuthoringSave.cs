using System;

namespace BTSMTL.Timeline.Editor
{
    public static class TimelineAuthoringSave
    {
        public static event Action<string[]> Saving;

        public static ITimelineAuthoringDocument Document(UnityEngine.Object source) =>
            TimelineWorkspaceModeBridge.Authoring.GetAuthoringDocument(source);

        public static void Save(UnityEngine.Object source) => Document(source).Save();

        internal static void NotifySaving(string path) => Saving?.Invoke(new[] { path });
    }
}
