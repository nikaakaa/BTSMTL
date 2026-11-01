using System;

namespace BTSMTL.Timeline.Editor
{
    public static class TimelineAuthoringSave
    {
        public static event Action<string[]> Saving;

        public static void Save(UnityEngine.Object source) => TimelineAuthoringGraphBindings.Document(source).Save();

        internal static void NotifySaving(string path) => Saving?.Invoke(new[] { path });
    }
}
