using System;

namespace BTSMTL.Timeline.Editor
{
    public static class TimelineAuthoringSave
    {
        public static event Action<string[]> Saving;

        public static Func<UnityEngine.Object, ITimelineAuthoringDocument> Document { get; set; }

        public static void Save(UnityEngine.Object source) => Document(source).Save();

        internal static void NotifySaving(string path) => Saving?.Invoke(new[] { path });
    }
}
