using System;

namespace BTSMTL.Timeline.Editor
{
    public static class TimelineAuthoringSave
    {
        public static event Action<string[]> Saving;

        internal static void NotifySaving(string path) => Saving?.Invoke(new[] { path });
    }
}
